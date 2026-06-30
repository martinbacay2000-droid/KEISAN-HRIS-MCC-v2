using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Helpers;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTemployeeMasterListM")]
    public class EmployeeMasterListController : Controller
    {
        private readonly IDbConnection _db;

        public EmployeeMasterListController(IDbConnection db)
        {
            _db = db;
        }

        private string CurrentRoleCode => HttpContext.Session.GetString("roleCode");
        private const string ADMIN_ROLE = "RL-000000";
        private bool IsAdmin => CurrentRoleCode == ADMIN_ROLE;

        // FULL access or Admin: can see Basic Monthly Pay
        private bool CanViewSalary => IsAdmin || AccessHelper.CanCreate(HttpContext, "RPTemployeeMasterListM");

        public IActionResult Index()
        {
            ViewBag.CanViewSalary = CanViewSalary;
            return View("~/Views/Report/EmployeeMasterList.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string company, string department, string rank, string position, string employmentstatus, string gender, string search)
        {
            string query = @"
        SELECT 
            x.employeeNo,
            x.employeeName,
            x.positionName,
            x.rankName,
            TRIM(CONCAT(x.years, ' ', x.strAnd, ' ', x.months)) AS lengthOfService,
            x.age,
            x.gender,
            x.dateOfBirth,
            x.dateHired,
            x.sssNo,
            x.philHealthNo AS philhealthNo,
            x.hdmfNo,
            x.tinNo,
            x.basicMonthlyPay,
            x.branchName AS company,
            x.departmentName AS department,
            x.employmentStatus AS status
        FROM (
            SELECT 
                ebasic.employeeNo,
                CONCAT(
                    IFNULL(ebasic.lastName, ''), ', ',
                    IFNULL(ebasic.firstName, ''), ' ',
                    IFNULL(ebasic.middleName, ''), ' ',
                    IFNULL(ebasic.suffix, '')
                ) AS employeeName,
                spost.positionName,
                rnk.rankName,
                bra.branchName,
                sdep.departmentName,
                ses.employmentStatusName AS employmentStatus,
                CAST(AES_DECRYPT(pay.basicMonthlyPay, 'portalkeisan') AS CHAR(50)) AS basicMonthlyPay,
                pay.tinNo, pay.sssNo, pay.philHealthNo, pay.hdmfNo, 
                eper.gender,
                DATE_FORMAT(eper.dateOfBirth, '%m/%d/%Y') AS dateOfBirth,
                FLOOR(TIMESTAMPDIFF(MONTH, eper.dateOfBirth, NOW()) / 12) AS age,
                CASE WHEN IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01' 
                     THEN DATE_FORMAT(ebasic.dateHired, '%m/%d/%Y') 
                     ELSE DATE_FORMAT(ebasic.dateRehired, '%m/%d/%Y') 
                END AS dateHired,
                CASE WHEN IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01' 
                     THEN IF(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateHired, NOW())/12)=0, '', CONCAT(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateHired, NOW())/12), ' yr(s)'))
                     ELSE IF(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateRehired, NOW())/12)=0, '', CONCAT(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateRehired, NOW())/12), ' yr(s)'))
                END AS years,
                CASE WHEN FLOOR(TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW())/12) > 0 
                      AND TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW()) % 12 > 0 
                     THEN 'and' ELSE '' END AS strAnd,
                CASE WHEN TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW()) % 12 = 0 
                     THEN '' ELSE CONCAT(TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW()) % 12, ' month(s)') 
                END AS months
            FROM e_basicinfo ebasic
            LEFT JOIN s_rank rnk ON ebasic.rankCode = rnk.rankCode
            LEFT JOIN s_branch bra ON ebasic.branchCode = bra.branchCode
            LEFT JOIN e_personalinfo eper ON ebasic.employeeNo = eper.employeeNo
            LEFT JOIN e_payrolldetails pay ON ebasic.employeeNo = pay.employeeNo
            LEFT JOIN s_department sdep ON ebasic.departmentCode = sdep.departmentCode
            LEFT JOIN s_position spost ON ebasic.positionCode = spost.positionCode
            LEFT JOIN s_employmentstatus ses ON ebasic.employmentStatus = ses.employmentStatusCode
            WHERE ebasic.isActive = 1
              AND (CASE WHEN @gender = 'ALL' THEN IFNULL(eper.gender, '') IN ('', 'FEMALE', 'MALE') ELSE eper.gender = @gender END)
              AND (@search IS NULL OR ebasic.employeeNo LIKE CONCAT('%', @search, '%') OR CONCAT(ebasic.firstName, ' ', ebasic.lastName) LIKE CONCAT('%', @search, '%'))
              AND (CASE WHEN @rank = 'ALL' THEN 1=1 ELSE ebasic.rankCode = @rank END)
              AND (CASE WHEN @department = 'ALL' THEN 1=1 ELSE ebasic.departmentCode = @department END)
              AND (CASE WHEN @employmentstatus = 'ALL' THEN 1=1 ELSE ebasic.employmentStatus = @employmentstatus END)
              AND (CASE WHEN @position = 'ALL' THEN 1=1 ELSE ebasic.positionCode = @position END)
              AND (CASE WHEN @company = 'ALL' THEN 1=1 ELSE ebasic.branchCode = @company END)
            GROUP BY ebasic.employeeNo
            ORDER BY ebasic.lastName
        ) x";

            var p = new DynamicParameters();
            p.Add("@company", company);
            p.Add("@department", department);
            p.Add("@rank", rank);
            p.Add("@position", position);
            p.Add("@employmentstatus", employmentstatus);
            p.Add("@gender", gender);
            p.Add("@search", string.IsNullOrWhiteSpace(search) ? null : search);

            var contriReport = _db.Query<EmployeeMasterListModel>(query.ToString(), p).ToList();

            // Mask basicMonthlyPay for non-FULL users
            if (!CanViewSalary)
            {
                foreach (var row in contriReport)
                    row.basicMonthlyPay = null;
            }

            return Json(new { data = contriReport });
        }


        [HttpGet]
        public IActionResult ExportToExcel(string? status, string? branch, string? dateMonth,
          string? dateYear, int offset = 0, int limit = -1)
        {
            try
            {
                // Get current employee number from session
                var employeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(employeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                // Get employee details
                var employeeInfo = GetEmployeeInfo(employeeNo);

                var data = GetGovernmentContriData(status, branch, dateMonth, dateYear, offset, limit);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, status ?? "SSSreport", employeeInfo);
                var reportName = GetReportDisplayName(status ?? "SSSreport");
                var fileName = $"{reportName}_{dateYear}_{dateMonth}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }



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

        private List<Dictionary<string, object>> GetGovernmentContriData(string? status, string? branch,
          string? dateMonth, string? dateYear, int offset, int limit)
        {
            var query = BuildQueryByReportType(status ?? "SSSreport");
            var parameters = new DynamicParameters();

            parameters.Add("@brcode", string.IsNullOrWhiteSpace(branch) || branch == "null" ? "ALL" : branch);
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
                            m.employeeNo AS 'EMPLOYEE NO',
                            UPPER(CONCAT(IFNULL(eb.lastName, ''), ', ', IFNULL(eb.firstName, ''))) AS 'FULL NAME',
                            ep.sssNo AS 'SSS NO',
                            m.ssscal AS 'CALAMITY LOAN',
                            m.ssssal AS 'SALARY LOAN'
                        FROM (
                            /* Subquery to aggregate sums first */
                            SELECT 
                                employeeNo,
                                dateMonth,
                                dateYear,
                                SUM(sssCalamity) AS ssscal,
                                SUM(sssLoan) AS ssssal
                            FROM p_biometrics
                            WHERE isActive = 1
                            AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR dateMonth = @dtMonth)
                            AND (@dtYear IS NULL OR dateYear = @dtYear)
                            AND statusName = 'POSTED'
                            GROUP BY employeeNo, dateMonth, dateYear
                            /* Only include rows where at least one loan has a value > 0 */
                            HAVING (ssscal > 0 OR ssssal > 0)
                        ) m
                        LEFT JOIN e_basicinfo eb ON m.employeeNo = eb.employeeNo
                        LEFT JOIN e_payrolldetails ep ON m.employeeNo = ep.employeeNo
                        WHERE (@brcode = 'ALL' OR @brcode IS NULL OR eb.branchCode = @brcode)
                        ORDER BY eb.lastName ASC ");
                    break;



                case "PIFreport":
                    query.Append(@"
                       SELECT 
                            m.employeeNo AS 'EMPLOYEE NO',
                            UPPER(CONCAT(IFNULL(eb.lastName, ''), ', ', IFNULL(eb.firstName, ''))) AS 'FULL NAME',
                         ep.philhealthNo as 'PAGIBIG NO',
                            m.pagcal AS 'CALAMITY LOAN',
                            m.pagsal AS 'SALARY LOAN'
   
                        FROM (
                            /* Subquery to aggregate sums first */
                            SELECT 
                                employeeNo,
                                dateMonth,
                                dateYear,
                                SUM(hdmfCalamity) AS pagcal,
                                SUM(hdmfLoan) AS pagsal
                            FROM p_biometrics
                            WHERE isActive = 1
                            AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR dateMonth = @dtMonth)
                            AND (@dtYear IS NULL OR dateYear = @dtYear)
                            AND statusName = 'POSTED'
                            GROUP BY employeeNo, dateMonth, dateYear
                            /* Only include rows where at least one loan has a value > 0 */
                            HAVING (pagcal > 0 OR pagsal > 0)
                        ) m
                        LEFT JOIN e_basicinfo eb ON m.employeeNo = eb.employeeNo
                        LEFT JOIN e_payrolldetails ep ON m.employeeNo = ep.employeeNo
                        WHERE (@brcode = 'ALL' OR @brcode IS NULL OR eb.branchCode = @brcode)
                        ORDER BY eb.lastName ASC ");
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
                if (columnName.Contains("EMPLOYEE NO", StringComparison.OrdinalIgnoreCase))
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
            var excludeKeywords = new[] { "EMPLOYEE NO", "EMPLOYEE NAME", "SSS NUMBER", "PAGIBIG NUMBER" };
            if (excludeKeywords.Any(keyword => columnName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return false;

            var numericKeywords = new[] { "CALAMITY LOAN", "SALARY LOAN" };
            return numericKeywords.Any(keyword => columnName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        // Gets display name for report type
        private string GetReportDisplayName(string reportType)
        {
            return reportType switch
            {
                "SSSreport" => "SSS Loans Report",
                "PIFreport" => "PAGIBIG Loans Report",
                _ => "Government Reports"
            };
        }
    }
}