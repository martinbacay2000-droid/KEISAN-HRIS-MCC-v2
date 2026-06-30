using Dapper;
using KEISAN_HRIS_v2.Models.AuditTrail;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.AuditTrail
{
    [ModuleAuthorize("Aaudittrail")]
    public class AuditTrailController : Controller
    {
        private readonly IDbConnection _db;

        public AuditTrailController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/AuditTrail/AuditTrail.cshtml");
        }

        [HttpGet]
        public JsonResult GetAuditTrailList(string dateFrom, string dateTo)
        {
            try
            {
                string sql = @"SELECT id, tableName, referenceID, action, remarks, 
                              usercode, dtAdded 
                              FROM audit_trail 
                              WHERE 1=1";

                var parameters = new DynamicParameters();

                // Apply date range filter only if both dates are provided
                if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
                {
                    sql += " AND DATE(dtAdded) BETWEEN STR_TO_DATE(@dateFrom, '%m/%d/%Y') AND STR_TO_DATE(@dateTo, '%m/%d/%Y')";
                    parameters.Add("@dateFrom", dateFrom);
                    parameters.Add("@dateTo", dateTo);
                }

                sql += " ORDER BY dtAdded DESC";

                var auditLogs = _db.Query<AuditTrailModel>(sql, parameters).ToList();
                Console.WriteLine($"Audit Trail Query - Record Count: {auditLogs.Count}");
                return Json(new { data = auditLogs });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAuditTrailList: {ex.Message}");
                return Json(new { data = new List<AuditTrailModel>(), error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetAuditByReference(string tableName, int referenceId)
        {
            try
            {
                string sql = @"SELECT id, tableName, referenceID, action, remarks, 
                              usercode, dtAdded 
                              FROM audit_trail 
                              WHERE tableName = @tableName 
                              AND referenceID = @referenceId
                              ORDER BY dtAdded DESC";

                var auditLogs = _db.Query<AuditTrailModel>(sql, new { tableName, referenceId }).ToList();
                return Json(new { data = auditLogs });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAuditByReference: {ex.Message}");
                return Json(new { data = new List<AuditTrailModel>(), error = ex.Message });
            }
        }
    }
}