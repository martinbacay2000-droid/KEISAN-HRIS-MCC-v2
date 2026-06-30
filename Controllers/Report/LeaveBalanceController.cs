using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTleaveBalanceM")]
    public class LeaveBalanceController : Controller
    {
        private readonly IDbConnection _db;

        public LeaveBalanceController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/LeaveBalance.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string branch, string department)
        {

            string query = @"
                SELECT
                    br.branchName,
                    dep.departmentName,
	                m.employeeNo, 
	                CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName,1),''), CASE WHEN IFNULL(b.middleName,'')<>'' THEN '.' ELSE '' END) AS fullName,
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

                -- GROUP BY m.employeeNo
                -- ORDER BY branchName, departmentName, lastname, firstName;

                GROUP BY 
                    m.employeeNo,
                    br.branchName,
                    dep.departmentName,
                    b.lastName,
                    b.firstName,
                    b.middleName
            ";



            var p = new DynamicParameters();
            p.Add("@brcode", branch);
            p.Add("@department", department);

            //var contriReport = _db.Query<AlphalistModel>(query.ToString(), p).ToList();

            var list = _db.Query<LeaveBalanceModel>(query, p).ToList();

            return Json(new { data = list });
        }

    }
}