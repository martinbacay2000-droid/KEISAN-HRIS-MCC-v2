using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTOvertimeRequestSummaryM")]
    public class OvertimeSummaryController : Controller
    {
        private readonly IDbConnection _db;

        public OvertimeSummaryController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/OvertimeSummary.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string branch, string department, string dateMonth, string dateYear, string cutoff)
        {

            string query = @"
                SELECT
	                r.statusName,
                    br.branchName,
                    dep.departmentName,
                    r.employeeNo, 

                    MIN(DATE_FORMAT(r.dateFrom,'%Y-%m-%d')) AS cutoffStart,
                    MAX(DATE_FORMAT(r.dateTo,'%Y-%m-%d')) AS cutoffEnd,

                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName,1),''), CASE WHEN IFNULL(b.middleName,'')<>'' THEN '.' ELSE '' END) AS fullName,
   
	                SUM(renderOT)            AS renderOT,
                    SUM(renderNSD)           AS renderNSD,
                    SUM(renderNSDOT)         AS renderNSDOT,

                    SUM(renderREST)          AS renderREST,
                    SUM(renderRESTOT)        AS renderRESTOT,
                    SUM(renderNSDREST)       AS renderNSDREST,
                    SUM(renderNSDRESTOT)     AS renderNSDRESTOT,

                    SUM(renderL)             AS renderL,
                    SUM(renderOTL)           AS renderOTL,
                    SUM(renderNSDL)          AS renderNSDL,
                    SUM(renderNSDOTL)        AS renderNSDOTL,

                    SUM(renderRESTL)         AS renderRESTL,
                    SUM(renderRESTOTL)       AS renderRESTOTL,
                    SUM(renderNSDRESTL)      AS renderNSDRESTL,
                    SUM(renderNSDRESTOTL)    AS renderNSDRESTOTL,

                    SUM(renderS)             AS renderS,
                    SUM(renderOTS)           AS renderOTS,
                    SUM(renderNSDS)          AS renderNSDS,
                    SUM(renderNSDOTS)        AS renderNSDOTS,

                    SUM(renderRESTS)         AS renderRESTS,
                    SUM(renderRESTOTS)       AS renderRESTOTS,
                    SUM(renderNSDRESTS)      AS renderNSDRESTS,
                    SUM(renderNSDRESTOTS)    AS renderNSDRESTOTS

                FROM p_biometricsline r
                LEFT JOIN e_basicinfo b ON b.employeeNo = r.employeeNo
                LEFT JOIN s_department dep ON dep.departmentCode = b.departmentCode
                LEFT JOIN s_branch br ON br.branchCode = b.branchCode

                WHERE b.isActive = 1
                AND (@brcode = '' OR @brcode = 'ALL' OR b.branchCode = @brcode)
                AND (@department = '' OR @department = 'ALL' OR b.departmentCode = @department)
                AND (@dateMonth = '' OR @dateMonth = 'ALL' OR r.dateMonth = @dateMonth)
                AND (@dateYear = '' OR r.dateYear = @dateYear)
                AND (r.renderOT > 0 OR r.renderRESTOT > 0 OR r.renderOTS > 0 OR r.renderOTL > 0)

                -- GROUP BY r.employeeNo

                -- ORDER BY 2, 3, 5, r.`date`

                GROUP BY
                    r.employeeNo,
                    r.statusName,
                    br.branchName,
                    dep.departmentName,
                    b.lastName,
                    b.firstName,
                    b.middleName

                ORDER BY br.branchName, dep.departmentName, cutoffStart, r.employeeNo

            ";

            var p = new DynamicParameters();
            p.Add("@brcode", branch);
            p.Add("@department", department);
            p.Add("@dateMonth", dateMonth);
            p.Add("@dateYear", dateYear);
            p.Add("@cutoff", cutoff);

            //var contriReport = _db.Query<AlphalistModel>(query.ToString(), p).ToList();

            var list = _db.Query<OvertimeRequestModel>(query, p).ToList();

            return Json(new { data = list });
        }

        [HttpGet]
        public JsonResult GetOTRequest(string employeeno, string datefrom, string dateto)
        {
            string query = @"
                SELECT 
                    rq.id,
                    rq.employeeNo AS employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                    CONCAT(DATE_FORMAT(rq.overtimeDateIn, '%m/%d/%Y'), ' ', TIME_FORMAT(overTimeIn, '%h:%i %p')) AS requestIn,
                    CONCAT(DATE_FORMAT(rq.OvertimeDateOut, '%m/%d/%Y'), ' ', TIME_FORMAT(overTimeOut, '%h:%i %p')) AS requestOut,
                    rq.overtimeReason AS reason,
                    rq.dtAdded AS dateRequested,
                    rq.statusname AS statusName

                FROM rq_overtime rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo

                WHERE rq.isActive = 1 
                AND rq.statusName IN ('Approved', 'Processed')
                AND rq.employeeNo = @employeeno
                AND rq.overtimeDateIn BETWEEN @datefrom AND @dateto ";

            var p = new DynamicParameters();
            p.Add("@employeeno", employeeno);
            p.Add("@datefrom", datefrom);
            p.Add("@dateto", dateto);

            var list = _db.Query<OvertimeRequestModel>(query, p).ToList();

            return Json(new { data = list });
        }


    }
}