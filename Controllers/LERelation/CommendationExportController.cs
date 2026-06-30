using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.LERelation
{
    [ModuleAuthorize("FCommendation")]
    public class CommendationExportController : BaseController
    {
        private readonly IDbConnection _db;

        public CommendationExportController(IDbConnection db) => _db = db;

        [HttpGet]
        public IActionResult ExportToExcel(
            string? employeeNo,
            string? department,
            int offset = 0,
            int limit = 25)
        {
            try
            {
                var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(sessionEmployeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                var employeeInfo = GetEmployeeInfo(sessionEmployeeNo);
                var data = GetCommendationData(employeeNo, department, offset, limit);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, employeeInfo);
                var fileName = $"CommendationList_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(
                    excelFile,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        private (string EmployeeNo, string EmployeeName) GetEmployeeInfo(string employeeNo)
        {
            var userResult = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT userCode,
                       CONCAT(lastName, ', ', firstName, ' ',
                              IFNULL(CONCAT(LEFT(middleName,1),'.'), '')) AS employeeName
                FROM s_user
                WHERE userCode = @employeeNo LIMIT 1",
                new { employeeNo });

            if (userResult != null)
                return ((string)userResult.userCode, (string)userResult.employeeName);

            var empResult = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT employeeNo,
                       CONCAT(lastName, ', ', firstName, ' ',
                              IFNULL(CONCAT(LEFT(middleName,1),'.'), '')) AS employeeName
                FROM e_basicinfo
                WHERE employeeNo = @employeeNo LIMIT 1",
                new { employeeNo });

            return empResult != null
                ? ((string)empResult.employeeNo, (string)empResult.employeeName)
                : (employeeNo, "Unknown User");
        }

        private List<Dictionary<string, object>> GetCommendationData(
            string? employeeNo,
            string? department,
            int offset,
            int limit)
        {
            var sql = new StringBuilder(@"
                SELECT
                    c.employeeNo                                    AS 'Employee No',
                    CONCAT(
                        IFNULL(e.firstName, ''), ' ',
                        IFNULL(CONCAT(e.middleName, ' '), ''),
                        IFNULL(e.lastName, ''))                     AS 'Employee Name',
                    IFNULL(dep.departmentName, '')                  AS 'Department',
                    IFNULL(s.commendationName, '')                  AS 'Commendation Type',
                    IFNULL(c.activity, '')                          AS 'Activity',
                    IFNULL(c.addedby, '')                           AS 'Issued By',
                    DATE_FORMAT(c.dateissued, '%Y/%m/%d')           AS 'Date Issued',
                    IFNULL(c.remarks, '')                           AS 'Remarks'
                FROM e_commendation c
                LEFT JOIN e_basicinfo e
                       ON e.employeeNo = c.employeeNo
                LEFT JOIN s_commendation s
                       ON s.commendationCode = c.commendationCode
                LEFT JOIN s_department dep
                       ON dep.departmentCode = e.departmentCode
                WHERE c.isActive = 1
                  AND e.isActive = 1");

            var parameters = new DynamicParameters();

            DataScopeHelper.ApplyDataScopeFilter(
                _db, sql, parameters, EmployeeNo, RoleCode, tableAlias: "e");
            DataScopeHelper.ApplyHiddenEmployeesFilter(
                _db, sql, parameters, EmployeeNo, RoleCode, tableAlias: "e");

            if (!string.IsNullOrWhiteSpace(employeeNo))
            {
                sql.Append(" AND c.employeeNo = @empFilter");
                parameters.Add("@empFilter", employeeNo);
            }

            if (!string.IsNullOrWhiteSpace(department) &&
                !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND e.departmentCode = @department");
                parameters.Add("@department", department);
            }

            sql.Append(" ORDER BY e.lastName, e.firstName, c.dateissued DESC");

            if (limit > 0)
            {
                sql.Append(" LIMIT @limit OFFSET @offset");
                parameters.Add("@limit", limit);
                parameters.Add("@offset", offset);
            }

            var rows = _db.Query(sql.ToString(), parameters);
            var result = new List<Dictionary<string, object>>();

            foreach (var row in rows)
            {
                var dict = (IDictionary<string, object>)row;
                result.Add(dict.ToDictionary(k => k.Key, k => k.Value ?? string.Empty));
            }

            return result;
        }

        private byte[] GenerateExcelFile(
            List<Dictionary<string, object>> data,
            (string EmployeeNo, string EmployeeName) employeeInfo)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Commendation List");
            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            ws.Cells[1, 1].Value = "Commendation List Report";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            ws.Cells[2, 1].Value = $"Generated By: ({employeeInfo.EmployeeNo}) {employeeInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            for (int col = 0; col < columns.Count; col++)
            {
                StyleHeader(ws.Cells[4, col + 1]);
                ws.Cells[4, col + 1].Value = columns[col];
            }

            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    var cellValue = data[row][columns[col]];
                    cell.Value = cellValue?.ToString() ?? string.Empty;

                    if (row % 2 == 1)
                    {
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(
                            System.Drawing.Color.FromArgb(242, 242, 242));
                    }

                    if (columns[col] is "Activity" or "Remarks")
                        cell.Style.WrapText = true;
                }
            }

            var tableRange = ws.Cells[4, 1, rowCount + 4, columns.Count];
            tableRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells[ws.Dimension.Address].AutoFitColumns();

            int activityColIdx = columns.IndexOf("Activity") + 1;
            int remarksColIdx = columns.IndexOf("Remarks") + 1;
            if (activityColIdx > 0) ws.Column(activityColIdx).Width = 45;
            if (remarksColIdx > 0) ws.Column(remarksColIdx).Width = 45;

            return package.GetAsByteArray();
        }

        private static void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(
                System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }
    }
}