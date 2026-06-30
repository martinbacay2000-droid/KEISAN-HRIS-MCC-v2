using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SotherdeductionM")]
    public class OtherDeductionListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public OtherDeductionListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/OtherDeductionList.cshtml");
        }

        [HttpGet]
        public JsonResult GetOtherDeductionList()
        {
            string sql = @"SELECT id, otherDeductionCode, otherDeductionName, isTaxable 
                          FROM s_otherdeduction 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var otherDeductions = _db.Query<OtherDeductionListModel>(sql).ToList();
            return Json(new { data = otherDeductions });
        }

        [HttpGet]
        public JsonResult GetOtherDeduction(int id)
        {
            string sql = @"SELECT id, otherDeductionCode, otherDeductionName, isTaxable 
                          FROM s_otherdeduction 
                          WHERE id = @Id AND isActive = 1";
            var otherDeduction = _db.QueryFirstOrDefault<OtherDeductionListModel>(sql, new { Id = id });
            return Json(otherDeduction);
        }

        [HttpPost]
        public JsonResult AddOtherDeduction(OtherDeductionListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_otherdeduction 
                                   WHERE otherDeductionCode = @otherDeductionCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { otherDeductionCode = model.otherDeductionCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Other Deduction code already exists!" });

                string sql = @"INSERT INTO s_otherdeduction (otherDeductionCode, otherDeductionName, isActive, isTaxable, dtAdded, addedByUser) 
                              VALUES (@otherDeductionCode, @otherDeductionName, 1, @isTaxable, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    otherDeductionCode = model.otherDeductionCode,
                    otherDeductionName = model.otherDeductionName,
                    isTaxable = model.isTaxable,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_otherdeduction", newId, "CREATED",
                    $"Added other deduction: {model.otherDeductionCode} - {model.otherDeductionName}");

                return Json(new { success = true, message = "Other Deduction added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding other deduction: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateOtherDeduction(OtherDeductionListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_otherdeduction 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Other Deduction record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_otherdeduction 
                                            WHERE otherDeductionCode = @otherDeductionCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    otherDeductionCode = model.otherDeductionCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Other Deduction code already exists!" });

                string sql = @"UPDATE s_otherdeduction 
                              SET otherDeductionCode = @otherDeductionCode, 
                                  otherDeductionName = @otherDeductionName, 
                                  isTaxable = @isTaxable,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    otherDeductionCode = model.otherDeductionCode,
                    otherDeductionName = model.otherDeductionName,
                    isTaxable = model.isTaxable,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_otherdeduction", model.id, "UPDATED",
                    $"Updated other deduction: {model.otherDeductionCode} - {model.otherDeductionName}");

                return Json(new { success = true, message = "Other Deduction updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating other deduction: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteOtherDeduction(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_otherdeduction 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_otherdeduction", id, "DELETED",
                    $"Other deduction soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Other Deduction deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedOtherDeductionList()
        {
            string sql = @"SELECT id, otherDeductionCode, otherDeductionName, isTaxable 
                          FROM s_otherdeduction 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var otherDeductions = _db.Query<OtherDeductionListModel>(sql).ToList();
            return Json(new { data = otherDeductions });
        }

        [HttpPost]
        public JsonResult RestoreOtherDeduction(int id)
        {
            try
            {
                string sql = @"UPDATE s_otherdeduction 
                              SET dtDeleted = NULL, 
                                  isActive = 1,
                                  deletedByUser = NULL,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_otherdeduction", id, "RESTORED", "Other deduction restored");

                return Json(new { success = true, message = "Other Deduction restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}