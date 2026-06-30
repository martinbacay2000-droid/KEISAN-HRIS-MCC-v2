using Dapper;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class DueForRegularizationExportController : Controller
    {
        private readonly IDbConnection _db;

        public DueForRegularizationExportController(IDbConnection db) => _db = db;

        [HttpGet]
        public IActionResult ExportToExcel(
            string departmentName,
            int offset = 0,
            int limit = -1,
            string? sortColumn = null,
            string? sortDirection = "asc")
        {
            try
            {
                var data = GetRegularizationData(departmentName, offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"DueForRegularizationReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        private List<Dictionary<string, object>> GetRegularizationData(
            string departmentName, int offset, int limit,
            string? sortColumn, string? sortDirection)
        {
            var query = new StringBuilder(@"
                SELECT
                    UPPER(CONCAT(eb.lastName, ' ', eb.firstName)) AS `Employee Name`,
                    DATE_FORMAT(eb.dateOfRegApp, '%m/%d/%Y')      AS `Date of Regularization`,
                    s.departmentName                               AS `Department`,
                    CASE
                        WHEN eb.dateOfRegApp IS NULL THEN NULL
                        WHEN CURDATE() > eb.dateOfRegApp THEN 'FOR REGULARIZATION'
                        WHEN CURDATE() = eb.dateOfRegApp THEN 'TODAY'
                        ELSE CONCAT(DATEDIFF(eb.dateOfRegApp, CURDATE()), ' DAYS REMAINING')
                    END AS `Status`
                FROM e_basicinfo eb
                LEFT JOIN s_department s ON eb.departmentCode = s.departmentCode
                WHERE eb.isActive = 1
                AND eb.employmentStatus = 'PROBATIONARY'
                AND CASE
                        WHEN IFNULL(@departmentName, '') = 'ALL' THEN eb.employeeNo IS NOT NULL
                        ELSE eb.departmentCode = @departmentName
                    END
            ");

            var parameters = new DynamicParameters();
            parameters.Add("@departmentName", string.IsNullOrWhiteSpace(departmentName) ? "ALL" : departmentName);

            if (!string.IsNullOrWhiteSpace(sortColumn))
            {
                var dbColumn = GetDatabaseColumnName(sortColumn);
                var direction = sortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";
                query.Append($" ORDER BY {dbColumn} {direction}");
            }
            else
            {
                query.Append(" ORDER BY eb.dateOfRegApp");
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
                "date of regularization" => "eb.dateOfRegApp",
                "dateofregularization" => "eb.dateOfRegApp",
                "department" => "s.departmentName",
                "departmentname" => "s.departmentName",
                "status" => "Status",
                _ => "eb.dateOfRegApp"
            };
        }

        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Due For Regularization");
            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // Row 1 — Title
            ws.Cells[1, 1].Value = "Due For Regularization Report";
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

            // Row 4 — Headers (Row 3 blank for spacing)
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
                    cell.Value = data[row][columnName] ?? string.Empty;
                }
            }

            // Borders
            var range = ws.Cells[4, 1, rowCount + 4, columns.Count];
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
    }
}