using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTleaveRequestSummaryM")]
    public class LeaveSummaryController : Controller
    {
        private readonly IDbConnection _db;

        public LeaveSummaryController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/LeaveSummary.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string branch, string department,
                           string leavecode, string dateFrom, string dateTo)
        {
            string query = @"
                SELECT
                    CONCAT('row_', r.id) AS DT_RowId,
                    br.branchName,
                    COALESCE(dep.departmentName, '') AS departmentName,
                    r.employeeNo, 
                    CONCAT(b.lastName, ', ', b.firstName, ' ', 
                           IFNULL(LEFT(b.middleName,1),''), 
                           CASE WHEN IFNULL(b.middleName,'')<>'' THEN '.' ELSE '' END) AS fullName,
                    sl.leaveName,
                    DATE_FORMAT(r.leaveDateFrom,'%m/%d/%Y') AS displayDateFrom,
                    DATE_FORMAT(r.leaveDateTo,'%m/%d/%Y') AS displayDateTo,
                    r.leaveCountDays,
                    COALESCE(r.leaveReason, '') AS leaveReason,		
                    DATE_FORMAT(r.dtAdded,'%m/%d/%Y') AS dateRequested,	
                    DATE_FORMAT(r.dtStatus,'%m/%d/%Y') AS dateApproved,
                    CONCAT(a.lastName, ', ', a.firstName, ' ', 
                           IFNULL(LEFT(a.middleName,1),''), 
                           CASE WHEN IFNULL(a.middleName,'')<>'' THEN '.' ELSE '' END) AS requestedByUser
                FROM rq_leave r
                LEFT JOIN s_leave sl ON sl.leaveCode = r.leaveCode
                LEFT JOIN e_basicinfo b ON b.employeeNo = r.employeeNo
                LEFT JOIN s_department dep ON dep.departmentCode = b.departmentCode
                LEFT JOIN s_branch br ON br.branchCode = b.branchCode
                LEFT JOIN e_basicinfo a ON a.employeeNo = r.requestedByUser
                WHERE r.isActive = 1
                AND (
                    (b.branchCode = 'REGULAR' AND r.statusLevel4 = 'Processed')
                    OR
                    (b.branchCode = 'CASUAL' AND r.statusLevel4 IN ('Approved', 'Processed'))
                    OR
                    (b.branchCode NOT IN ('REGULAR', 'CASUAL') AND r.statusLevel4 IN ('Approved', 'Processed'))
                )
                AND r.leaveCode != 'CTO'
                    AND (@brcode IS NULL OR @brcode = '' OR @brcode = 'ALL' 
                         OR b.branchCode = @brcode)
                    AND (@department IS NULL OR @department = '' OR @department = 'ALL' 
                         OR b.departmentCode = @department)
                    AND (@leavecode IS NULL OR @leavecode = '' OR @leavecode = 'ALL' 
                         OR r.leaveCode = @leavecode)
                    AND (@dateFrom IS NULL OR @dateFrom = '' OR 
                         r.leaveDateFrom BETWEEN DATE(@dateFrom) AND DATE(@dateTo))
                ORDER BY br.branchName, dep.departmentName, b.lastName, b.firstName, 
                         r.leaveDateFrom";

            var p = new DynamicParameters();
            p.Add("@brcode", string.IsNullOrWhiteSpace(branch) ? null : branch);
            p.Add("@department", string.IsNullOrWhiteSpace(department) ? null : department);
            p.Add("@leavecode", string.IsNullOrWhiteSpace(leavecode) ? null : leavecode);
            p.Add("@dateFrom", string.IsNullOrWhiteSpace(dateFrom) ? null : dateFrom);
            p.Add("@dateTo", string.IsNullOrWhiteSpace(dateTo) ? null : dateTo);

            var list = _db.Query<dynamic>(query, p).ToList();
            return Json(new { data = list });
        }

    }
}