using Dapper;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTprobationarySchedM")]
    public class DueForProbationary : Controller
    {
        private readonly IDbConnection _db;

        public DueForProbationary(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/DueForProbationary.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList()
        {

            string query = @"
         SELECT
    DATE_FORMAT(eb.dateOfProApp, '%m/%d/%Y') AS probationaryDate,
    UPPER(CONCAT(eb.lastName, ' ', eb.firstName)) AS employeeName,
    CASE
        WHEN eb.dateOfProApp IS NULL THEN NULL
        WHEN CURDATE() > eb.dateOfProApp THEN
            'FOR PROBATIONARY'
        WHEN CURDATE() = eb.dateOfProApp THEN
            'TODAY'
        ELSE
            CONCAT(DATEDIFF(eb.dateOfProApp, CURDATE()), ' DAYS REMAINING')
    END AS status
FROM e_basicinfo eb
WHERE eb.isActive = 1
AND employmentStatus != 'PROBATIONARY'
AND employmentStatus != 'REGULAR'
ORDER BY eb.dateOfProApp;
                            ";

            var contriReport = _db.Query<Models.Report.DueForProbationary>(query.ToString()).ToList();
            return Json(new { data = contriReport });
        }

    }
}