using Dapper;
using KEISAN_HRIS_v2.Models.Payroll;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class ProcessPayrollController : Controller
    {
        private readonly IDbConnection _db;

        public ProcessPayrollController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payroll/ProcessPayroll.cshtml");

        }


        //private IDbConnection GetConnection()
        //{
        //    return _db;
        //}
        [HttpGet]
        public JsonResult GetEmployeeListPayroll()
        {

            string sql = @"SELECT b.employeeNo, CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS employeeName 
                        FROM e_basicinfo b WHERE b.isActive = 1 ";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        // CRUD Operations for Overtime Request List
        [HttpGet]
        public JsonResult GetOvertimeRequestList(string status, string branch, string department, string dateFrom, string dateTo)
        {
            var sb = new StringBuilder(@"
                SELECT 
                    rq.id,
                    rq.employeeNo AS employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                    TIMESTAMP(rq.overTimeDateIN, rq.overTimeIN) AS requestIn,
                    TIMESTAMP(rq.overTimeDateOUT, rq.overTimeOUT) AS requestOut,
                    rq.overTimeReason AS reason,
                    rq.dtAdded AS dateRequested,
                    rq.statusLevel4 AS statusName
                FROM rq_overtime rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo

                WHERE rq.isActive = 1

            ");

            var p = new DynamicParameters();

            if (status != "All")
            {
                sb.Append(" AND rq.statusLevel4 = @status ");
                p.Add("@status", status);
            }

            if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND b.branchCode = @branch ");
                p.Add("@branch", branch);
            }

            if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND b.departmentCode = @department ");
                p.Add("@department", department);
            }

            //if (!string.IsNullOrWhiteSpace(dateFrom))
            //{
            //    sb.Append(" AND rq.overTimeDateIN BETWEEN DATE(@dateFrom) AND DATE(@dateTo) ");
            //    p.Add("@dateFrom", dateFrom); p.Add("@dateTo", dateTo);
            //}

            sb.Append(" ORDER BY rq.dtAdded ; ");

            var requests = _db.Query<ProcessPayrollModel>(sb.ToString(), p).ToList();
            return Json(new { data = requests });
        }

    }
}