using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SloanM")]
    public class LoanListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LoanListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/LoanList.cshtml");
        }

        [HttpGet]
        public JsonResult GetLoanList()
        {
            string sql = @"SELECT id, loanCode, loanName, loanType
                          FROM s_loan
                          WHERE dtDeleted IS NULL
                          ORDER BY id DESC";
            var loans = _db.Query<LoanListModel>(sql).ToList();
            return Json(new { data = loans });
        }

        [HttpGet]
        public JsonResult GetLoan(int id)
        {
            string sql = @"SELECT id, loanCode, loanName, loanType
                          FROM s_loan
                          WHERE id = @Id AND isActive = 1";
            var loan = _db.QueryFirstOrDefault<LoanListModel>(sql, new { Id = id });
            return Json(loan);
        }

        [HttpPost]
        public JsonResult AddLoan(LoanListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_loan
                                    WHERE loanCode = @loanCode
                                    AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { loanCode = model.loanCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Loan code already exists!" });

                string sql = @"INSERT INTO s_loan (loanCode, loanName, loanType, isActive, dtAdded, addedByUser)
                              VALUES (@loanCode, @loanName, @loanType, 1, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    loanCode = model.loanCode,
                    loanName = model.loanName,
                    loanType = model.loanType,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_loan", newId, "CREATED",
                    $"Added loan: {model.loanCode} - {model.loanName}");

                return Json(new { success = true, message = "Loan added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding loan: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateLoan(LoanListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_loan
                                    WHERE id = @id AND dtDeleted IS NULL";
                int recordExist = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExist == 0)
                    return Json(new { success = false, message = "Loan record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_loan
                                            WHERE loanCode = @loanCode
                                            AND id != @id
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    loanCode = model.loanCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Loan code already exists!" });

                string sql = @"UPDATE s_loan
                              SET loanCode = @loanCode,
                                  loanName = @loanName,
                                  loanType = @loanType,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    loanCode = model.loanCode,
                    loanName = model.loanName,
                    loanType = model.loanType,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_loan", model.id, "UPDATED",
                    $"Updated loan: {model.loanCode} - {model.loanName}");

                return Json(new { success = true, message = "Loan updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating loan: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteLoan(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_loan
                               SET dtDeleted = NOW(), 
                                   isActive = 0,
                                   deletedByUser = @deletedByUser
                               WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_loan", id, "DELETED",
                    $"Loan soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Loan deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedLoanList()
        {
            string sql = @"SELECT id, loanCode, loanName, loanType
                          FROM s_loan
                          WHERE dtDeleted IS NOT NULL
                          ORDER BY loanCode";
            var loans = _db.Query<LoanListModel>(sql).ToList();
            return Json(new { data = loans });
        }

        [HttpPost]
        public JsonResult RestoreLoan(int id)
        {
            try
            {
                string sql = @"UPDATE s_loan
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

                _auditTrail.Log("s_loan", id, "RESTORED", "Loan restored");

                return Json(new { success = true, message = "Loan restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}