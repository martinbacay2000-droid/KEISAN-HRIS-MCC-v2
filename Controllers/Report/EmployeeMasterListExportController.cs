using Dapper;
using KEISAN_HRIS_v2.Helpers;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class EmployeeMasterListExportController : Controller
    {
        private readonly IDbConnection _db;

        public EmployeeMasterListExportController(IDbConnection db) => _db = db;

        private string CurrentRoleCode => HttpContext.Session.GetString("roleCode");
        private const string ADMIN_ROLE = "RL-000000";
        private bool IsAdmin => CurrentRoleCode == ADMIN_ROLE;

        // FULL access or Admin: can see Basic Monthly Pay in export
        private bool CanViewSalary => IsAdmin || AccessHelper.CanDelete(HttpContext, "RPTemployeeMasterListM");

        // Exports employee master list data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(
            string company, string department, string rank, string position,
            string employmentstatus, string gender,
            int offset = 0, int limit = -1,
            string? sortColumn = null, string? sortDirection = "asc")
        {
            try
            {
                var data = GetEmployeeMasterData(company, department, rank, position,
                    employmentstatus, gender, offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"EmployeeMasterList_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // Retrieves employee master list records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetEmployeeMasterData(
            string company, string department, string rank, string position,
            string employmentstatus, string gender, int offset, int limit,
            string? sortColumn = null, string? sortDirection = "asc")
        {
            var query = new StringBuilder(@"
                SELECT 
                    x.employeeNo        AS 'Employee No',
                    x.employeeName      AS 'Employee Name',
                    x.positionName      AS 'Position',
                    x.rankName          AS 'Rank',
                    x.dateOfBirth       AS 'Birthday',
                    x.age               AS 'Age',
                    x.gender            AS 'Gender',
                    x.dateHired         AS 'Date Hired',
                    x.lengthOfService   AS 'Length of Service',
                    x.sssNo             AS 'SSS Number',
                    x.philhealthNo      AS 'PHIC Number',
                    x.hdmfNo            AS 'HDMF Number',
                    x.tinNo             AS 'TIN Number',
                    x.basicMonthlyPay   AS 'Basic Monthly Pay',
                    x.company           AS 'Company',
                    x.department        AS 'Department',
                    x.status            AS 'Status'
                FROM (
                    SELECT 
                        ebasic.employeeNo,
                        -- ✅ IFNULL on every name field to prevent null employeeName
                        CONCAT(
                            IFNULL(ebasic.lastName,   ''), ', ',
                            IFNULL(ebasic.firstName,  ''), ' ',
                            IFNULL(ebasic.middleName, ''), ' ',
                            IFNULL(ebasic.suffix,     '')
                        ) AS employeeName,
                        spost.positionName,
                        rnk.rankName,
                        bra.branchName                                                       AS company,
                        sdep.departmentName                                                  AS department,
                        ses.employmentStatusName                                             AS status,
                        -- ✅ Correct key 'portalkeisan', decrypt ONCE here only
                        CAST(AES_DECRYPT(pay.basicMonthlyPay, 'portalkeisan') AS CHAR(50))  AS basicMonthlyPay,
                        pay.tinNo,
                        pay.sssNo,
                        pay.philHealthNo                                                     AS philhealthNo,
                        pay.hdmfNo,
                        eper.gender,
                        DATE_FORMAT(eper.dateOfBirth, '%m/%d/%Y')                           AS dateOfBirth,
                        FLOOR(TIMESTAMPDIFF(MONTH, eper.dateOfBirth, NOW()) / 12)           AS age,
                        CASE WHEN IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01'
                             THEN DATE_FORMAT(ebasic.dateHired, '%m/%d/%Y')
                             ELSE DATE_FORMAT(ebasic.dateRehired, '%m/%d/%Y')
                        END AS dateHired,
                        TRIM(CONCAT(
                            CASE WHEN IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01'
                                 THEN IF(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateHired, NOW())/12) = 0, '',
                                      CONCAT(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateHired, NOW())/12), ' yr(s)'))
                                 ELSE IF(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateRehired, NOW())/12) = 0, '',
                                      CONCAT(FLOOR(TIMESTAMPDIFF(MONTH, ebasic.dateRehired, NOW())/12), ' yr(s)'))
                            END,
                            ' ',
                            CASE WHEN FLOOR(TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW())/12) > 0
                                  AND TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW()) % 12 > 0
                                 THEN 'and' ELSE ''
                            END,
                            ' ',
                            CASE WHEN TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW()) % 12 = 0
                                 THEN '' ELSE CONCAT(TIMESTAMPDIFF(MONTH, IF(IFNULL(ebasic.dateRehired, '1900-01-01') = '1900-01-01', ebasic.dateHired, ebasic.dateRehired), NOW()) % 12, ' month(s)')
                            END
                        )) AS lengthOfService
                    FROM e_basicinfo ebasic
                    LEFT JOIN s_rank             rnk   ON ebasic.rankCode         = rnk.rankCode
                    LEFT JOIN s_branch           bra   ON ebasic.branchCode       = bra.branchCode
                    LEFT JOIN e_personalinfo     eper  ON ebasic.employeeNo       = eper.employeeNo
                    LEFT JOIN e_payrolldetails   pay   ON ebasic.employeeNo       = pay.employeeNo
                    LEFT JOIN s_department       sdep  ON ebasic.departmentCode   = sdep.departmentCode
                    LEFT JOIN s_position         spost ON ebasic.positionCode     = spost.positionCode
                    LEFT JOIN s_employmentstatus ses   ON ebasic.employmentStatus = ses.employmentStatusCode
                    WHERE ebasic.isActive = 1
                      AND (CASE WHEN @gender           = 'ALL' THEN IFNULL(eper.gender, '') IN ('', 'FEMALE', 'MALE') ELSE eper.gender            = @gender           END)
                      AND (CASE WHEN @company          = 'ALL' THEN 1=1 ELSE ebasic.branchCode      = @company          END)
                      AND (CASE WHEN @rank             = 'ALL' THEN 1=1 ELSE ebasic.rankCode         = @rank             END)
                      AND (CASE WHEN @department       = 'ALL' THEN 1=1 ELSE ebasic.departmentCode   = @department       END)
                      AND (CASE WHEN @employmentstatus = 'ALL' THEN 1=1 ELSE ebasic.employmentStatus = @employmentstatus END)
                      AND (CASE WHEN @position         = 'ALL' THEN 1=1 ELSE ebasic.positionCode     = @position         END)
                    GROUP BY ebasic.employeeNo
                ) x
            ");

            // Apply sorting
            if (!string.IsNullOrWhiteSpace(sortColumn))
            {
                var dbColumn = GetDatabaseColumnName(sortColumn);
                var direction = sortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";
                query.Append($" ORDER BY {dbColumn} {direction}");
            }
            else
            {
                query.Append(" ORDER BY x.employeeName");
            }

            var parameters = new DynamicParameters();
            parameters.Add("@company", company ?? "ALL");
            parameters.Add("@department", department ?? "ALL");
            parameters.Add("@rank", rank ?? "ALL");
            parameters.Add("@position", position ?? "ALL");
            parameters.Add("@employmentstatus", employmentstatus ?? "ALL");
            parameters.Add("@gender", gender ?? "ALL");

            if (limit > 0)
                query.Append($" LIMIT {limit} OFFSET {offset}");

            var result = _db.Query(query.ToString(), parameters);
            var dataList = new List<Dictionary<string, object>>();

            foreach (var row in result)
            {
                var rowDict = (IDictionary<string, object>)row;
                var dict = rowDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty);

                // Mask Basic Monthly Pay for non-authorized users
                if (!CanViewSalary && dict.ContainsKey("Basic Monthly Pay"))
                    dict["Basic Monthly Pay"] = string.Empty;

                dataList.Add(dict);
            }

            return dataList;
        }

        // Maps DataTable column names to database column references for sorting
        private string GetDatabaseColumnName(string sortColumn)
        {
            return sortColumn?.ToLower() switch
            {
                "employeeno" => "x.employeeNo",
                "employeename" => "x.employeeName",
                "position" => "x.positionName",
                "rank" => "x.rankName",
                "birthday" => "x.dateOfBirth",
                "age" => "x.age",
                "gender" => "x.gender",
                "datehired" => "x.dateHired",
                "lengthofservice" => "x.lengthOfService",
                "basicmonthlypay" => "x.basicMonthlyPay",
                "company" => "x.company",
                "department" => "x.department",
                "status" => "x.status",
                _ => "x.employeeName"
            };
        }

        // Generates formatted Excel file from employee data
        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Employee Master List");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // Get current employee info for export header
            var employeeNo = HttpContext.Session.GetString("employeeNo");
            var employeeName = HttpContext.Session.GetString("userFullName") ?? "Unknown User";

            // ── Row 1: Main Title ────────────────────────────────────────────────
            ws.Cells[1, 1].Value = "Employee Master List";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ── Row 2: Generated By + Timestamp ─────────────────────────────────
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({employeeNo}) {employeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // ── Row 3: Blank spacer ──────────────────────────────────────────────

            // ── Row 4: Headers ───────────────────────────────────────────────────
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // ── Rows 5+: Data ────────────────────────────────────────────────────
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    var columnName = columns[col];
                    var cellValue = data[row][columnName];

                    cell.Value = cellValue ?? string.Empty;

                    if (IsNumericColumn(columnName))
                    {
                        if (decimal.TryParse(cellValue?.ToString(), out decimal numValue))
                        {
                            cell.Value = numValue;
                            cell.Style.Numberformat.Format = "#,##0.00";
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        }
                    }
                }
            }

            // ── Borders (Row 4 to last data row) ────────────────────────────────
            int lastDataRow = rowCount + 4;
            var range = ws.Cells[4, 1, lastDataRow, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells.AutoFitColumns();

            return package.GetAsByteArray();
        }

        // Applies bold blue header styling with white text and center alignment
        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        // Determines if a column should be formatted as a number in Excel
        private bool IsNumericColumn(string columnName)
        {
            var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Basic Monthly Pay",
                "Age"
            };

            return numericColumns.Contains(columnName);
        }
    }
}