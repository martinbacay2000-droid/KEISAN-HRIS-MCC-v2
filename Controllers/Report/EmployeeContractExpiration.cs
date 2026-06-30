using Dapper;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTemployeeContractExpirationM ")]
    public class EmployeeContractExpiration : Controller
    {
        private readonly IDbConnection _db;

        public EmployeeContractExpiration(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/EmployeeContractExpiration.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList()
        {

            string query = @"
        SELECT
    DATE_FORMAT(eb.dtContractStart, '%m/%d/%Y') AS contractStart,
    UPPER(CONCAT(eb.lastName, ' ', eb.firstName)) AS employeeName,
    CASE
        WHEN eb.dtContractEnd IS NULL THEN NULL
        WHEN CURDATE() > eb.dtContractEnd THEN
            'CONTRACT ENDED'
        WHEN CURDATE() = eb.dtContractEnd THEN
            'ENDS TODAY'
        ELSE
            CONCAT(DATEDIFF(eb.dtContractEnd, CURDATE()), ' DAYS REMAINING')
    END AS status
FROM e_basicinfo eb
WHERE eb.isActive = 1
ORDER BY eb.dtContractEnd;
                            ";

            var contriReport = _db.Query<Models.Report.EmployeeContractExpiration>(query.ToString()).ToList();
            return Json(new { data = contriReport });
        }

    }
}