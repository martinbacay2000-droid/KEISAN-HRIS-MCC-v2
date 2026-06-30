using Dapper;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTregularizationSchedM")]
    public class DueForRegularization : Controller
    {
        private readonly IDbConnection _db;

        public DueForRegularization(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/DueForRegularization.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string departmentName)
        {

            string query = @"
         SELECT
        DATE_FORMAT(eb.dateOfRegApp, '%m/%d/%Y') AS dateOfRegularization,
        UPPER(CONCAT(eb.lastName, ' ', eb.firstName)) AS employeeName,
        CASE
            WHEN eb.dateOfRegApp IS NULL THEN NULL
            WHEN CURDATE() > eb.dateOfRegApp THEN
                'FOR REGULARIZATION'
            WHEN CURDATE() = eb.dateOfRegApp THEN
                'TODAY'
            ELSE
                CONCAT(DATEDIFF(eb.dateOfRegApp, CURDATE()), ' DAYS REMAINING')
        END AS status,
        s.departmentName
        FROM e_basicinfo eb
        LEFT JOIN s_department s ON eb.departmentCode = s.departmentCode
        WHERE eb.isActive = 1
        AND employmentStatus = 'PROBATIONARY'
        AND CASE WHEN IFNULL(@departmentName,'') = 'ALL' THEN eb.employeeNo IS NOT NULL ELSE eb.departmentCode = @departmentName END
        ORDER BY eb.dateOfRegApp;

                            ";
            var p = new DynamicParameters();
            p.Add("@departmentName", departmentName);

            var contriReport = _db.Query<Models.Report.DueForRegularization>(query.ToString(), p).ToList();

            return Json(new { data = contriReport });
        }

    }
}