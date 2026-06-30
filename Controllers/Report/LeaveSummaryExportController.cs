using Dapper;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class LeaveSummaryExportController : Controller
    {
        private readonly IDbConnection _db;

        public LeaveSummaryExportController(IDbConnection db) => _db = db;

        [HttpGet]
        public IActionResult ExportToExcel(
            string branch, string department, string datefrom, string dateto, string leavecode,
            int offset = 0, int limit = -1,
            string? sortColumn = null, string? sortDirection = "asc")
        {
            try
            {
                var data = GetRequestData(branch, department, datefrom, dateto, leavecode,
                                          offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"LeaveRequestSummary_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        private List<Dictionary<string, object>> GetRequestData(
            string branch, string department, string datefrom, string dateto, string leavecode,
            int offset, int limit, string? sortColumn = null, string? sortDirection = "asc")
        {
            var query = new StringBuilder(@"
                SELECT
                    COALESCE(br.branchName, '') AS Company,
                    COALESCE(dep.departmentName, '') AS Department,
                    r.employeeNo AS EmployeeNo, 
                    CONCAT(b.lastName, ', ', b.firstName, ' ', 
                           IFNULL(LEFT(b.middleName,1),''), 
                           CASE WHEN IFNULL(b.middleName,'')<>'' THEN '.' ELSE '' END) AS EmployeeName,
                    sl.leaveName AS LeaveType,
                    DATE_FORMAT(r.leaveDateFrom,'%m/%d/%Y') AS LeaveDateFrom,	
                    DATE_FORMAT(r.leaveDateTo,'%m/%d/%Y') AS LeaveDateTo,
                    r.leaveCountDays AS LeaveDays,
                    COALESCE(r.leaveReason, '') AS LeaveReason,		
                    DATE_FORMAT(r.dtAdded,'%m/%d/%Y') AS DateRequested,	
                    DATE_FORMAT(r.dtStatus,'%m/%d/%Y') AS DateApproved,
                    CONCAT(a.lastName, ', ', a.firstName, ' ', 
                           IFNULL(LEFT(a.middleName,1),''), 
                           CASE WHEN IFNULL(a.middleName,'')<>'' THEN '.' ELSE '' END) AS RequestedByUser
                FROM rq_leave r
                LEFT JOIN s_leave sl ON sl.leaveCode = r.leaveCode
                LEFT JOIN e_basicinfo b ON b.employeeNo = r.employeeNo
                LEFT JOIN s_department dep ON dep.departmentCode = b.departmentCode
                LEFT JOIN s_branch br ON br.branchCode = b.branchCode
                LEFT JOIN e_basicinfo a ON a.employeeNo = r.requestedByUser
                WHERE r.isActive = 1
                    AND r.statusLevel4 = 'Approved'
                    AND r.leaveCode != 'CTO'
                    AND (@brcode IS NULL OR @brcode = '' OR @brcode = 'ALL' 
                         OR b.branchCode = @brcode)
                    AND (@department IS NULL OR @department = '' OR @department = 'ALL' 
                         OR b.departmentCode = @department)
                    AND (@leavecode IS NULL OR @leavecode = '' OR @leavecode = 'ALL' 
                         OR r.leaveCode = @leavecode)
                    AND (@dateFrom IS NULL OR @dateFrom = '' OR 
                         r.leaveDateFrom BETWEEN DATE(@dateFrom) AND DATE(@dateTo))
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
                query.Append(@" ORDER BY br.branchName, dep.departmentName, 
                                b.lastName, b.firstName, r.leaveDateFrom");
            }

            // Apply pagination
            if (limit > 0)
                query.Append($" LIMIT {limit} OFFSET {offset}");

            var parameters = new DynamicParameters();
            parameters.Add("@brcode",
                string.IsNullOrWhiteSpace(branch) ? null : branch);
            parameters.Add("@department",
                string.IsNullOrWhiteSpace(department) ? null : department);
            parameters.Add("@leavecode",
                string.IsNullOrWhiteSpace(leavecode) ? null : leavecode);
            parameters.Add("@dateFrom",
                string.IsNullOrWhiteSpace(datefrom) ? null : datefrom);
            parameters.Add("@dateTo",
                string.IsNullOrWhiteSpace(dateto) ? null : dateto);

            var result = _db.Query(query.ToString(), parameters);
            var dataList = new List<Dictionary<string, object>>();

            foreach (var row in result)
            {
                var rowDict = (IDictionary<string, object>)row;
                dataList.Add(rowDict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value ?? string.Empty));
            }

            return dataList;
        }

        private string GetDatabaseColumnName(string sortColumn)
        {
            return sortColumn?.ToLower() switch
            {
                "company" => "br.branchName",
                "department" => "dep.departmentName",
                "employeeno" => "r.employeeNo",
                "employeename" => "b.lastName",
                "leavetype" => "sl.leaveName",
                "leavedatefrom" => "r.leaveDateFrom",
                "leavedateto" => "r.leaveDateTo",
                "leavedays" => "r.leaveCountDays",
                "leavereason" => "r.leaveReason",
                "daterequested" => "r.dtAdded",
                "dateapproved" => "r.dtStatus",
                "requestedbyuser" => "a.lastName",
                _ => "r.employeeNo"
            };
        }

        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Leave Request Summary Report");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            var employeeNo = HttpContext.Session.GetString("employeeNo");
            var employeeName = HttpContext.Session.GetString("userFullName") ?? "Unknown User";

            // ROW 1 - Title
            ws.Cells[1, 1].Value = "Leave Request Summary Report";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ROW 2 - Generated by
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({employeeNo}) {employeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // ROW 4 - Headers
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // ROW 5+ - Data
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
            cell.Style.Fill.BackgroundColor.SetColor(
                System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        private bool IsNumericColumn(string columnName)
        {
            var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "LeaveDays"
            };
            return numericColumns.Contains(columnName);
        }
    }
}