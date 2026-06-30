using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.DTR
{
    [ModuleAuthorize("FSdtrM")]
    public class DTRController : Controller
    {
        private readonly IDbConnection _db;

        public DTRController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult GetDTR(string employeeNo)
        {
            var employeeID = new EmployeeID
            {
                id = employeeNo
            };
            return PartialView("~/Views/Users/Partials/_DTR.cshtml", employeeID);
        }

        [HttpGet]
        public JsonResult GetDTRList(string employeeNo, string dateFrom, string dateTo)
        {
            string sql = @"
                SELECT 
                    id, 
                    DATE_FORMAT(biometricsDate, '%m/%d/%Y') AS biometricsDate, 
                    DATE_FORMAT(biometricsDateOut, '%m/%d/%Y') AS biometricsDateOut, 
                    DATE_FORMAT(biometricsTimeIn, '%h:%i %p') AS biometricsTimeIn, 
                    DATE_FORMAT(biometricsTimeOut, '%h:%i %p') AS biometricsTimeOut,
                    biometricsDeviceLog,
                    (SELECT s.leaveName 
                     FROM s_leave s 
                     LEFT JOIN rq_leave l ON s.leaveCode = l.leaveCode
                     WHERE l.leaveDateFrom = biometricsDate
                       AND l.statusLevel4 = 'Approved'
                       AND l.employeeNo = @Id
                    ) AS remarks
                FROM u_biometrics
                WHERE employeeNo = @Id 
                  AND isActive = 1
                  AND (@DateFrom IS NULL OR biometricsDate >= STR_TO_DATE(@DateFrom, '%m/%d/%Y'))
                  AND (@DateTo IS NULL OR biometricsDateOut <= STR_TO_DATE(@DateTo, '%m/%d/%Y'))
                ORDER BY biometricsDate DESC, biometricsTimeIn DESC";

            var employeeDTR = _db.Query<DTRInfo>(sql, new
            {
                Id = employeeNo,
                DateFrom = string.IsNullOrWhiteSpace(dateFrom) ? null : dateFrom,
                DateTo = string.IsNullOrWhiteSpace(dateTo) ? null : dateTo
            }).ToList();

            return Json(new { data = employeeDTR ?? new List<DTRInfo>() });
        }
    }
}