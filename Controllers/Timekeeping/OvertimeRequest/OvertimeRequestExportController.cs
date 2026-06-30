using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.OvertimeRequest
{
    [ModuleAuthorize("RovertimeM")]
    public class OvertimeRequestExportController : BaseController
    {
        private readonly IDbConnection _db;

        public OvertimeRequestExportController(IDbConnection db) => _db = db;

        // Exports overtime request data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(string? status, string? branch, string? department,
            string? dateFrom, string? dateTo, int offset = 0, int limit = -1,
            string? sortColumn = "", string? sortDirection = "desc")
        {
            try
            {
                // Get current employee number from session
                var employeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(employeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                // Get employee details
                var employeeInfo = GetEmployeeInfo(employeeNo);

                var data = GetOvertimeRequestData(status, branch, department, dateFrom, dateTo,
                    offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, employeeInfo);
                var fileName = $"OvertimeRequest_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

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

        // Retrieves overtime request records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetOvertimeRequestData(string? status, string? branch,
            string? department, string? dateFrom, string? dateTo, int offset, int limit,
            string? sortColumn, string? sortDirection)
        {
            var query = new StringBuilder(@"
                SELECT 
                    rq.employeeNo AS 'Employee No',
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(CONCAT(LEFT(b.middleName, 1), '.'), '')) AS 'Employee Name',
                    DATE_FORMAT(rq.overTimeDateIN, '%Y-%m-%d') AS 'Date In',
                    TIME_FORMAT(rq.overTimeIN, '%H:%i') AS 'Time In',
                    DATE_FORMAT(rq.overTimeDateOUT, '%Y-%m-%d') AS 'Date Out',
                    TIME_FORMAT(rq.overTimeOUT, '%H:%i') AS 'Time Out',
                    rq.overTimeReason AS 'Reason',
                    IFNULL(rq.remarks, '') AS 'Remarks',
                    rq.statusLevel4 AS 'Status',
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(CONCAT(LEFT(req.middleName, 1), '.'), '')) AS 'Requested By',
                    DATE_FORMAT(rq.dtAdded, '%Y-%m-%d %H:%i:%s') AS 'Date Requested'
                FROM rq_overtime rq
                INNER JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                WHERE rq.isActive = 1");

            var parameters = new DynamicParameters();
            DataScopeHelper.ApplyDataScopeFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "b");
            DataScopeHelper.ApplyHiddenEmployeesFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "b");
            ApplyFilters(query, parameters, status, branch, department, dateFrom, dateTo);

            // Apply sorting
            var validSortColumns = new Dictionary<string, string>
            {
                { "employeeName", "b.lastName" },
                { "displayDateIn", "rq.overTimeDateIN" },
                { "displayDateOut", "rq.overTimeDateOUT" },
                { "displayTimeIn", "rq.overTimeIN" },
                { "displayTimeOut", "rq.overTimeOUT" },
                { "overtimeReason", "rq.overTimeReason" },
                { "statusName", "rq.statusLevel4" },
                { "requestedByUser", "req.lastName" }
            };

            if (!string.IsNullOrEmpty(sortColumn) && validSortColumns.ContainsKey(sortColumn))
            {
                var direction = sortDirection?.ToLower() == "asc" ? "ASC" : "DESC";
                query.Append($" ORDER BY {validSortColumns[sortColumn]} {direction}");
            }
            else
            {
                query.Append(" ORDER BY rq.id DESC");
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
            string? status, string? branch, string? department, string? dateFrom, string? dateTo)
        {
            // Status filter
            if (string.IsNullOrWhiteSpace(status) || status == "Default")
            {
                query.Append(" AND rq.statusLevel4 = @status");
                parameters.Add("@status", "Pending");
            }
            else if (status != "All")
            {
                query.Append(" AND rq.statusLevel4 = @status");
                parameters.Add("@status", status);
            }

            // Branch filter
            if (!string.IsNullOrWhiteSpace(branch) && branch != "ALL")
            {
                query.Append(" AND b.branchCode = @branch");
                parameters.Add("@branch", branch);
            }

            // Department filter
            if (!string.IsNullOrWhiteSpace(department) && department != "ALL")
            {
                query.Append(" AND b.departmentCode = @department");
                parameters.Add("@department", department);
            }

            // Date range filter — handle both mm/dd/yyyy (from UI) and yyyy-MM-dd formats
            if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
            {
                string parsedFrom = dateFrom;
                string parsedTo = dateTo;

                if (DateTime.TryParseExact(dateFrom, "MM/dd/yyyy", null,
                        System.Globalization.DateTimeStyles.None, out var dtFrom))
                    parsedFrom = dtFrom.ToString("yyyy-MM-dd");

                if (DateTime.TryParseExact(dateTo, "MM/dd/yyyy", null,
                        System.Globalization.DateTimeStyles.None, out var dtTo))
                    parsedTo = dtTo.ToString("yyyy-MM-dd");

                query.Append(" AND DATE(rq.overTimeDateIN) BETWEEN @dateFrom AND @dateTo");
                parameters.Add("@dateFrom", parsedFrom);
                parameters.Add("@dateTo", parsedTo);
            }
        }

        // Generates an Excel file from the provided data with formatted headers, borders, and status color coding
        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data, (string EmployeeNo, string EmployeeName) employeeInfo)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Overtime Requests");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // Add main title (Row 1)
            ws.Cells[1, 1].Value = "Overtime Requests";
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
                    cell.Value = cellValue?.ToString() ?? string.Empty;

                    if (columns[col] == "Status" && cellValue != null)
                        ApplyStatusColor(cell, cellValue.ToString() ?? string.Empty);
                }
            }

            // Format table (apply borders from Row 4 onwards)
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            var range = ws.Cells[4, 1, rowCount + 4, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

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

        // Applies color coding to status cells based on status value (green for approved, red for declined, etc.)
        private void ApplyStatusColor(ExcelRange cell, string status)
        {
            if (string.IsNullOrEmpty(status)) return;

            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Font.Bold = true;

            var (bgColor, fgColor) = status.ToUpper() switch
            {
                "APPROVED" => (System.Drawing.Color.FromArgb(198, 239, 206), System.Drawing.Color.FromArgb(0, 97, 0)),
                "DECLINED" => (System.Drawing.Color.FromArgb(255, 199, 206), System.Drawing.Color.FromArgb(156, 0, 6)),
                "CANCELLED" => (System.Drawing.Color.FromArgb(196, 196, 196), System.Drawing.Color.FromArgb(68, 68, 68)),
                "PROCESSED" => (System.Drawing.Color.FromArgb(180, 235, 250), System.Drawing.Color.FromArgb(0, 67, 88)),
                _ => (System.Drawing.Color.FromArgb(255, 242, 204), System.Drawing.Color.FromArgb(156, 101, 0))
            };

            cell.Style.Fill.BackgroundColor.SetColor(bgColor);
            cell.Style.Font.Color.SetColor(fgColor);
        }
    }
}