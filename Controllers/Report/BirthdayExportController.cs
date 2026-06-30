using Dapper;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class BirthdayExportController : Controller
    {
        private readonly IDbConnection _db;

        public BirthdayExportController(IDbConnection db) => _db = db;

        [HttpGet]
        public IActionResult ExportToExcel(
            string dateMonth,
            int offset = 0,
            int limit = -1,
            string? sortColumn = null,
            string? sortDirection = "asc")
        {
            try
            {
                var data = GetBirthdayData(dateMonth, offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"BirthdayReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        private List<Dictionary<string, object>> GetBirthdayData(
            string dateMonth, int offset, int limit,
            string? sortColumn, string? sortDirection)
        {
            var query = new StringBuilder(@"
                SELECT
                    UPPER(CONCAT(eb.lastName, ' ', eb.firstName)) AS `Employee Name`,
                    DATE_FORMAT(ep.dateOfBirth, '%m/%d/%Y') AS `Birthday`,
                    (YEAR(NOW()) - YEAR(ep.dateOfBirth)) AS `Age`,
                    CASE 
                        WHEN CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth,'%m-%d')) AS DATE) = CURDATE()
                            THEN 'TODAY''S BIRTHDAY CELEBRANT'
                        WHEN DATEDIFF(CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth,'%m-%d')) AS DATE), NOW()) = 1
                            THEN '1 DAY BEFORE BIRTHDAY'
                        ELSE
                            CASE
                                WHEN DATEDIFF(CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth,'%m-%d')) AS DATE), NOW()) < 0
                                    THEN ''
                                ELSE CONCAT(DATEDIFF(CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth,'%m-%d')) AS DATE), NOW()), ' DAYS BEFORE BIRTHDAY')
                            END
                    END AS `Status`
                FROM e_personalInfo ep
                LEFT JOIN e_basicinfo eb ON ep.employeeNo = eb.employeeNo
                WHERE eb.isActive = 1
                AND (@dateMonth = 'ALL' OR MONTHNAME(ep.dateOfBirth) = @dateMonth)
            ");

            var parameters = new DynamicParameters();
            parameters.Add("@dateMonth", string.IsNullOrWhiteSpace(dateMonth) ? "ALL" : dateMonth);

            if (!string.IsNullOrWhiteSpace(sortColumn))
            {
                var dbColumn = GetDatabaseColumnName(sortColumn);
                var direction = sortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";
                query.Append($" ORDER BY {dbColumn} {direction}");
            }
            else
            {
                query.Append(" ORDER BY ep.dateOfBirth");
            }

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

        private string GetDatabaseColumnName(string sortColumn)
        {
            return sortColumn?.ToLower() switch
            {
                "employee name" => "eb.lastName",
                "employeename" => "eb.lastName",
                "birthday" => "ep.dateOfBirth",
                "age" => "Age",
                "status" => "Status",
                _ => "ep.dateOfBirth"
            };
        }

        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Birthday Report");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // Row 1 — Title
            ws.Cells[1, 1].Value = "Birthday Report";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Row 2 — Generated by / Timestamp
            var sessionUserFullName = HttpContext.Session.GetString("userFullName") ?? "Unknown User";
            var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo") ?? "N/A";
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({sessionEmployeeNo}) {sessionUserFullName}     Timestamp: {timestamp}";

            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // Row 4 — Headers (Row 3 left blank for spacing)
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // Row 5+ — Data
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
                        cell.Style.Numberformat.Format = "#,##0";
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }
                }
            }

            // Borders
            int lastDataRow = rowCount + 4;
            var range = ws.Cells[4, 1, lastDataRow, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells.AutoFitColumns();

            return package.GetAsByteArray();
        }

        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        private bool IsNumericColumn(string columnName)
        {
            var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Age" };
            return numericColumns.Contains(columnName);
        }
    }
}