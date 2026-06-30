using Dapper;
using Microsoft.AspNetCore.Mvc;
using Mysqlx.Crud;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Org.BouncyCastle.Asn1.X509;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class LeaveBalanceExportController : Controller
    {
        private readonly IDbConnection _db;

        public LeaveBalanceExportController(IDbConnection db) => _db = db;

        // Exports request data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(
            string branch, string department, int offset = 0, int limit = -1,
            string? sortColumn = null, string? sortDirection = "asc"
            )
        {
            try
            {
                var data = GetRequestData(branch, department, offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"LeaveBalanceReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // Retrieves official business request records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetRequestData(string branch, string department, int offset, int limit,
            string? sortColumn, string? sortDirection)
        {
            var query = new StringBuilder(@"
                SELECT
                    br.branchName Company,
                    dep.departmentName Department,
                    m.EmployeeNo, 
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName,1),''), CASE WHEN IFNULL(b.middleName,'')<>'' THEN '.' ELSE '' END) AS `Employee Name`,
                    MAX(CASE WHEN m.leaveCode = 'SL'  THEN IFNULL(m.availablebalance,0) END) AS SL,
                    MAX(CASE WHEN m.leaveCode = 'VL'  THEN IFNULL(m.availablebalance,0) END) AS VL,
                    MAX(CASE WHEN m.leaveCode = 'CTO' THEN IFNULL(m.availablebalance,0) END) AS CTO

                FROM m_leave m
                JOIN e_basicinfo b ON m.employeeNo = b.employeeNo
                JOIN s_department dep ON dep.departmentCode = b.departmentCode
                JOIN s_branch br ON br.branchCode = b.branchCode
                JOIN (
                    SELECT
                        employeeNo,
                        leaveCode,
                        MAX(id) AS latestId
                    FROM m_leave
                    WHERE leaveCode IN ('SL','VL','CTO')
                    GROUP BY employeeNo, leaveCode
                ) latest
                   ON m.employeeNo = latest.employeeNo
                   AND m.leaveCode = latest.leaveCode
                   AND m.id = latest.latestId

                WHERE b.isActive = 1
                AND (@brcode = '' OR @brcode = 'ALL' OR b.branchCode = @brcode)
                AND (@department = '' OR @department = 'ALL' OR b.departmentCode = @department)

                GROUP BY m.employeeNo
                ");

            var parameters = new DynamicParameters();
            parameters.Add("@brcode", branch);
            parameters.Add("@department", department);

            // Apply sorting
            if (!string.IsNullOrWhiteSpace(sortColumn))
            {
                var dbColumn = GetDatabaseColumnName(sortColumn);
                var direction = sortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";
                query.Append($" ORDER BY {dbColumn} {direction}");
            }
            else
            {
                query.Append(" ORDER BY branchName, departmentName, lastName, firstName");
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

        // Applies filter conditions to the SQL query based on status, branch, department, and date range parameters
        private void ApplyFilters(StringBuilder query, DynamicParameters parameters,
             string? branch, string? department,int? offSet, int? limit)
        {
            // Branch filter
            if (!string.IsNullOrWhiteSpace(branch) && branch != "ALL")
            {
                query.Append(" AND b.branchCode = @branchCode");
                parameters.Add("@branchCode", branch);
            }

            // Department filter
            if (!string.IsNullOrWhiteSpace(department) && department != "ALL")
            {
                query.Append(" AND b.departmentCode = @department");
                parameters.Add("@department", department);
            }

        }

        // Map frontend column names to database column names
        private string GetDatabaseColumnName(string sortColumn)
        {
            return sortColumn?.ToLower() switch
            {
                "company" => "branchName",
                "branchname" => "branchName",
                "department" => "departmentName",
                "departmentname" => "departmentName",
                "employeeno" => "EmployeeNo",
                "employee name" => "lastName",
                "employeename" => "lastName",
                "sl" => "SL",
                "vl" => "VL",
                "cto" => "CTO",
                _ => "branchName"
            };
        }

        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Leave Balance Report");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // ======================
            // TITLE ROW (ROW 1)
            // ======================
            ws.Cells[1, 1].Value = "Leave Balance Report";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ======================
            // EMPLOYEE INFO AND TIMESTAMP (ROW 2)
            // ======================
            var sessionUserFullName = HttpContext.Session.GetString("userFullName") ?? "Unknown User";
            var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo") ?? "N/A";
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({sessionEmployeeNo}) {sessionUserFullName}     Timestamp: {timestamp}";

            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // ======================
            // HEADERS (ROW 4, leaving Row 3 blank for spacing)
            // ======================
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // ======================
            // DATA ROWS (START ROW 5)
            // ======================
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
                        cell.Style.Numberformat.Format = "#,##0.00";
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }
                }
            }

            int totalsRow = rowCount + 5;

            // ======================
            // BORDERS
            // ======================
            var range = ws.Cells[4, 1, totalsRow - 1, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells.AutoFitColumns();

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
        }


        private bool IsNumericColumn(string columnName)
        {
            var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SL",
                "VL",
                "CTO"
            };

            return numericColumns.Contains(columnName);
        }
              
    }
}
