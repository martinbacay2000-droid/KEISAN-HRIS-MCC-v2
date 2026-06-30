using Dapper;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTlicenseM")]
    public class LicenseExpiration : Controller
    {
        private readonly IDbConnection _db;

        public LicenseExpiration(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/LicenseExpiration.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList()
        {

            string query = @"
        SELECT
    UPPER(CONCAT(eb.lastName, ' ', eb.firstName)) AS employeeName,
    elc.licenseAndCertificateDescription,
    DATE_FORMAT(elc.validUntil, '%m/%d/%Y') AS validUntil,
    CASE
        WHEN elc.validUntil IS NULL THEN NULL
        WHEN CURDATE() > elc.validUntil THEN
            'EXPIRED'
        WHEN CURDATE() = elc.validUntil THEN
            'EXPIRES TODAY'
        ELSE
            CONCAT(DATEDIFF(elc.validUntil, CURDATE()), ' DAYS REMAINING')
    END AS status
FROM e_licenseandcertificate elc
INNER JOIN e_basicinfo eb
    ON elc.employeeNo = eb.employeeNo
WHERE eb.isActive = 1
ORDER BY elc.validUntil;

                            ";

            var contriReport = _db.Query<Models.Report.LicenseExpiration>(query.ToString()).ToList();
            return Json(new { data = contriReport });
        }

    }
}