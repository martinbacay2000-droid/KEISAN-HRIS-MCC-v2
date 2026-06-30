using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Globalization;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSAllowancesM")]
    public class AllowancesController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public AllowancesController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        // ── Access helpers ────────────────────────────────────────────────────────
        // Amount visible to READWRITE or FULL
        private bool CanViewAmount
        {
            get
            {
                var accessLevel = AccessHelper.GetAccess(HttpContext, "FSAllowancesM");
                var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo");

                if (accessLevel == "READ")
                {
                    var employeeNoBeingViewed = HttpContext.Request.Query["employeeNo"].ToString()
                        ?? HttpContext.Request.Form["employeeNo"].ToString();
                    return employeeNoBeingViewed == sessionEmployeeNo;
                }

                return accessLevel is "READWRITE" or "FULL";
            }
        }
        // Can edit fields (effectivity date etc.): EDIT, READWRITE, FULL
        private bool CanEditAllowance => AccessHelper.CanEdit(HttpContext, "FSAllowancesM");
        // Can create new records: READWRITE, FULL
        private bool CanSaveAllowance => AccessHelper.CanCreate(HttpContext, "FSAllowancesM");
        // Full destructive actions (delete/restore): FULL only
        private bool CanFullAccess => AccessHelper.CanDelete(HttpContext, "FSAllowancesM");

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetAllowances(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_Allowances.cshtml", new List<userAllowances>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, ''))
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                var allowances = GetAllowanceData(employeeNo, false);

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";
                ViewBag.CanViewAmount = CanViewAmount;
                ViewBag.CanEditAllowance = CanEditAllowance;
                ViewBag.CanSaveAllowance = CanSaveAllowance;
                ViewBag.CanFullAccess = CanFullAccess;

                return PartialView("~/Views/Users/Partials/_Allowances.cshtml", allowances);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAllowances: {ex.Message}");
                ViewBag.CanViewAmount = false;
                ViewBag.CanEditAllowance = false;
                ViewBag.CanSaveAllowance = false;
                ViewBag.CanFullAccess = false;
                return PartialView("~/Views/Users/Partials/_Allowances.cshtml", new List<userAllowances>());
            }
        }

        [HttpGet]
        public JsonResult GetAllowanceList(string employeeNo, string isactive)
        {
            try
            {
                bool? activeFilter = isactive == "2" ? null : isactive == "1";
                var allowances = GetAllowanceData(employeeNo, false, activeFilter);

                // Mask amount if user cannot view it
                if (!CanViewAmount)
                {
                    foreach (var a in allowances)
                        a.allowanceAmount = null; // null signals the view to show ****
                }

                return Json(new { data = allowances });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAllowanceList: {ex.Message}");
                return Json(new { data = new List<userAllowances>() });
            }
        }

        [HttpGet]
        public JsonResult GetAllowanceTypes()
        {
            try
            {
                var sql = @"
                    SELECT allowanceCode, allowanceName, isTaxable, basis, isActive
                    FROM s_allowance
                    WHERE isActive = 1
                    AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                    ORDER BY allowanceName";

                var allowanceTypes = _db.Query<dynamic>(sql).ToList();
                return Json(allowanceTypes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAllowanceTypes: {ex.Message}");
                return Json(new List<dynamic>());
            }
        }

        [HttpGet]
        public JsonResult GetAllowanceById(int id)
        {
            try
            {
                var sql = BuildAllowanceQuery("WHERE ea.id = @Id");
                var allowance = _db.QueryFirstOrDefault<userAllowances>(sql, new { Id = id });

                if (allowance == null)
                    return Json(new { success = false, message = "Allowance not found." });

                // Mask amount if user cannot view it
                if (!CanViewAmount)
                    allowance.allowanceAmount = null;

                return Json(new { success = true, data = allowance });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAllowanceById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving allowance: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedAllowances(string employeeNo)
        {
            try
            {
                var allowances = GetAllowanceData(employeeNo, true);

                if (!CanViewAmount)
                {
                    foreach (var a in allowances)
                        a.allowanceAmount = null;
                }

                return Json(new { data = allowances });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedAllowances: {ex.Message}");
                return Json(new { data = new List<userAllowances>() });
            }
        }

        [HttpPost]
        public JsonResult SaveAllowance([FromBody] AllowanceDto model)
        {
            // Must have at least EDIT access to save
            if (!CanEditAllowance)
                return Json(new { success = false, message = "Unauthorized: You do not have permission to edit allowances." });

            try
            {
                if (!ValidateAllowance(model, out string validationMessage))
                    return Json(new { success = false, message = validationMessage });

                var allowanceType = _db.QueryFirstOrDefault<dynamic>(
                    @"SELECT * FROM s_allowance
                      WHERE allowanceCode = @AllowanceCode
                      AND isActive = 1
                      AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                    new { AllowanceCode = model.AllowanceCode });

                if (allowanceType == null)
                    return Json(new { success = false, message = "Invalid allowance type selected." });

                // Amount validation only for users who can view/edit it
                if (CanViewAmount && model.AllowanceAmount <= 0)
                    return Json(new { success = false, message = "Allowance amount must be greater than 0." });

                if (!ProcessEffectivityDate(model, out DateTime effectivityDate, out string dateError))
                    return Json(new { success = false, message = dateError });

                if (model.Id.HasValue && model.Id > 0)
                    return UpdateAllowance(model, effectivityDate);
                else
                    return InsertAllowance(model, effectivityDate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveAllowance: {ex.Message}");
                return Json(new { success = false, message = "Error saving allowance: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult InactiveAllowance(int id, string remarks = "")
        {
            if (!CanFullAccess)
                return Json(new { success = false, message = "Unauthorized: Only users with Full Access can delete allowances." });

            try
            {
                if (!RecordExists(id))
                    return Json(new { success = false, message = "Allowance record not found or already deleted!" });

                var sql = @"
                    UPDATE e_allowance
                    SET dtDeleted    = NOW(),
                        isActive     = 0,
                        deletedByUser = @DeletedByUser
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id, DeletedByUser = EmployeeNo });

                if (rowsAffected > 0)
                {
                    var auditMessage = string.IsNullOrWhiteSpace(remarks)
                        ? "Allowance soft deleted"
                        : $"Allowance soft deleted. Reason: {remarks}";

                    _auditTrail.Log("e_allowance", id, "DELETED", auditMessage);
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Allowance deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete allowance." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactiveAllowance: {ex.Message}");
                return Json(new { success = false, message = "Error deleting allowance: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreAllowance(int id)
        {
            if (!CanFullAccess)
                return Json(new { success = false, message = "Unauthorized: Only users with Full Access can restore allowances." });

            try
            {
                var existingRecord = _db.QueryFirstOrDefault<userAllowances>(
                    "SELECT * FROM e_allowance WHERE id = @Id AND (dtDeleted IS NOT NULL AND dtDeleted != '0000-00-00 00:00:00')",
                    new { Id = id });

                if (existingRecord == null)
                    return Json(new { success = false, message = "Allowance record not found or not deleted!" });

                var sql = @"
                    UPDATE e_allowance
                    SET dtDeleted      = NULL,
                        deletedByUser  = NULL,
                        isActive       = 1,
                        dtLastModified = NOW()
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new { Id = id });

                if (rowsAffected > 0)
                    _auditTrail.Log("e_allowance", id, "RESTORED", "Allowance restored");

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Allowance restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore allowance." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreAllowance: {ex.Message}");
                return Json(new { success = false, message = "Error restoring allowance: " + ex.Message });
            }
        }

        // ── Helper Methods ────────────────────────────────────────────────────────

        private bool ValidateAllowance(AllowanceDto model, out string message)
        {
            message = string.Empty;

            if (model == null) { message = "Invalid data provided."; return false; }
            if (string.IsNullOrWhiteSpace(model.EmployeeNo)) { message = "Employee number is required."; return false; }
            if (string.IsNullOrWhiteSpace(model.AllowanceCode)) { message = "Allowance type is required."; return false; }
            if (string.IsNullOrWhiteSpace(model.EffectivityDate)) { message = "Effectivity date is required."; return false; }

            // Only validate amount if user can see/edit it
            if (CanViewAmount && model.AllowanceAmount <= 0)
            {
                message = "Allowance amount must be greater than 0.";
                return false;
            }

            return true;
        }

        private bool ProcessEffectivityDate(AllowanceDto model, out DateTime effectivityDate, out string errorMessage)
        {
            effectivityDate = DateTime.MinValue;
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(model.EffectivityDate))
            {
                errorMessage = "Effectivity date is required.";
                return false;
            }

            string[] formats = { "yyyy/MM/dd", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy" };

            if (!DateTime.TryParseExact(model.EffectivityDate, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out effectivityDate))
            {
                errorMessage = "Invalid effectivity date format. Expected format: yyyy/MM/dd";
                return false;
            }

            return true;
        }

        private JsonResult UpdateAllowance(AllowanceDto model, DateTime effectivityDate)
        {
            var existingRecord = _db.QueryFirstOrDefault<userAllowances>(
                "SELECT * FROM e_allowance WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = model.Id });

            if (existingRecord == null)
                return Json(new { success = false, message = "Allowance record not found or has been deleted!" });

            // Build SET clause: only update amount column when user has salary-level access
            string amountClause = CanViewAmount
                ? "allowanceAmount = @AllowanceAmount,"
                : ""; // EDIT-only: skip amount column entirely

            var sql = $@"
                UPDATE e_allowance
                SET allowanceCode    = @AllowanceCode,
                    {amountClause}
                    effectivityDate  = @EffectivityDate,
                    dtLastModified   = NOW(),
                    lastModifiedByUser = @ModifiedByUser
                WHERE id = @Id";

            var rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                AllowanceCode = model.AllowanceCode,
                AllowanceAmount = CanViewAmount ? model.AllowanceAmount : 0,
                EffectivityDate = effectivityDate.ToString("yyyy-MM-dd"),
                ModifiedByUser = EmployeeNo
            });

            if (rowsAffected > 0)
            {
                _auditTrail.Log("e_allowance", model.Id.Value, "UPDATED",
                    $"Updated allowance: {model.AllowanceCode} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Allowance updated successfully!" });
            }

            return Json(new { success = false, message = "Failed to update allowance." });
        }

        private JsonResult InsertAllowance(AllowanceDto model, DateTime effectivityDate)
        {
            // Creating a new record requires READWRITE or FULL
            if (!CanSaveAllowance)
                return Json(new { success = false, message = "Unauthorized: You do not have permission to create new allowances." });

            var existingAllowance = _db.QueryFirstOrDefault<userAllowances>(
                @"SELECT * FROM e_allowance
                  WHERE employeeNo   = @EmployeeNo
                  AND allowanceCode  = @AllowanceCode
                  AND isActive       = 1
                  AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { EmployeeNo = model.EmployeeNo, AllowanceCode = model.AllowanceCode });

            if (existingAllowance != null)
                return Json(new { success = false, message = "Active allowance already exists for this allowance type!" });

            var sql = @"
                INSERT INTO e_allowance (
                    employeeNo, isActive, allowanceCode, allowanceAmount,
                    effectivityDate, dtAdded, addedByUser
                )
                VALUES (
                    @EmployeeNo, @IsActive, @AllowanceCode, @AllowanceAmount,
                    @EffectivityDate, NOW(), @AddedByUser
                );
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                IsActive = 1,
                AllowanceCode = model.AllowanceCode,
                AllowanceAmount = CanViewAmount ? model.AllowanceAmount : 0,
                EffectivityDate = effectivityDate.ToString("yyyy-MM-dd"),
                AddedByUser = EmployeeNo
            });

            if (newId > 0)
            {
                _auditTrail.Log("e_allowance", newId, "CREATED",
                    $"Added allowance: {model.AllowanceCode} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "New allowance added successfully!" });
            }

            return Json(new { success = false, message = "Failed to add allowance." });
        }

        private List<userAllowances> GetAllowanceData(string employeeNo, bool isDeleted, bool? isActiveFilter = null)
        {
            string whereClause;

            if (isDeleted)
            {
                whereClause = "WHERE ea.employeeNo = @EmployeeNo AND (ea.dtDeleted IS NOT NULL AND ea.dtDeleted != '0000-00-00 00:00:00')";
            }
            else if (isActiveFilter.HasValue)
            {
                whereClause = isActiveFilter.Value
                    ? "WHERE ea.employeeNo = @EmployeeNo AND ea.isActive = 1 AND (ea.dtDeleted IS NULL OR ea.dtDeleted = '0000-00-00 00:00:00')"
                    : "WHERE ea.employeeNo = @EmployeeNo AND ea.isActive = 0 AND (ea.dtDeleted IS NULL OR ea.dtDeleted = '0000-00-00 00:00:00')";
            }
            else
            {
                whereClause = "WHERE ea.employeeNo = @EmployeeNo AND (ea.dtDeleted IS NULL OR ea.dtDeleted = '0000-00-00 00:00:00')";
            }

            var sql = BuildAllowanceQuery(whereClause);
            return _db.Query<userAllowances>(sql, new { EmployeeNo = employeeNo }).ToList();
        }

        private string BuildAllowanceQuery(string whereClause)
        {
            return $@"
                SELECT
                    ea.id,
                    ea.isActive,
                    ea.employeeNo,
                    ea.allowanceCode,
                    sa.allowanceName,
                    CASE WHEN IFNULL(sa.isTaxable, 0) = 1 THEN 'Taxable' ELSE 'NonTaxable' END as taxType,
                    sa.basis,
                    DATE_FORMAT(ea.effectivityDate, '%Y/%m/%d') AS effectivityDate,
                    CAST(ea.allowanceAmount AS DECIMAL(10,2)) AS allowanceAmount,
                    DATE_FORMAT(ea.dtAdded, '%Y/%m/%d') AS dtAdded,
                    CONCAT(COALESCE(u.lastName, ''), ', ', COALESCE(u.firstName, '')) AS addedByUser,
                    ea.dtLastModified,
                    ea.lastModifiedByUser,
                    ea.dtDeleted,
                    ea.deletedByUser
                FROM e_allowance ea
                LEFT JOIN s_user u ON u.userCode = ea.addedByUser
                LEFT JOIN s_allowance sa ON sa.allowanceCode = ea.allowanceCode
                {whereClause}
                ORDER BY ea.id DESC";
        }

        private bool RecordExists(int id)
        {
            var record = _db.QueryFirstOrDefault<userAllowances>(
                "SELECT * FROM e_allowance WHERE id = @Id AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')",
                new { Id = id });

            return record != null;
        }
    }
}