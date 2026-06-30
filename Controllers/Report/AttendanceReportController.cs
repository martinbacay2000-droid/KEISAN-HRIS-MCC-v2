using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTAttendanceReportM")]
    public class AttendanceReportController : BaseController
    {
        private readonly IDbConnection _db;

        public AttendanceReportController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/AttendanceReport.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string status, string branch, string department, string dateMonth, string dateYear)
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
                                SUM(CASE WHEN p.attendanceStatus = 'ABSENT' AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS AWOL,
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
                                SUM(CASE WHEN p.attendanceStatus = 'ABSENT' AND IFNULL(ob.id, 0) = 0 THEN 1 ELSE 0 END) AS AWOL,
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
                    return new JsonResult(new { data = new List<dynamic>() });
            }

            var p = new DynamicParameters();
            p.Add("@brcode", string.IsNullOrWhiteSpace(branch) ? "ALL" : branch);
            p.Add("@department", string.IsNullOrWhiteSpace(department) ? "ALL" : department);
            p.Add("@dtMonth", dtMonth);
            p.Add("@dtYear", dateYear);

            var result = _db.Query<AttendanceReportModel>(query, p).ToList();
            return Json(new { data = result });
        }
    }
}