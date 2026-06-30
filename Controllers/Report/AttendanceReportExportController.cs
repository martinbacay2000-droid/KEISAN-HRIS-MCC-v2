using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Drawing;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTAttendanceReportM")]
    public class AttendanceReportExportController : BaseController
    {
        private readonly IDbConnection _db;

        public AttendanceReportExportController(IDbConnection db)
        {
            _db = db;
        }

        [HttpGet]
        public IActionResult ExportToExcel(string status, string branch, string department,
                           string dateMonth, string dateYear,
                           string sortColumn = "", string sortDir = "asc")
        {
            // Convert month name to number if necessary (e.g. "April" → 4)
            int dtMonth;
            if (!int.TryParse(dateMonth, out dtMonth))
            {
                dtMonth = DateTime.ParseExact(dateMonth, "MMMM", System.Globalization.CultureInfo.InvariantCulture).Month;
            }

            string query = "";

            switch (status)
            {
                case "PerfectAttendance":
                    query = @"
                        SELECT * FROM (
                            SELECT
                                b.employeeNo,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode,
                                b.departmentCode,
                                b.employmentStatus,
                                b.positionCode,
                                MAX(CASE WHEN ob.id IS NOT NULL THEN ob.id ELSE 0 END) AS withOB,
                                SUM(CASE
                                    WHEN p.attendanceStatus = 'NO SCHEDULE'
                                      OR (p.absentCount = 1 AND IFNULL(ob.id, 0) > 0)
                                    THEN 0
                                    ELSE p.absentCount
                                END) AS absentCount,
                                SUM(CASE WHEN p.attendanceStatus = 'ON LEAVE' THEN 1 ELSE 0 END) AS paidLeave,
                                SUM(CASE WHEN p.attendanceStatus NOT IN ('NO SCHEDULE','Absent','NO PAY LEAVE')
                                         AND (p.absentCount = 0 OR IFNULL(ob.id, 0) > 0)
                                    THEN 1 ELSE 0 END) AS presentDays,
                                SUM(IFNULL(p.renderLate, 0)) AS totalLate,
                                SUM(IFNULL(p.renderUndertime, 0)) AS totalUndertime,
                                SUM(CASE WHEN p.attendanceStatus IN ('NO PAY LEAVE','MATERNITY LEAVE','PATERNITY LEAVE','SUSPENDED')
                                    THEN 1 ELSE 0 END) AS specialLeaveCount
                            FROM p_biometricsline p
                            LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                            LEFT JOIN rq_officialbusiness ob
                                ON ob.employeeNo = p.employeeNo
                                AND p.date BETWEEN ob.obDateIn AND ob.obDateOut
                                AND ob.statusLevel4 = 'Approved'
                            WHERE MONTH(p.date) = @dtMonth
                              AND YEAR(p.date)  = @dtYear
                              AND p.statusName = 'Posted'
                              AND p.isActive = 1
                              AND (@department = '' OR @department IS NULL OR @department = 'ALL' OR b.departmentCode = @department)
                              AND (@brcode = '' OR @brcode IS NULL OR @brcode = 'ALL' OR b.branchCode = @brcode)
                            GROUP BY
                                p.employeeNo,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode,
                                b.departmentCode,
                                b.employmentStatus,
                                b.positionCode
                        ) tbl1
                        WHERE tbl1.absentCount = 0
                          AND tbl1.totalLate = 0
                          AND tbl1.totalUndertime = 0
                          AND tbl1.specialLeaveCount = 0
                        ORDER BY branchCode, departmentCode, lastName;";
                    break;

                case "AbsentDetail":
                    query = @"
                        SELECT * FROM (
                            SELECT
                                b.employeeNo,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode,
                                b.departmentCode,
                                b.employmentStatus,
                                b.positionCode,
                                p.date AS dateAbsent,
                                p.scheduleIn,
                                p.scheduleOut,
                                p.attendanceStatus,
                                ob.id AS obID,
                                SUM(CASE
                                    WHEN p.attendanceStatus = 'NO SCHEDULE'
                                      OR (p.absentCount = 1 AND IFNULL(ob.id, 0) > 0)
                                    THEN 0
                                    ELSE p.absentCount
                                END) AS absentCount,
                                SUM(CASE WHEN p.attendanceStatus = 'NO TIMEOUT'   AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS NoTimeOut,
                                SUM(CASE WHEN p.attendanceStatus = 'ABSENT'       AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS AWOL,
                                SUM(CASE WHEN p.attendanceStatus = 'NO PAY LEAVE' AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS AbsentWithLeave
                            FROM p_biometricsline p
                            LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                            LEFT JOIN rq_officialbusiness ob
                                ON ob.employeeNo = p.employeeNo
                                AND p.date BETWEEN ob.obDateIn AND ob.obDateOut
                                AND ob.statusLevel4 = 'Approved'
                            WHERE MONTH(p.date) = @dtMonth
                              AND YEAR(p.date)  = @dtYear
                              AND p.statusName = 'Posted'
                              AND p.isActive = 1
                              AND (@department = '' OR @department IS NULL OR @department = 'ALL' OR b.departmentCode = @department)
                              AND (@brcode = '' OR @brcode IS NULL OR @brcode = 'ALL' OR b.branchCode = @brcode)
                            GROUP BY p.employeeNo, p.date,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode, b.departmentCode,
                                b.employmentStatus, b.positionCode,
                                p.scheduleIn, p.scheduleOut, p.attendanceStatus, ob.id
                        ) tbl1
                        WHERE tbl1.absentCount > 0
                        ORDER BY branchCode, departmentCode, lastName;";
                    break;

                case "AbsentSummary":
                    query = @"
                        SELECT * FROM (
                            SELECT
                                b.employeeNo,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode,
                                b.departmentCode,
                                b.employmentStatus,
                                b.positionCode,
                                SUM(CASE
                                    WHEN p.attendanceStatus = 'NO SCHEDULE'
                                      OR (p.absentCount = 1 AND IFNULL(ob.id, 0) > 0)
                                    THEN 0
                                    ELSE p.absentCount
                                END) AS absentCount,
                                SUM(CASE WHEN p.attendanceStatus = 'NO TIMEOUT'   AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS NoTimeOut,
                                SUM(CASE WHEN p.attendanceStatus = 'ABSENT'       AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS AWOL,
                                SUM(CASE WHEN p.attendanceStatus = 'NO PAY LEAVE' AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS AbsentWithLeave
                            FROM p_biometricsline p
                            LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                            LEFT JOIN rq_officialbusiness ob
                                ON ob.employeeNo = p.employeeNo
                                AND p.date BETWEEN ob.obDateIn AND ob.obDateOut
                                AND ob.statusLevel4 = 'Approved'
                            WHERE MONTH(p.date) = @dtMonth
                              AND YEAR(p.date)  = @dtYear
                              AND p.statusName = 'Posted'
                              AND p.isActive = 1
                              AND (@department = '' OR @department IS NULL OR @department = 'ALL' OR b.departmentCode = @department)
                              AND (@brcode = '' OR @brcode IS NULL OR @brcode = 'ALL' OR b.branchCode = @brcode)
                            GROUP BY p.employeeNo,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode, b.departmentCode,
                                b.employmentStatus, b.positionCode
                        ) tbl1
                        WHERE tbl1.absentCount > 0
                        ORDER BY branchCode, departmentCode, lastName;";
                    break;

                case "TardinessDetail":
                    query = @"
                        SELECT
                            *,
                            CASE WHEN renderLate > 0 THEN 1 ELSE 0 END AS lateFrequency,
                            CASE WHEN renderUndertime > 0 THEN 1 ELSE 0 END AS undertimeFrequency
                        FROM (
                            SELECT
                                b.employeeNo,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode,
                                b.departmentCode,
                                b.employmentStatus,
                                b.positionCode,
                                p.date AS dateAbsent,
                                p.scheduleIn,
                                p.scheduleOut,
                                p.timeIn,
                                p.timeOut,
                                p.attendanceStatus,
                                CASE WHEN IFNULL(ob.id, 0) > 0 THEN
                                    GREATEST(TIMESTAMPDIFF(MINUTE, p.scheduleIn, TIMESTAMP(ob.obDateIn, ob.obTimeIn)), 0)
                                ELSE GREATEST(p.renderLate, 0) END AS renderLate,
                                CASE WHEN IFNULL(ob.id, 0) > 0 THEN
                                    TIMESTAMPDIFF(MINUTE, TIMESTAMP(ob.obDateOut, ob.obTimeOut), p.scheduleOut)
                                ELSE p.renderUndertime END AS renderUndertime
                            FROM p_biometricsline p
                            LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                            LEFT JOIN rq_officialbusiness ob
                                ON ob.employeeNo = p.employeeNo
                                AND p.date BETWEEN ob.obDateIn AND ob.obDateOut
                                AND ob.statusLevel4 = 'Approved'
                            WHERE MONTH(p.date) = @dtMonth
                              AND YEAR(p.date)  = @dtYear
                              AND p.statusName = 'Posted'
                              AND p.isActive = 1
                              AND (@department = '' OR @department IS NULL OR @department = 'ALL' OR b.departmentCode = @department)
                              AND (@brcode = '' OR @brcode IS NULL OR @brcode = 'ALL' OR b.branchCode = @brcode)
                        ) tbl1
                        WHERE tbl1.renderLate + tbl1.renderUndertime > 0
                        ORDER BY branchCode, departmentCode, lastName;";
                    break;

                case "TardinessSummary":
                    query = @"
                        SELECT
                            employeeNo, lastName, firstName, middleName,
                            branchCode, departmentCode, employmentStatus, positionCode,
                            SUM(renderLate) AS totalLate,
                            SUM(renderUndertime) AS totalUndertime,
                            SUM(CASE WHEN renderLate > 0 THEN 1 ELSE 0 END) AS lateFrequency,
                            SUM(CASE WHEN renderUndertime > 0 THEN 1 ELSE 0 END) AS undertimeFrequency
                        FROM (
                            SELECT
                                b.employeeNo,
                                b.lastName, b.firstName, b.middleName,
                                b.branchCode,
                                b.departmentCode,
                                b.employmentStatus,
                                b.positionCode,
                                CASE WHEN IFNULL(ob.id, 0) > 0 THEN
                                    GREATEST(TIMESTAMPDIFF(MINUTE, p.scheduleIn, TIMESTAMP(ob.obDateIn, ob.obTimeIn)), 0)
                                ELSE GREATEST(p.renderLate, 0) END AS renderLate,
                                CASE WHEN IFNULL(ob.id, 0) > 0 THEN
                                    TIMESTAMPDIFF(MINUTE, TIMESTAMP(ob.obDateOut, ob.obTimeOut), p.scheduleOut)
                                ELSE p.renderUndertime END AS renderUndertime
                            FROM p_biometricsline p
                            LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                            LEFT JOIN rq_officialbusiness ob
                                ON ob.employeeNo = p.employeeNo
                                AND p.date BETWEEN ob.obDateIn AND ob.obDateOut
                                AND ob.statusLevel4 = 'Approved'
                            WHERE MONTH(p.date) = @dtMonth
                              AND YEAR(p.date)  = @dtYear
                              AND p.statusName = 'Posted'
                              AND p.isActive = 1
                              AND (@department = '' OR @department IS NULL OR @department = 'ALL' OR b.departmentCode = @department)
                              AND (@brcode = '' OR @brcode IS NULL OR @brcode = 'ALL' OR b.branchCode = @brcode)
                        ) tbl1
                        WHERE tbl1.renderLate + tbl1.renderUndertime > 0
                        GROUP BY employeeNo, lastName, firstName, middleName,
                                 branchCode, departmentCode, employmentStatus, positionCode
                        ORDER BY branchCode, departmentCode, lastName;";
                    break;

                default:
                    return BadRequest("Invalid report type.");
            }

            var p = new DynamicParameters();
            p.Add("@brcode", string.IsNullOrWhiteSpace(branch) ? "ALL" : branch);
            p.Add("@department", string.IsNullOrWhiteSpace(department) ? "ALL" : department);
            p.Add("@dtMonth", dtMonth);
            p.Add("@dtYear", dateYear);

            var data = _db.Query<AttendanceReportModel>(query, p).ToList();

            // Apply DataTable sort order if a valid column was passed
            if (!string.IsNullOrWhiteSpace(sortColumn))
            {
                var prop = typeof(AttendanceReportModel).GetProperty(sortColumn,
                    System.Reflection.BindingFlags.IgnoreCase |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance);

                if (prop != null)
                {
                    data = sortDir == "desc"
                        ? data.OrderByDescending(x => prop.GetValue(x)).ToList()
                        : data.OrderBy(x => prop.GetValue(x)).ToList();
                }
            }

            // Handle computed "employeeName" sort (lastName, firstName, middleName)
            if (!string.IsNullOrWhiteSpace(sortColumn) &&
                sortColumn.Equals("employeeName", StringComparison.OrdinalIgnoreCase))
            {
                data = sortDir == "desc"
                    ? data.OrderByDescending(x => $"{x.lastName},{x.firstName},{x.middleName}").ToList()
                    : data.OrderBy(x => $"{x.lastName},{x.firstName},{x.middleName}").ToList();
            }

            // ── Employee info from session ────────────────────────────────────────
            var employeeNo = HttpContext.Session.GetString("employeeNo") ?? string.Empty;
            var employeeInfo = GetEmployeeInfo(employeeNo);

            // ── Build Excel ──────────────────────────────────────────────────────
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add(GetSheetName(status));

            var columns = GetColumns(status);
            int colCount = columns.Count;

            // ── Row 1: Main title ────────────────────────────────────────────────
            var reportName = GetSheetName(status);
            ws.Cells[1, 1].Value = reportName;
            ws.Cells[1, 1, 1, colCount].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ── Row 2: Generated-by + timestamp ─────────────────────────────────
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            ws.Cells[2, 1].Value =
                $"Generated By: ({employeeInfo.EmployeeNo}) {employeeInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1, 2, colCount].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // ── Row 3: blank spacer ───────────────────────────────────────────────

            // ── Row 4: Column headers ────────────────────────────────────────────
            for (int c = 0; c < colCount; c++)
            {
                var cell = ws.Cells[4, c + 1];
                cell.Value = columns[c].Header;
                StyleHeader(cell);
            }

            // ── Rows 5+: Data ────────────────────────────────────────────────────
            int dataStartRow = 5;
            int row = dataStartRow;
            foreach (var item in data)
            {
                for (int c = 0; c < colCount; c++)
                {
                    var cell = ws.Cells[row, c + 1];
                    var val = columns[c].ValueSelector(item);

                    if (columns[c].IsNumeric && val != null &&
                        decimal.TryParse(val.ToString(), out decimal numVal))
                    {
                        cell.Value = numVal;
                        cell.Style.Numberformat.Format = "#,##0.##";
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }
                    else
                    {
                        cell.Value = val;
                    }
                }
                row++;
            }

            // ── Totals row ───────────────────────────────────────────────────────
            int totalRow = row;
            for (int c = 0; c < colCount; c++)
            {
                var cell = ws.Cells[totalRow, c + 1];

                if (c == 0)
                {
                    cell.Value = "TOTAL";
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }
                else if (columns[c].IsNumeric)
                {
                    decimal sum = 0;
                    foreach (var item in data)
                    {
                        var val = columns[c].ValueSelector(item);
                        if (val != null && decimal.TryParse(val.ToString(), out decimal v))
                            sum += v;
                    }
                    cell.Value = sum;
                    cell.Style.Numberformat.Format = "#,##0.##";
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                }
                else
                {
                    cell.Value = "";
                }

                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(217, 217, 217));
                cell.Style.Font.Bold = true;
            }

            // ── Borders on header + data + totals (rows 4 to totalRow) ──────────
            var tableRange = ws.Cells[4, 1, totalRow, colCount];
            tableRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            tableRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            // Thicker top border on totals row for visual separation
            ws.Cells[totalRow, 1, totalRow, colCount].Style.Border.Top.Style = ExcelBorderStyle.Medium;

            // ── Auto-fit & freeze header ─────────────────────────────────────────
            ws.Cells[ws.Dimension?.Address ?? "A1"].AutoFitColumns();
            ws.View.FreezePanes(5, 1);

            var fileBytes = package.GetAsByteArray();
            var fileName = $"AttendanceReport_{status}_{dateYear}_{dateMonth}_{DateTimeOffset.Now.ToUnixTimeSeconds()}.xlsx";

            return File(fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private (string EmployeeNo, string EmployeeName) GetEmployeeInfo(string employeeNo)
        {
            if (string.IsNullOrWhiteSpace(employeeNo))
                return ("N/A", "Unknown User");

            var userQuery = @"
                SELECT userCode,
                       CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName,1),'.'), '')) AS employeeName
                FROM s_user WHERE userCode = @employeeNo LIMIT 1";

            var userResult = _db.QueryFirstOrDefault<dynamic>(userQuery, new { employeeNo });
            if (userResult != null)
                return (userResult.userCode, userResult.employeeName);

            var empQuery = @"
                SELECT employeeNo,
                       CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName,1),'.'), '')) AS employeeName
                FROM e_basicinfo WHERE employeeNo = @employeeNo LIMIT 1";

            var empResult = _db.QueryFirstOrDefault<dynamic>(empQuery, new { employeeNo });
            if (empResult != null)
                return (empResult.employeeNo, empResult.employeeName);

            return (employeeNo, "Unknown User");
        }

        private static void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        private static string GetSheetName(string status) => status switch
        {
            "PerfectAttendance" => "Perfect Attendance",
            "AbsentDetail" => "Absent Detail",
            "AbsentSummary" => "Absent Summary",
            "TardinessDetail" => "Tardiness Detail",
            "TardinessSummary" => "Tardiness Summary",
            _ => "Report"
        };

        private static string? FormatTime12Hr(object? value)
        {
            if (value == null) return null;
            var str = value.ToString();
            if (string.IsNullOrWhiteSpace(str)) return str;

            if (TimeSpan.TryParse(str, out var ts))
            {
                var dt = DateTime.Today.Add(ts);
                return dt.ToString("h:mm tt");
            }

            if (DateTime.TryParse(str, out var dt2))
                return dt2.ToString("h:mm tt");

            return str;
        }

        private static string? FormatDateOnly(object? value)
        {
            if (value == null) return null;
            var str = value.ToString();
            if (string.IsNullOrWhiteSpace(str)) return str;

            if (DateTime.TryParse(str, out var dt))
                return dt.ToString("M/d/yyyy");

            return str;
        }

        private record ColDef(string Header, Func<AttendanceReportModel, object?> ValueSelector, bool IsNumeric = false);

        private static List<ColDef> GetColumns(string status)
        {
            var common = new List<ColDef>
            {
                new("Employee No",        m => m.employeeNo),
                new("Employee Name",      m => $"{m.lastName}, {m.firstName} {m.middleName}".Trim()),
                new("Branch",             m => m.branchCode),
                new("Department",         m => m.departmentCode),
                new("Employment Status",  m => m.employmentStatus),
                new("Position",           m => m.positionCode),
            };

            return status switch
            {
                "PerfectAttendance" => new List<ColDef>(common)
                {
                    new("Present Days",    m => m.presentDays, true),
                    new("Paid Leave Days", m => m.paidLeave,   true),
                    new("With OB",         m => m.withOB,      true),
                },

                "AbsentDetail" => new List<ColDef>(common)
                {
                    new("Date Absent",      m => FormatDateOnly(m.dateAbsent)),
                    new("Schedule In",      m => FormatTime12Hr(m.scheduleIn)),
                    new("Schedule Out",     m => FormatTime12Hr(m.scheduleOut)),
                    new("Status",           m => m.attendanceStatus),
                    new("Absent Count",     m => m.absentCount,      true),
                    new("No Time Out",      m => m.NoTimeOut,        true),
                    new("AWOL",             m => m.AWOL,             true),
                    new("Absent w/ Leave",  m => m.AbsentWithLeave,  true),
                    new("OB ID",            m => m.obID,             true),
                },

                "AbsentSummary" => new List<ColDef>(common)
                {
                    new("Total Absent",     m => m.absentCount,     true),
                    new("No Time Out",      m => m.NoTimeOut,       true),
                    new("AWOL",             m => m.AWOL,            true),
                    new("Absent w/ Leave",  m => m.AbsentWithLeave, true),
                },

                "TardinessDetail" => new List<ColDef>(common)
                {
                    new("Date",             m => FormatDateOnly(m.dateAbsent)),
                    new("Schedule In",      m => FormatTime12Hr(m.scheduleIn)),
                    new("Schedule Out",     m => FormatTime12Hr(m.scheduleOut)),
                    new("Time In",          m => FormatTime12Hr(m.timeIn)),
                    new("Time Out",         m => FormatTime12Hr(m.timeOut)),
                    new("Status",           m => m.attendanceStatus),
                    new("Late (mins)",      m => m.renderLate,         true),
                    new("Undertime (mins)", m => m.renderUndertime,    true),
                    new("Late Flag",        m => m.lateFrequency,      true),
                    new("UT Flag",          m => m.undertimeFrequency, true),
                },

                "TardinessSummary" => new List<ColDef>(common)
                {
                    new("Total Late (mins)",      m => m.totalLate,          true),
                    new("Total Undertime (mins)", m => m.totalUndertime,     true),
                    new("Late Frequency",         m => m.lateFrequency,      true),
                    new("Undertime Frequency",    m => m.undertimeFrequency, true),
                },

                _ => common
            };
        }
    }
}