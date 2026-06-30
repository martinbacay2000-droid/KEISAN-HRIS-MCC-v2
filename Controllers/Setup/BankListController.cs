using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SbankM")]
    public class BankListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public BankListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/BankList.cshtml");
        }

        [HttpGet]
        public JsonResult GetBankList()
        {
            string sql = @"SELECT id, bankCode, bankName 
                          FROM s_bank 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var banks = _db.Query<BankListModel>(sql).ToList();
            return Json(new { data = banks });
        }

        [HttpGet]
        public JsonResult GetBank(int id)
        {
            string sql = @"SELECT id, bankCode, bankName 
                          FROM s_bank 
                          WHERE id = @Id AND isActive = 1";
            var bank = _db.QueryFirstOrDefault<BankListModel>(sql, new { Id = id });
            return Json(bank);
        }

        [HttpPost]
        public JsonResult AddBank(BankListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_bank 
                                   WHERE bankCode = @bankCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { bankCode = model.bankCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Bank code already exists!" });

                string sql = @"INSERT INTO s_bank (bankCode, bankName, isActive, dtAdded, addedByUser) 
                              VALUES (@bankCode, @bankName, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    bankCode = model.bankCode,
                    bankName = model.bankName,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_bank", newId, "CREATED",
                    $"Added bank: {model.bankCode} - {model.bankName}");

                return Json(new { success = true, message = "Bank added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding bank: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateBank(BankListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_bank 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Bank record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_bank 
                                            WHERE bankCode = @bankCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    bankCode = model.bankCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Bank code already exists!" });

                string sql = @"UPDATE s_bank 
                              SET bankCode = @bankCode, 
                                  bankName = @bankName, 
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    bankCode = model.bankCode,
                    bankName = model.bankName,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_bank", model.id, "UPDATED",
                    $"Updated bank: {model.bankCode} - {model.bankName}");

                return Json(new { success = true, message = "Bank updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating bank: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteBank(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_bank 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_bank", id, "DELETED",
                    $"Bank soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Bank deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedBankList()
        {
            string sql = @"SELECT id, bankCode, bankName 
                          FROM s_bank 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var banks = _db.Query<BankListModel>(sql).ToList();
            return Json(new { data = banks });
        }

        [HttpPost]
        public JsonResult RestoreBank(int id)
        {
            try
            {
                string sql = @"UPDATE s_bank 
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

                _auditTrail.Log("s_bank", id, "RESTORED", "Bank restored");

                return Json(new { success = true, message = "Bank restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}