using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Setup
{
    [ModuleAuthorize("SallowanceM")]
    public class AllowanceListController : BaseController // ← changed from Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public AllowanceListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Setup/AllowanceList.cshtml");
        }

        [HttpGet]
        public JsonResult GetAllowanceList()
        {
            string sql = @"SELECT id, allowanceCode, allowanceName, isTaxable, amount, basis, basisDate, employmentStatus, positionCode
                          FROM s_allowance 
                          WHERE dtDeleted IS NULL 
                          ORDER BY id DESC";
            var allowances = _db.Query<AllowanceListModel>(sql).ToList();
            return Json(new { data = allowances });
        }

        [HttpGet]
        public JsonResult GetAllowance(int id)
        {
            string sql = @"SELECT id, allowanceCode, allowanceName, isTaxable, amount, basis, basisDate, employmentStatus, positionCode
                          FROM s_allowance 
                          WHERE id = @Id AND isActive = 1";
            var allowance = _db.QueryFirstOrDefault<AllowanceListModel>(sql, new { Id = id });
            return Json(allowance);
        }

        [HttpPost]
        public JsonResult AddAllowance(AllowanceListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_allowance 
                                   WHERE allowanceCode = @allowanceCode 
                                   AND dtDeleted IS NULL";
                int existingCount = _db.QuerySingle<int>(checkSql, new { allowanceCode = model.allowanceCode });

                if (existingCount > 0)
                    return Json(new { success = false, message = "Allowance code already exists!" });

                if (!ValidateBasisDate(model.basis, model.basisDate))
                    return Json(new { success = false, message = "Invalid basis date for the selected basis!" });

                string sql = @"INSERT INTO s_allowance (allowanceCode, allowanceName, isActive, isTaxable, amount, basis, basisDate, employmentStatus, positionCode, dtAdded, addedByUser) 
                              VALUES (@allowanceCode, @allowanceName, 1, @isTaxable, @amount, @basis, @basisDate, @employmentStatus, @positionCode, NOW(), @addedByUser);
                              SELECT LAST_INSERT_ID();";

                int newId = _db.QuerySingle<int>(sql, new
                {
                    allowanceCode = model.allowanceCode,
                    allowanceName = model.allowanceName,
                    isTaxable = model.isTaxable,
                    amount = model.amount,
                    basis = model.basis,
                    basisDate = model.basisDate,
                    employmentStatus = model.employmentStatus,
                    positionCode = model.positionCode,
                    addedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_allowance", newId, "CREATED",
                    $"Added allowance: {model.allowanceCode} - {model.allowanceName}");

                return Json(new { success = true, message = "Allowance added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error adding allowance: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateAllowance(AllowanceListModel model)
        {
            try
            {
                string checkSql = @"SELECT COUNT(*) FROM s_allowance 
                                   WHERE id = @id AND dtDeleted IS NULL";
                int recordExists = _db.QuerySingle<int>(checkSql, new { id = model.id });

                if (recordExists == 0)
                    return Json(new { success = false, message = "Allowance record not found or has been deleted!" });

                string duplicateCheckSql = @"SELECT COUNT(*) FROM s_allowance 
                                            WHERE allowanceCode = @allowanceCode 
                                            AND id != @id 
                                            AND dtDeleted IS NULL";
                int duplicateCount = _db.QuerySingle<int>(duplicateCheckSql, new
                {
                    allowanceCode = model.allowanceCode,
                    id = model.id
                });

                if (duplicateCount > 0)
                    return Json(new { success = false, message = "Allowance code already exists!" });

                if (!ValidateBasisDate(model.basis, model.basisDate))
                    return Json(new { success = false, message = "Invalid basis date for the selected basis!" });

                string sql = @"UPDATE s_allowance 
                              SET allowanceCode = @allowanceCode, 
                                  allowanceName = @allowanceName, 
                                  isTaxable = @isTaxable,
                                  amount = @amount,
                                  basis = @basis,
                                  basisDate = @basisDate,
                                  employmentStatus = @employmentStatus,
                                  positionCode = @positionCode,
                                  dtLastModified = NOW(),
                                  lastModifiedByUser = @lastModifiedByUser
                              WHERE id = @id";

                _db.Execute(sql, new
                {
                    id = model.id,
                    allowanceCode = model.allowanceCode,
                    allowanceName = model.allowanceName,
                    isTaxable = model.isTaxable,
                    amount = model.amount,
                    basis = model.basis,
                    basisDate = model.basisDate,
                    employmentStatus = model.employmentStatus,
                    positionCode = model.positionCode,
                    lastModifiedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_allowance", model.id, "UPDATED",
                    $"Updated allowance: {model.allowanceCode} - {model.allowanceName}");

                return Json(new { success = true, message = "Allowance updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating allowance: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteAllowance(int id, string reason = "")
        {
            try
            {
                string sql = @"UPDATE s_allowance 
                              SET dtDeleted = NOW(), 
                                  isActive = 0,
                                  deletedByUser = @deletedByUser
                              WHERE id = @Id";

                _db.Execute(sql, new
                {
                    Id = id,
                    deletedByUser = EmployeeNo // ← from BaseController
                });

                _auditTrail.Log("s_allowance", id, "DELETED",
                    $"Allowance soft deleted{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Allowance deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedAllowanceList()
        {
            string sql = @"SELECT id, allowanceCode, allowanceName, isTaxable, amount, basis, basisDate, employmentStatus, positionCode
                          FROM s_allowance 
                          WHERE dtDeleted IS NOT NULL 
                          ORDER BY id DESC";
            var allowances = _db.Query<AllowanceListModel>(sql).ToList();
            return Json(new { data = allowances });
        }

        [HttpPost]
        public JsonResult RestoreAllowance(int id)
        {
            try
            {
                string sql = @"UPDATE s_allowance 
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

                _auditTrail.Log("s_allowance", id, "RESTORED", "Allowance restored");

                return Json(new { success = true, message = "Allowance restored successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private bool ValidateBasisDate(string basis, string basisDate)
        {
            if (string.IsNullOrEmpty(basis) || string.IsNullOrEmpty(basisDate))
                return false;

            switch (basis.ToLower())
            {
                case "daily":
                    return basisDate.ToLower() == "daily";
                case "semi-monthly":
                    return basisDate == "1st And 2nd Cut-off";
                case "monthly":
                    return basisDate == "1st cut-off" || basisDate == "2nd cut-off";
                case "yearly":
                    return DateTime.TryParse(basisDate, out _);
                default:
                    return false;
            }
        }
    }
}