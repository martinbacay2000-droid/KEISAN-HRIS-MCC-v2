using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTbirthdayM")]
    public class BirthdayController : Controller
    {
        private readonly IDbConnection _db;

        public BirthdayController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/Birthday.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string dateMonth)
        {

            string query = @"
          SELECT 
				 ep.employeeNo,
    DATE_FORMAT(ep.dateOfBirth, '%m/%d/%Y') AS birthDay,
    UPPER(CONCAT(eb.lastName, ' ', eb.firstName)) AS employeeName,
		YEAR(NOW())- YEAR(ep.dateOfBirth)  AS AGE,
    	CASE 
            WHEN CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth, '%m-%d')) AS DATE) = CAST(CONCAT(YEAR(NOW()),'-',MONTH(Now()),'-',DAY(Now())) AS DATE) THEN 'TODAY''S BIRTHDAY CELEBRANT' 
            WHEN DATEDIFF(CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth, '%m-%d')) AS DATE), Now()) = 1 THEN '1 DAY BEFORE BIRTHDAY'

			ELSE
				CASE WHEN DATEDIFF(CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth, '%m-%d')) AS DATE), Now()) < 0 THEN ''
				 WHEN DATEDIFF(CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth, '%m-%d')) AS DATE), Now()) < 0 THEN ''
				ELSE CONCAT(DATEDIFF(CAST(CONCAT(YEAR(NOW()),'-',DATE_FORMAT(ep.dateOfBirth, '%m-%d')) AS DATE), Now()), ' DAYS BEFORE BIRTHDAY') END
			END as status
FROM e_personalInfo ep
LEFT JOIN e_basicinfo eb ON ep.employeeNo = eb.employeeNo
WHERE eb.isActive = 1
AND (@dateMonth = 'ALL' OR MONTHNAME(ep.dateOfBirth) = @dateMonth)
ORDER BY ep.dateOfBirth;



                            ";

            var p = new DynamicParameters();
            p.Add("@dateMonth", dateMonth);

            var contriReport = _db.Query<EmployeeMasterListModel>(query.ToString(), p).ToList();
            return Json(new { data = contriReport });
        }

    }
}