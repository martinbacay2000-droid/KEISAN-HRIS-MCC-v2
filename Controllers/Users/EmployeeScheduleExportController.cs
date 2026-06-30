using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FscheduleM")]
    public class EmployeeScheduleExportController : BaseController
    {
        private readonly IDbConnection _db;

        public EmployeeScheduleExportController(IDbConnection db) => _db = db;

        /// <summary>
        /// Exports individual schedule rows (one per employee/weekday/effectivity date) to Excel.
        /// Mirrors the filters of GetScheduleList. The offset/limit apply to the grouped
        /// employee+scheduleType pairs (i.e. the current DataTable page), so the exported
        /// row count may exceed the page size once each pair is expanded to individual rows.
        /// </summary>
        [HttpGet]
        public IActionResult ExportToExcel(
            string? employeeNo,
            string? branch,
            string? department,
            string? dateFrom,
            string? dateTo,
            int offset = 0,
            int limit = 25)
        {
            try
            {
                var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(sessionEmployeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                var employeeInfo = GetEmployeeInfo(sessionEmployeeNo);
                var data = GetScheduleData(employeeNo, branch, department, dateFrom, dateTo, offset, limit);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, employeeInfo);
                var fileName = $"EmployeeSchedule_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

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

        // ─────────────────────────────────────────────────────────────────────────
        // Data retrieval
        // ─────────────────────────────────────────────────────────────────────────

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

        /// <summary>
        /// Two-step strategy:
        ///   1. Run the same grouped query as GetScheduleList with LIMIT/OFFSET
        ///      to find which employeeNo+scheduleTypeCode pairs are on the current page.
        ///   2. Fetch all individual rows for those pairs (still honouring date filters).
        /// </summary>
        private List<Dictionary<string, object>> GetScheduleData(
            string? employeeNo, string? branch, string? department,
            string? dateFrom, string? dateTo, int offset, int limit)
        {
            // ── Step 1: identify the paged grouped keys ──────────────────────────
            var groupedSql = new StringBuilder(@"
                SELECT e.employeeNo, e.scheduleTypeCode
                FROM e_schedule e
                LEFT JOIN e_basicinfo a    ON a.employeeNo       = e.employeeNo
                LEFT JOIN s_scheduleType s ON s.scheduleTypeCode = e.scheduleTypeCode
                WHERE e.isActive = 1 AND a.isActive = 1");

            var groupedParams = new DynamicParameters();
            DataScopeHelper.ApplyDataScopeFilter(_db, groupedSql, groupedParams, EmployeeNo, RoleCode, tableAlias: "a");
            DataScopeHelper.ApplyHiddenEmployeesFilter(_db, groupedSql, groupedParams, EmployeeNo, RoleCode, tableAlias: "a");
            AppendCommonFilters(groupedSql, groupedParams, employeeNo, branch, department, dateFrom, dateTo);

            groupedSql.Append(" GROUP BY e.employeeNo, e.scheduleTypeCode");
            groupedSql.Append(" ORDER BY CONCAT(a.lastName, ', ', a.firstName)");

            if (limit > 0)
            {
                groupedSql.Append(" LIMIT @limit OFFSET @offset");
                groupedParams.Add("@limit", limit);
                groupedParams.Add("@offset", offset);
            }

            var groupedKeys = _db.Query<dynamic>(groupedSql.ToString(), groupedParams).ToList();

            if (!groupedKeys.Any())
                return new List<Dictionary<string, object>>();

            // ── Step 2: expand to individual rows ────────────────────────────────
            var detailSql = new StringBuilder(@"
                SELECT
                    e.employeeNo                                   AS 'Employee No',
                    CONCAT(a.lastName, ', ', a.firstName)          AS 'Full Name',
                    DATE_FORMAT(e.effectivityDate, '%Y-%m-%d')     AS 'Effectivity Date',
                    IFNULL(s.scheduleTypeName, '')                 AS 'Schedule Type',
                    e.weekdayName                                  AS 'Weekday',
                    IFNULL(TIME_FORMAT(e.timeIn,  '%H:%i'), '')    AS 'Time In',
                    IFNULL(TIME_FORMAT(e.timeOut, '%H:%i'), '')    AS 'Time Out',
                    IFNULL(e.totalRenderHour, 0)                   AS 'Render Hours',
                    IFNULL(e.totalBreaktimeMinute, 0)              AS 'Break (mins)',
                    CASE WHEN e.isRestDay = 1 THEN 'Yes' ELSE 'No' END AS 'Rest Day'
                FROM e_schedule e
                LEFT JOIN e_basicinfo a    ON a.employeeNo       = e.employeeNo
                LEFT JOIN s_scheduleType s ON s.scheduleTypeCode = e.scheduleTypeCode
                WHERE e.isActive = 1 AND a.isActive = 1
                AND (");

            var detailParams = new DynamicParameters();
            var orClauses = new List<string>();

            for (int i = 0; i < groupedKeys.Count; i++)
            {
                orClauses.Add($"(e.employeeNo = @emp{i} AND e.scheduleTypeCode = @stype{i})");
                detailParams.Add($"emp{i}", (string)groupedKeys[i].employeeNo);
                detailParams.Add($"stype{i}", (string)groupedKeys[i].scheduleTypeCode);
            }

            detailSql.Append(string.Join(" OR ", orClauses));
            detailSql.Append(")");

            // Re-apply date filter so only the requested effectivity range appears
            if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
            {
                detailSql.Append(" AND DATE(e.effectivityDate) BETWEEN @dateFrom AND @dateTo");
                detailParams.Add("@dateFrom", dateFrom);
                detailParams.Add("@dateTo", dateTo);
            }

            detailSql.Append(@"
                ORDER BY CONCAT(a.lastName, ', ', a.firstName),
                         e.effectivityDate,
                         FIELD(e.weekdayName,
                               'Monday','Tuesday','Wednesday',
                               'Thursday','Friday','Saturday','Sunday')");

            var rows = _db.Query(detailSql.ToString(), detailParams);
            var result = new List<Dictionary<string, object>>();

            foreach (var row in rows)
            {
                var dict = (IDictionary<string, object>)row;
                result.Add(dict.ToDictionary(k => k.Key, k => k.Value ?? string.Empty));
            }

            return result;
        }

        private static void AppendCommonFilters(
            StringBuilder sql, DynamicParameters p,
            string? employeeNo, string? branch, string? department,
            string? dateFrom, string? dateTo)
        {
            if (!string.IsNullOrWhiteSpace(employeeNo))
            {
                sql.Append(" AND e.employeeNo = @employeeNo");
                p.Add("@employeeNo", employeeNo);
            }

            if (!string.IsNullOrWhiteSpace(branch) &&
                !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND a.branchCode = @branch");
                p.Add("@branch", branch);
            }

            if (!string.IsNullOrWhiteSpace(department) &&
                !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND a.departmentCode = @department");
                p.Add("@department", department);
            }

            if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
            {
                sql.Append(" AND DATE(e.effectivityDate) BETWEEN @dateFrom AND @dateTo");
                p.Add("@dateFrom", dateFrom);
                p.Add("@dateTo", dateTo);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Excel generation
        // ─────────────────────────────────────────────────────────────────────────

        private byte[] GenerateExcelFile(
            List<Dictionary<string, object>> data,
            (string EmployeeNo, string EmployeeName) employeeInfo)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Employee Schedules");
            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // ── Row 1: Title ──────────────────────────────────────────────────────
            ws.Cells[1, 1].Value = "Employee Schedule";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ── Row 2: Generated by + timestamp ──────────────────────────────────
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            ws.Cells[2, 1].Value = $"Generated By: ({employeeInfo.EmployeeNo}) {employeeInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // ── Row 4: Column headers (row 3 is blank for spacing) ───────────────
            for (int col = 0; col < columns.Count; col++)
            {
                StyleHeader(ws.Cells[4, col + 1]);
                ws.Cells[4, col + 1].Value = columns[col];
            }

            // ── Rows 5+: Data ─────────────────────────────────────────────────────
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    var cellValue = data[row][columns[col]];
                    cell.Value = cellValue?.ToString() ?? string.Empty;

                    // Alternate row shading for readability
                    if (row % 2 == 1)
                    {
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(
                            System.Drawing.Color.FromArgb(242, 242, 242));
                    }

                    // Highlight Rest Day cells in light blue
                    if (columns[col] == "Rest Day" && cellValue?.ToString() == "Yes")
                    {
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(
                            System.Drawing.Color.FromArgb(189, 215, 238));
                        cell.Style.Font.Bold = true;
                    }
                }
            }

            // ── Borders ───────────────────────────────────────────────────────────
            var tableRange = ws.Cells[4, 1, rowCount + 4, columns.Count];
            tableRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells[ws.Dimension.Address].AutoFitColumns();

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