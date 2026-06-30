using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTloanM")]
    public class LoanBalanceController : Controller
    {
        private readonly IDbConnection _db;

        public LoanBalanceController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/LoanBalance.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string branch, string department, string loancode, string loanStatus = "Ongoing")
        {
            string query = @"
                SELECT
                    br.branchName,
                    dep.departmentName,
                    el.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName,1),''), CASE WHEN IFNULL(b.middleName,'')<>'' THEN '.' ELSE '' END) AS fullName,
                    sl.loanName,
                    DATE_FORMAT(el.dateGranted,'%m/%d/%Y') dateGranted,
                    DATE_FORMAT(el.deductionStartDate,'%m/%d/%Y') deductionStartDate,
                    el.deductionSchedule,
                    CAST(el.principalAmount AS DECIMAL(10,2)) AS principalAmount,
                    CAST(el.amortizationAmount AS DECIMAL(10,2)) AS amortizationAmount,
                    CAST(ROUND(
                        el.principalAmount - IFNULL((SELECT SUM(IFNULL(m.credit,0))
                                FROM m_loan m
                                WHERE m.e_loanID = el.id
                                AND m.isActive = 1
                                AND m.statusName = 'Added'), 0)
                    , 2) AS DECIMAL(10,2)) AS loanBalance,
                    el.statusName AS loanStatus
                FROM e_loan el
                LEFT JOIN s_loan sl ON sl.loanCode = el.loanCode
                LEFT JOIN e_basicinfo b ON b.employeeNo = el.employeeNo
                LEFT JOIN s_department dep ON dep.departmentCode = b.departmentCode
                LEFT JOIN s_branch br ON br.branchCode = b.branchCode
                WHERE b.isActive = 1
                AND el.isActive = 1
                AND (@brcode = '' OR @brcode = 'ALL' OR b.branchCode = @brcode)
                AND (@department = '' OR @department = 'ALL' OR b.departmentCode = @department)
                AND (@loancode = '' OR @loancode = 'ALL' OR el.loanCode = @loancode)
                AND (@loanStatus = 'ALL' OR el.statusName = @loanStatus)
                ORDER BY 1,2,3
            ";

            var p = new DynamicParameters();
            p.Add("@brcode", branch);
            p.Add("@department", department);
            p.Add("@loancode", loancode);
            p.Add("@loanStatus", loanStatus);

            // Updated to use UsersLoansModel instead of userLoans
            var list = _db.Query<UsersLoansModel>(query, p).ToList();

            return Json(new { data = list });
        }
    }
}