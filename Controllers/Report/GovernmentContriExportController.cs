using Dapper;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class GovernmentContriExportController : Controller
    {
        private readonly IDbConnection _db;

        public GovernmentContriExportController(IDbConnection db) => _db = db;

        // Exports government contribution data to Excel based on report type and filters
        [HttpGet]
        public IActionResult ExportToExcel(string? status, string? branch, string? department,
        string? dateMonth, string? dateYear, int offset = 0, int limit = -1)
        {
            try
            {
                var employeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(employeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                var employeeInfo = GetEmployeeInfo(employeeNo);

                // ✅ Normalize branch: treat empty/null as "ALL"
                if (string.IsNullOrWhiteSpace(branch) || branch == "null")
                    branch = "ALL";

                // ✅ Normalize dateMonth
                if (string.IsNullOrWhiteSpace(dateMonth) || dateMonth == "null")
                    dateMonth = "ALL";

                if (string.IsNullOrWhiteSpace(department) || department == "null")
                    department = "ALL";

                var data = GetGovernmentContriData(status, branch, department, dateMonth, dateYear, offset, limit);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, status ?? "SSSreport", employeeInfo);
                var reportName = GetReportDisplayName(status ?? "SSSreport");
                var fileName = $"{reportName}_{dateYear}_{dateMonth}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // Retrieves employee information for the export header
        private (string EmployeeNo, string EmployeeName) GetEmployeeInfo(string employeeNo)
        {
            // First try to get from s_user table (for system users)
            var userQuery = @"
                SELECT 
                    userCode,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM s_user
                WHERE userCode = @employeeNo
                LIMIT 1";

            var userResult = _db.QueryFirstOrDefault<dynamic>(userQuery, new { employeeNo });

            if (userResult != null)
            {
                return (userResult.userCode, userResult.employeeName);
            }

            // Fallback to e_basicinfo table (for employees)
            var empQuery = @"
                SELECT 
                    employeeNo,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM e_basicinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1";

            var empResult = _db.QueryFirstOrDefault<dynamic>(empQuery, new { employeeNo });

            if (empResult != null)
            {
                return (empResult.employeeNo, empResult.employeeName);
            }

            return (employeeNo, "Unknown User");
        }

        // Retrieves government contribution records based on report type and filters
        private List<Dictionary<string, object>> GetGovernmentContriData(string? status, string? branch,
            string? department, string? dateMonth, string? dateYear, int offset, int limit)
        {
            var query = BuildQueryByReportType(status ?? "SSSreport");
            var parameters = new DynamicParameters();

            parameters.Add("@brcode", string.IsNullOrWhiteSpace(branch) || branch == "null" ? "ALL" : branch);
            parameters.Add("@department", string.IsNullOrWhiteSpace(department) || department == "null" ? "ALL" : department);
            parameters.Add("@dtMonth", string.IsNullOrWhiteSpace(dateMonth) || dateMonth == "null" ? "ALL" : dateMonth);
            parameters.Add("@dtYear", dateYear);

            if (limit > 0)
                query.Append($" LIMIT {limit} OFFSET {offset}");

            var result = _db.Query(query.ToString(), parameters);
            var dataList = new List<Dictionary<string, object>>();

            foreach (var row in result)
            {
                var rowDict = (IDictionary<string, object>)row;
                dataList.Add(rowDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty));
            }

            return dataList;
        }

        // Builds SQL query based on report type
        private StringBuilder BuildQueryByReportType(string reportType)
        {
            var query = new StringBuilder();

            switch (reportType)
            {
                case "SSSreport":
                    query.Append(@"
                        SELECT 
                            pbio.employeeNo AS 'Employee No',
                            CONCAT(ebasic.lastName,' ', IFNULL(ebasic.suffix,''),', ',ebasic.firstName,' ', ebasic.middleName) AS 'Employee Name',
                            ebasic.departmentCode AS 'Department',
                            ROUND(SUM(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200)) + pbio.nonBasicPay + pbio.allowanceTaxable + pbio.otherIncome 
                                - pbio.totalAmountLate - pbio.totalAmountUndertime - pbio.absentAmount)) AS 'Total Pay',
                            ROUND(SUM(pbio.deductionSSSemployee + pbio.deductionWISPemployee),2) AS 'SSS Employee',
                            ROUND(SUM(pbio.deductionSSSemployer + pbio.deductionWISPemployer),2) AS 'SSS Employer',
                            ROUND(SUM(pbio.deductionSSSec),2) AS 'SSS EC',
                            ROUND(SUM(pbio.deductionSSSemployee + pbio.deductionSSSemployer + pbio.deductionWISPemployee + pbio.deductionWISPemployer + pbio.deductionSSSec),2) AS 'SSS Total',
                            ROUND(SUM(pbio.sssLoan),2) AS 'SSS Loan',
                            ROUND(SUM(pbio.sssCalamity),2) AS 'SSS Calamity Loan'
                        FROM p_biometrics pbio
                        JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo
                        WHERE pbio.isActive = 1 AND pbio.statusName='POSTED'
                          
                          AND (@brcode='ALL' OR @brcode IS NULL OR ebasic.branchCode=@brcode)
                          AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                          AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR pbio.dateMonth=@dtMonth)
                          AND (@dtYear IS NULL OR pbio.dateYear=@dtYear)
                        GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode");
                    break;

                case "PHIreport":
                    query.Append(@"
                        SELECT 
                            pbio.employeeNo AS 'Employee No',
                            CONCAT(ebasic.lastName,' ', IFNULL(ebasic.suffix,''),', ',ebasic.firstName,' ', ebasic.middleName) AS 'Employee Name',
                            ebasic.departmentCode AS 'Department',
                            ROUND(SUM(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200))),2) AS 'Total Pay',
                            ROUND(SUM(ROUND(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200)),-2)),2) AS 'Rounded',
                            ROUND(SUM(pbio.deductionPHIemployee),2) AS 'PhilHealth Employee',
                            ROUND(SUM(pbio.deductionPHIemployer),2) AS 'PhilHealth Employer',
                            ROUND(SUM(pbio.deductionPHIemployee + pbio.deductionPHIemployer),2) AS 'PhilHealth Total',
                            DATE_FORMAT(eperson.dateOfBirth, '%Y/%m/%d') AS 'Date of Birth'
                        FROM p_biometrics pbio
                        JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo
                        LEFT JOIN e_personalinfo eperson ON pbio.employeeNo = eperson.employeeNo
                        WHERE pbio.isActive=1 AND pbio.statusName='POSTED'
                          
                          AND (@brcode='ALL' OR ebasic.branchCode=@brcode)
                          AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                          AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR pbio.dateMonth=@dtMonth)
                          AND (@dtYear IS NULL OR pbio.dateYear=@dtYear)
                        GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode");
                    break;

                case "PIFreport":
                    query.Append(@"
                        SELECT 
                            pbio.employeeNo AS 'Employee No',
                            CONCAT(ebasic.lastName,' ', IFNULL(ebasic.suffix,''),', ',ebasic.firstName,' ', ebasic.middleName) AS 'Employee Name',
                            ebasic.departmentCode AS 'Department',
                            ROUND(SUM(pbio.deductionPIFemployee),2) AS 'Pag-IBIG Employee',
                            ROUND(SUM(pbio.deductionPIFemployer),2) AS 'Pag-IBIG Employer',
                            ROUND(SUM(pbio.deductionPIFemployee + pbio.deductionPIFemployer),2) AS 'Pag-IBIG Total',
                            DATE_FORMAT(eperson.dateOfBirth, '%Y/%m/%d') AS 'Date of Birth',
                            ROUND(SUM(pbio.hdmfLoan),2) AS 'HDMF Loan',
                            ROUND(SUM(pbio.hdmfCalamity),2) AS 'HDMF Calamity Loan'
                        FROM p_biometrics pbio
                        JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo
                        LEFT JOIN e_personalinfo eperson ON pbio.employeeNo = eperson.employeeNo
                        WHERE pbio.isActive=1 AND pbio.statusName='POSTED'
                          
                          AND (@brcode='ALL' OR @brcode IS NULL OR ebasic.branchCode=@brcode)
                          AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                          AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR pbio.dateMonth=@dtMonth)
                          AND (@dtYear IS NULL OR pbio.dateYear=@dtYear)
                        GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode");
                    break;

                case "TAXreport":
                    query.Append(@"
                        SELECT 
                            pbio.employeeNo AS 'Employee No',
                            CONCAT(ebasic.lastName, ', ', ebasic.firstname, ' ', IFNULL(ebasic.middleName,''), ' ', IFNULL(ebasic.suffix,'')) AS 'Employee Name',
                            ebasic.departmentCode AS 'Department',
                            epaydet.tinNo AS 'TIN Number',
                            ROUND(SUM(pbio.totalMandatory),4) AS 'Mandatories',
                            ROUND(SUM(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200)) - pbio.totalAmountUndertime - pbio.totalAmountLate - pbio.absentAmount - pbio.totalMandatory),4) AS 'Net Taxable',
                            ROUND(SUM(pbio.withHeldTax),4) AS 'Tax Withheld',
                            DATE_FORMAT(eperson.dateofbirth,'%m/%d/%Y') AS 'Birthday'
                        FROM p_biometrics pbio 
                        JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo 
                        LEFT JOIN e_payrolldetails epaydet ON pbio.employeeNo = epaydet.employeeNo
                        LEFT JOIN e_personalinfo eperson ON pbio.employeeNo = eperson.employeeNo
                        LEFT JOIN s_branch br ON br.branchCode = pbio.branchCode
                        WHERE pbio.isActive = 1
                          AND pbio.statusName = 'Posted'
                          AND (pbio.branchCode=@brcode OR @brcode IS NULL OR @brcode='ALL')
                          AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                          AND (pbio.dateMonth=@dtMonth OR @dtMonth = 'ALL' OR @dtMonth IS NULL) 
                          AND pbio.dateYear=@dtYear
                        GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode");
                    break;

                default:
                    query.Append("SELECT 'No data' AS Message");
                    break;
            }

            return query;
        }

        // Generates Excel file with formatted headers and data
        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data, string reportType, (string EmployeeNo, string EmployeeName) employeeInfo)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var reportName = GetReportDisplayName(reportType);
            var ws = package.Workbook.Worksheets.Add(reportName);

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // Calculate totals for numeric columns
            var totals = new Dictionary<string, decimal>();
            foreach (var column in columns)
            {
                if (IsNumericColumn(column))
                {
                    decimal sum = 0;
                    foreach (var row in data)
                    {
                        if (row.ContainsKey(column) &&
                            decimal.TryParse(row[column]?.ToString(), out decimal value))
                        {
                            sum += value;
                        }
                    }
                    totals[column] = sum;
                }
            }

            // Add main title (Row 1)
            ws.Cells[1, 1].Value = $"{reportName}";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Add employee info and timestamp (Row 2)
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({employeeInfo.EmployeeNo}) {employeeInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // Add headers (Row 4, leaving Row 3 blank for spacing)
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // Add data rows (starting from Row 5)
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    var cellValue = data[row][columns[col]];

                    // Format numeric columns
                    if (IsNumericColumn(columns[col]))
                    {
                        if (decimal.TryParse(cellValue?.ToString(), out decimal numValue))
                        {
                            cell.Value = numValue;
                            cell.Style.Numberformat.Format = "#,##0.00";
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        }
                        else
                        {
                            cell.Value = cellValue?.ToString() ?? "0.00";
                        }
                    }
                    else
                    {
                        cell.Value = cellValue?.ToString() ?? string.Empty;
                    }
                }
            }

            // Add TOTALS ROW (after all data rows)
            int totalRowIndex = rowCount + 5;

            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[totalRowIndex, col + 1];
                var columnName = columns[col];

                // Employee No column gets "TOTAL" label
                if (columnName.Contains("Employee No", StringComparison.OrdinalIgnoreCase))
                {
                    cell.Value = "TOTAL";
                    cell.Style.Font.Bold = true;
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }
                // Numeric columns get totals
                else if (IsNumericColumn(columnName) && totals.ContainsKey(columnName))
                {
                    cell.Value = totals[columnName];
                    cell.Style.Numberformat.Format = "#,##0.00";
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    cell.Style.Font.Bold = true;
                }
                // Non-numeric columns remain empty
                else
                {
                    cell.Value = "";
                }

                // Apply styling to all cells in totals row
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 217, 217));
                cell.Style.Font.Bold = true;
            }

            // Format table (apply borders from Row 4 onwards, including totals row)
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            var range = ws.Cells[4, 1, totalRowIndex, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            // Add thicker border above totals row for visual separation
            var totalRowTopBorder = ws.Cells[totalRowIndex, 1, totalRowIndex, columns.Count];
            totalRowTopBorder.Style.Border.Top.Style = ExcelBorderStyle.Medium;

            return package.GetAsByteArray();
        }

        // Applies header styling with blue background and white text
        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        // Checks if column contains numeric data for formatting
        private bool IsNumericColumn(string columnName)
        {
            // Exclude columns that contain these keywords (they're not numeric)
            var excludeKeywords = new[] { "Name", "No", "Number", "TIN", "Birth", "Birthday", "Department" };
            if (excludeKeywords.Any(keyword => columnName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return false;

            var numericKeywords = new[] { "Pay", "Employee", "Employer", "EC", "Total", "Loan",
                "Rounded", "Mandatories", "Taxable", "Withheld", "HDMF", "Calamity" };
            return numericKeywords.Any(keyword => columnName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        // Gets display name for report type
        private string GetReportDisplayName(string reportType)
        {
            return reportType switch
            {
                "SSSreport" => "SSS Contribution Report",
                "PHIreport" => "PhilHealth Contribution Report",
                "PIFreport" => "HDMF Contribution Report",
                "TAXreport" => "Withheld Tax Report",
                _ => "Government Reports"
            };
        }
    }
}