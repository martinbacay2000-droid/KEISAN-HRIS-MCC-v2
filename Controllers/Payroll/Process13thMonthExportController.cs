using Dapper;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class Process13thMonthExportController : Controller
    {
        private readonly IDbConnection _db;

        public Process13thMonthExportController(IDbConnection db) => _db = db;

        // Exports 13th month pay data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(string? branch, string? dateYear, int offset = 0, int limit = -1)
        {
            try
            {
                // Get current employee number from session
                var employeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(employeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                // Get employee details
                var employeeInfo = GetEmployeeInfo(employeeNo);

                var data = GetMonth13Data(branch, dateYear, offset, limit);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, dateYear, employeeInfo);
                var fileName = $"13thMonthPay_{dateYear}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
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

        // Retrieves 13th month pay records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetMonth13Data(string? branch, string? dateYear, int offset, int limit)
        {
            var query = new StringBuilder(@"
                SELECT 
                    t2.employeeNo AS 'Employee No',
                    t2.fullName AS 'Employee Name',
                    t2.branchName AS 'Branch',
                    FORMAT(t2.basicPay, 2) AS 'Basic Pay',
                    FORMAT(t2.rataAmount, 2) AS 'Allowance',
                    FORMAT(t2.adjustment, 2) AS 'Adjustment',
                    FORMAT(t2.totalLate, 2) AS 'Total Late',
                    FORMAT(t2.totalUndertime, 2) AS 'Total Undertime',
                    FORMAT(t2.absentAmount, 2) AS 'Total Absent',
                    FORMAT(t2.v13thMonth, 2) AS '13th Month Pay'
                FROM (
                    SELECT *,
                        ROUND(SLVL * dailyRate, 2) AS totalSLVL,
                        CAST((basicPay + rataAmount - totalLate - totalUndertime - absentAmount) / 12
                            + ROUND(SLVL * dailyRate, 2) + adjustment - deduction
                        AS DECIMAL(10,2)) AS v13thMonth,
                        deduction AS v13thMonthDeduction 
                    FROM (
                        SELECT *,
                            basicMonthlyPay + allowanceAmount AS monthly,
                            CASE WHEN payrollBasis='MONTHLY' THEN (basicMonthlyPay + allowanceAmount)/26 ELSE dailyRate1 END AS dailyRate		
                        FROM (
                            SELECT 
                                pbio.employeeNo,
                                CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''),1), '.') AS fullName,
                                pbio.employmentStatus,
                                CASE WHEN p.payrollBasis = 'D' THEN 'DAILY' ELSE 'MONTHLY' END AS payrollBasis,
                                b.dateHired,
                                12 - (SELECT MONTH(dateTo) FROM p_biometrics pb WHERE dateYear=@dtYear AND pb.employeeNo = b.employeeNo AND isactive = 1 ORDER BY ID LIMIT 1) AS tenure,
                                pbio.branchCode, 
                                br.branchName,
                                SUM(IFNULL(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200)) + pbio.workOnOffPresentAmount + pbio.amountRestOT + pbio.amountNSDRest + pbio.legalPresentAmount + pbio.specialPresentAmount + IFNULL(pbio.reg_basic_al,0),0)) AS basicPay,
                                SUM(IFNULL(pbio.totalAmountLate,0)) AS totalLate,
                                SUM(IFNULL(pbio.totalAmountUndertime,0)) AS totalUndertime,
                                SUM(IFNULL(pbio.absentAmount,0)) AS absentAmount,
                                SUM(IFNULL(pbio.rataAmount - pbio.allowanceDeductionAbsent - pbio.allowanceDeductionLate,0)) AS rataAmount, 
                                CAST(AES_DECRYPT(p.basicMonthlyPay,'portalkeisan') AS CHAR(200)) as basicMonthlyPay, 
                                CASE WHEN pbio.employeeNo = 'C-049' AND @dtYear=2025 THEN 550 ELSE CAST(AES_DECRYPT(pbio.dailyRate,'portalkeisan') AS CHAR(200)) END as dailyRate1,
                                IFNULL((SELECT allowanceAmount FROM e_allowance al WHERE al.employeeNo = b.employeeNo AND al.isActive =1 AND al.allowanceCode='SALARY' ORDER BY EFFECTIVITYDATE DESC LIMIT 1),0) AS allowanceAmount,
                                IFNULL((SELECT SUM(approvedAmount) FROM c_payable cp WHERE cp.adjustmentCode='YEARENDBONUS' AND cp.statusName='Approved' AND cp.isActive = 1 AND cp.employeeNo = b.employeeNo AND Year(cp.dateToAdjustment)=@dtYear),0) AS adjustment,
                                IFNULL((SELECT SUM(amount) FROM c_receivable cp WHERE cp.otherdeductionCode='MONTH13THDEDUCTION' AND cp.statusName='Approved' AND cp.isActive = 1 AND cp.employeeNo = b.employeeNo AND Year(cp.dtAdded)=@dtYear),0) AS deduction,
                                IFNULL((SELECT m.availableBalance FROM m_leave m WHERE m.employeeNo = b.employeeNo AND m.leaveCode = 'LC-000001' ORDER BY id DESC LIMIT 1),0)
                                + IFNULL((SELECT m.availableBalance FROM m_leave m WHERE m.employeeNo = b.employeeNo AND m.leaveCode = 'LC-000002' ORDER BY id DESC LIMIT 1),0) AS SLVL,
                                IFNULL(rq.id,0) AS requestID
                            FROM p_biometrics pbio
                            LEFT JOIN e_basicinfo b ON b.employeeNo = pbio.employeeNo
                            LEFT JOIN e_payrolldetails p ON p.employeeNo = pbio.employeeNo
                            LEFT JOIN s_branch br ON br.branchCode = pbio.branchCode
                            LEFT JOIN rq_13thmonth rq ON rq.employeeNo = pbio.employeeNo AND rq.dateYear = @dtYear
                            WHERE pbio.dateYear = @dtYear
                            AND pbio.isActive = 1
                            AND pbio.statusName = 'POSTED'
                            AND IFNULL(rq.id,0) = 0");

            var parameters = new DynamicParameters();
            parameters.Add("@dtYear", dateYear);

            ApplyFilters(query, parameters, branch);

            query.Append(@"
                            GROUP BY pbio.employeeNo
                            ORDER BY pbio.employeeNo
                        ) AS t1
                        WHERE t1.basicMonthlyPay != 0
                    ) t2
                ) t2");

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

        // Applies filter conditions to the SQL query based on branch parameter
        private void ApplyFilters(StringBuilder query, DynamicParameters parameters, string? branch)
        {
            // Branch filter
            if (!string.IsNullOrWhiteSpace(branch) && branch != "ALL")
            {
                query.Append(" AND pbio.branchCode = @brcode");
                parameters.Add("@brcode", branch);
            }
        }

        // Generates an Excel file from the provided data with formatted headers and borders
        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data, string? dateYear, (string EmployeeNo, string EmployeeName) employeeInfo)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("13th Month Pay");

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
                        if (row.ContainsKey(column))
                        {
                            var value = row[column]?.ToString() ?? "0";
                            // Remove commas if present (from FORMAT function)
                            value = value.Replace(",", "");

                            if (decimal.TryParse(value, out decimal numValue))
                            {
                                sum += numValue;
                            }
                        }
                    }
                    totals[column] = sum;
                }
            }

            // Add main title (Row 1)
            ws.Cells[1, 1].Value = $"13th Month Pay Report - Year {dateYear}";
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
                        var value = cellValue?.ToString() ?? "0";
                        // Remove commas if present
                        value = value.Replace(",", "");

                        if (decimal.TryParse(value, out decimal numValue))
                        {
                            cell.Value = numValue;
                            cell.Style.Numberformat.Format = "#,##0.00";
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        }
                        else
                        {
                            cell.Value = cellValue?.ToString() ?? "0.00";
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
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

            // Format table (update range to include totals row, starting from Row 4)
            var range = ws.Cells[4, 1, totalRowIndex, columns.Count];
            range.AutoFitColumns();
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            // Add thicker border above totals row for visual separation
            var totalRowTopBorder = ws.Cells[totalRowIndex, 1, totalRowIndex, columns.Count];
            totalRowTopBorder.Style.Border.Top.Style = ExcelBorderStyle.Medium;

            return package.GetAsByteArray();
        }

        // Applies bold blue header styling with white text and center alignment to the specified cell
        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.WrapText = true;
        }

        // Determines if a column contains numeric data based on column name
        private bool IsNumericColumn(string columnName)
        {
            var numericColumns = new[] { "Basic Pay", "Allowance", "Adjustment", "Total Late",
                "Total Undertime", "Total Absent", "13th Month Pay" };
            return numericColumns.Contains(columnName);
        }
    }
}