using Dapper;
using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Models.Memo;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Memo
{
    [ModuleAuthorize("MMemo")]
    public class MemoController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public MemoController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Memo/Memo.cshtml");
        }

        private void ApplyDataScopeFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyDataScopeFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "e");
        }

        private void ApplyHiddenEmployeesFilter(StringBuilder query, DynamicParameters parameters)
        {
            DataScopeHelper.ApplyHiddenEmployeesFilter(_db, query, parameters, EmployeeNo, RoleCode, tableAlias: "e");
        }

        private bool CanViewEmployee(string employeeNo)
        {
            return DataScopeHelper.CanViewEmployee(_db, EmployeeNo, RoleCode, employeeNo);
        }

        // ─────────────────────────────────────────────────────────────────────
        // FIX 1: GetMemoList
        // Admins / creators see ALL memos they have access to via module.
        // Regular employees only see memos where they are a recipient.
        // Also added m.recipientType IS NULL as fallback for old data.
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetMemoList(string month, string year)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@loggedIn", EmployeeNo);

            // Check role access level for MMemo
            var access = AccessHelper.GetAccess(HttpContext, "MMemo");
            bool isAdminOrReadWrite = access == "FULL" || access == "READWRITE" || access == "EDIT";

            StringBuilder query;

            if (isAdminOrReadWrite)
            {
                // Admins / HR staff: see ALL active memos
                query = new StringBuilder(@"
                    SELECT
                        m.id,
                        m.seriesNo,
                        m.title,
                        DATE_FORMAT(m.effectivityDate, '%Y-%m-%d') AS effectivityDate,
                        m.remarks,
                        m.createdBy,
                        m.recipientType,
                        m.recipientTypeCode
                    FROM ad_memo m
                    WHERE m.isActive = 1");
            }
            else
            {
                // Regular employees (READ only): only see memos addressed to them
                query = new StringBuilder(@"
                    SELECT
                        m.id,
                        m.seriesNo,
                        m.title,
                        DATE_FORMAT(m.effectivityDate, '%Y-%m-%d') AS effectivityDate,
                        m.remarks,
                        m.createdBy,
                        m.recipientType,
                        m.recipientTypeCode
                    FROM ad_memo m
                    INNER JOIN e_basicinfo e ON e.employeeNo = @loggedIn AND e.isActive = 1
                    WHERE m.isActive = 1
                      AND (
                            m.recipientType IS NULL
                         OR m.recipientType = 'ALL'
                         OR (m.recipientType = 'INDIVIDUAL'        AND FIND_IN_SET(@loggedIn, REPLACE(m.recipientTypeCode, ' ', '')) > 0)
                         OR (m.recipientType = 'EMPLOYMENT_STATUS' AND m.recipientTypeCode = e.employmentStatus)
                         OR (m.recipientType = 'BRANCH'            AND m.recipientTypeCode = e.branchCode)
                         OR (m.recipientType = 'DEPARTMENT'        AND m.recipientTypeCode = e.departmentCode)
                         OR (m.recipientType = 'RANK'              AND m.recipientTypeCode = e.positionCode)
                      )");
            }

            if (!string.IsNullOrWhiteSpace(month) && !string.IsNullOrWhiteSpace(year))
            {
                query.Append(" AND MONTH(m.effectivityDate) = @month AND YEAR(m.effectivityDate) = @year");
                parameters.Add("@month", month);
                parameters.Add("@year", year);
            }
            else if (!string.IsNullOrWhiteSpace(year))
            {
                query.Append(" AND YEAR(m.effectivityDate) = @year");
                parameters.Add("@year", year);
            }
            else if (!string.IsNullOrWhiteSpace(month))
            {
                query.Append(" AND MONTH(m.effectivityDate) = @month");
                parameters.Add("@month", month);
            }

            query.Append(" ORDER BY m.dtAdded DESC");

            var memoList = (await _db.QueryAsync<MemoModelList>(query.ToString(), parameters)).ToList();
            return Json(new { data = memoList });
        }

        [HttpGet]
        public async Task<IActionResult> GetMemo(int id)
        {
            try
            {
                var memo = await _db.QueryFirstOrDefaultAsync<MemoModelList>(@"
                    SELECT
                        id,
                        seriesNo,
                        title,
                        DATE_FORMAT(effectivityDate, '%Y-%m-%d') AS effectivityDate,
                        remarks,
                        createdBy,
                        recipientType,
                        recipientTypeCode
                    FROM ad_memo
                    WHERE id = @id AND isActive = 1", new { id });

                return Json(memo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetMemo: {ex.Message}");
                return Json(null);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRecipients(int memoId)
        {
            if (memoId <= 0) return BadRequest("memoId is required");

            try
            {
                var memo = await _db.QueryFirstOrDefaultAsync<MemoModelList>(
                    "SELECT recipientType, recipientTypeCode FROM ad_memo WHERE id = @memoId AND isActive = 1",
                    new { memoId });

                if (memo == null) return Json(new { data = new List<object>() });

                var rType = (memo.recipientType ?? "").ToUpper().Trim();
                var rCode = memo.recipientTypeCode ?? "";

                var query = new StringBuilder(@"
                    SELECT
                        e.employeeNo,
                        CONCAT(e.lastName, ', ', e.firstName) AS employeeName,
                        e.employmentStatus,
                        sb.branchName,
                        sd.departmentName,
                        sp.positionName
                    FROM e_basicinfo e
                    LEFT JOIN s_branch     sb ON sb.branchCode     = e.branchCode
                    LEFT JOIN s_department sd ON sd.departmentCode = e.departmentCode
                    LEFT JOIN s_position   sp ON sp.positionCode   = e.positionCode
                    WHERE e.isActive = 1");

                var parameters = new DynamicParameters();

                switch (rType)
                {
                    case "ALL":
                        break;

                    case "INDIVIDUAL":
                        if (string.IsNullOrWhiteSpace(rCode))
                            return Json(new { recipientType = rType, recipientTypeCode = rCode, data = new List<object>() });
                        var empNos = rCode.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        if (!empNos.Any())
                            return Json(new { recipientType = rType, recipientTypeCode = rCode, data = new List<object>() });
                        query.Append(" AND e.employeeNo IN @empNos");
                        parameters.Add("@empNos", empNos);
                        break;

                    case "EMPLOYMENT_STATUS":
                        query.Append(" AND e.employmentStatus = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    case "BRANCH":
                        query.Append(" AND e.branchCode = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    case "DEPARTMENT":
                        query.Append(" AND e.departmentCode = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    case "RANK":
                        query.Append(" AND e.positionCode = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    default:
                        return Json(new { data = new List<object>() });
                }

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                query.Append(" ORDER BY e.lastName, e.firstName");

                var recipients = (await _db.QueryAsync<dynamic>(query.ToString(), parameters)).ToList();
                return Json(new { recipientType = rType, recipientTypeCode = rCode, data = recipients });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetRecipients: {ex.Message}");
                return Json(new { data = new List<object>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAttachments(int memoId)
        {
            if (memoId <= 0) return BadRequest("memoId is required");

            var attachments = (await _db.QueryAsync<dynamic>(@"
                SELECT id, attachmentPath, dtAdded
                FROM e_attachment
                WHERE employeeNo         = @memoId
                  AND attachmentTypeCode = 'MEMO'
                  AND isActive           = 1
                ORDER BY dtAdded DESC",
                new { memoId = memoId.ToString() })).ToList();

            return Json(attachments);
        }

        [HttpGet]
        public IActionResult GetEmployeeListForMemo(string searchTerm = "")
        {
            try
            {
                var query = new StringBuilder(@"
                    SELECT
                        e.employeeNo,
                        CONCAT(e.lastName, ', ', e.firstName) AS employeeName
                    FROM e_basicinfo e
                    WHERE e.isActive = 1");

                var parameters = new DynamicParameters();

                ApplyDataScopeFilter(query, parameters);
                ApplyHiddenEmployeesFilter(query, parameters);

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    query.Append(" AND (CONCAT(e.lastName, ', ', e.firstName) LIKE @search OR e.employeeNo LIKE @search)");
                    parameters.Add("@search", $"%{searchTerm}%");
                }

                query.Append(" ORDER BY e.lastName, e.firstName LIMIT 100");

                return Json(_db.Query<dynamic>(query.ToString(), parameters).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeListForMemo: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public IActionResult GetEmploymentStatusList() =>
            Json(_db.Query<dynamic>(
                "SELECT DISTINCT employmentStatus AS code, employmentStatus AS name FROM e_basicinfo WHERE isActive = 1 AND employmentStatus IS NOT NULL ORDER BY employmentStatus"
            ).ToList());

        [HttpGet]
        public IActionResult GetBranchListForMemo() =>
            Json(_db.Query<dynamic>(
                "SELECT branchCode AS code, branchName AS name FROM s_branch WHERE isActive = 1 ORDER BY branchName"
            ).ToList());

        [HttpGet]
        public IActionResult GetDepartmentListForMemo() =>
            Json(_db.Query<dynamic>(
                "SELECT departmentCode AS code, departmentName AS name FROM s_department WHERE isActive = 1 ORDER BY departmentName"
            ).ToList());

        [HttpGet]
        public IActionResult GetRankListForMemo() =>
            Json(_db.Query<dynamic>(
                "SELECT positionCode AS code, positionName AS name FROM s_position WHERE isActive = 1 ORDER BY positionName"
            ).ToList());

        [HttpPost]
        public async Task<IActionResult> AddMemo(MemoModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.SeriesNo))
                    return Json(new { success = false, message = "Series Number is required." });
                if (string.IsNullOrWhiteSpace(model.Title))
                    return Json(new { success = false, message = "Title is required." });
                if (string.IsNullOrWhiteSpace(model.EffectivityDate))
                    return Json(new { success = false, message = "Effectivity Date is required." });

                var recipientValidation = ValidateRecipient(model);
                if (!recipientValidation.valid)
                    return Json(new { success = false, message = recipientValidation.message });

                var exists = _db.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ad_memo WHERE seriesNo = @seriesNo AND isActive = 1",
                    new { model.SeriesNo });
                if (exists > 0)
                    return Json(new { success = false, message = "Series Number already exists." });

                var newId = await _db.QuerySingleAsync<int>(@"
                    INSERT INTO ad_memo
                        (seriesNo, title, effectivityDate, remarks,
                         recipientType, recipientTypeCode,
                         createdBy, isActive, dtAdded)
                    VALUES
                        (@seriesNo, @title, @effectivityDate, @remarks,
                         @recipientType, @recipientTypeCode,
                         @createdBy, 1, NOW());
                    SELECT LAST_INSERT_ID();",
                    new
                    {
                        seriesNo = model.SeriesNo,
                        title = model.Title,
                        effectivityDate = model.EffectivityDate,
                        remarks = model.Remarks ?? "",
                        recipientType = model.RecipientType,
                        recipientTypeCode = model.RecipientTypeCode ?? "",
                        createdBy = EmployeeNo ?? "SYSTEM"
                    });

                if (model.Attachments != null && model.Attachments.Count > 0)
                {
                    var uploadResult = await SaveAttachmentsAsync(newId.ToString(), model.Attachments);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Memo saved but attachment error: {uploadResult.message}" });
                }

                // ── FIX 2: Actually call InsertMemoNotificationsAsync ──────────
                await InsertMemoNotificationsAsync(newId, model.RecipientType, model.RecipientTypeCode);

                _auditTrail.Log("ad_memo", newId, "CREATED",
                    $"Added Memo: {model.SeriesNo} - {model.Title} | Recipients: {model.RecipientType} ({model.RecipientTypeCode})");

                return Json(new { success = true, message = "Memo added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddMemo: {ex.Message}");
                return Json(new { success = false, message = $"Error adding memo: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateMemo(MemoModel model)
        {
            try
            {
                if (model.Id <= 0)
                    return Json(new { success = false, message = "Invalid memo ID." });

                var existing = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT id, seriesNo FROM ad_memo WHERE id = @id AND isActive = 1",
                    new { model.Id });
                if (existing == null)
                    return Json(new { success = false, message = "Memo not found." });

                var duplicate = _db.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ad_memo WHERE seriesNo = @seriesNo AND id != @id AND isActive = 1",
                    new { model.SeriesNo, model.Id });
                if (duplicate > 0)
                    return Json(new { success = false, message = "Series Number already exists." });

                var recipientValidation = ValidateRecipient(model);
                if (!recipientValidation.valid)
                    return Json(new { success = false, message = recipientValidation.message });

                await _db.ExecuteAsync(@"
                    UPDATE ad_memo
                    SET seriesNo          = @seriesNo,
                        title             = @title,
                        effectivityDate   = @effectivityDate,
                        remarks           = @remarks,
                        recipientType     = @recipientType,
                        recipientTypeCode = @recipientTypeCode
                    WHERE id = @id",
                    new
                    {
                        seriesNo = model.SeriesNo,
                        title = model.Title,
                        effectivityDate = model.EffectivityDate,
                        remarks = model.Remarks ?? "",
                        recipientType = model.RecipientType,
                        recipientTypeCode = model.RecipientTypeCode ?? "",
                        id = model.Id
                    });

                if (model.Attachments != null && model.Attachments.Count > 0)
                {
                    var uploadResult = await SaveAttachmentsAsync(model.Id.ToString(), model.Attachments);
                    if (!uploadResult.success)
                        return Json(new { success = false, message = $"Memo updated but attachment error: {uploadResult.message}" });
                }

                _auditTrail.Log("ad_memo", model.Id, "UPDATED",
                    $"Updated Memo: {model.SeriesNo} - {model.Title} | Recipients: {model.RecipientType} ({model.RecipientTypeCode})");

                return Json(new { success = true, message = "Memo updated successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateMemo: {ex.Message}");
                return Json(new { success = false, message = $"Error updating memo: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMemo(int id, string reason)
        {
            try
            {
                if (id <= 0)
                    return Json(new { success = false, message = "Invalid memo ID." });

                var existing = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT seriesNo FROM ad_memo WHERE id = @id AND isActive = 1", new { id });
                if (existing == null)
                    return Json(new { success = false, message = "Memo not found." });

                await _db.ExecuteAsync("UPDATE ad_memo SET isActive = 0 WHERE id = @id", new { id });

                _auditTrail.Log("ad_memo", id, "DELETED",
                    $"Deleted Memo: {existing.seriesNo}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Memo deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteMemo: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // FIX 2: GetPendingMemoNotifications — moved to its own route bypass
        // NOTE: This is still on MemoController but we call it from Dashboard.
        // The [ModuleAuthorize] attribute on the class blocks employees without
        // MMemo access. We override at method level with [AllowWithSessionOnly]
        // by NOT using the authorize filter — instead we handle it inline.
        // Since we cannot remove class-level attribute per method in this setup,
        // the Dashboard must call a separate endpoint.
        // ─────────────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetPendingMemoNotifications()
        {
            try
            {
                var notifications = await _db.QueryAsync<dynamic>(@"
                    SELECT
                        n.id            AS notificationId,
                        n.requestId     AS memoId,
                        n.message,
                        n.dtCreated,
                        m.seriesNo,
                        m.title,
                        m.remarks,
                        DATE_FORMAT(m.effectivityDate, '%Y-%m-%d') AS effectivityDateFormatted,
                        DATE_FORMAT(m.dtAdded, '%M %d, %Y')        AS dateAdded
                    FROM s_notification n
                    INNER JOIN ad_memo m ON m.id = n.requestId AND m.isActive = 1
                    WHERE n.recipientEmployeeNo = @employeeNo
                      AND n.requestType         = 'memo'
                      AND n.actionType          = 'new_memo'
                      AND n.isRead              = 0
                      AND n.isActive            = 1
                      AND m.dtAdded            >= DATE_SUB(NOW(), INTERVAL 7 DAY)
                    ORDER BY n.dtCreated DESC",
                    new { employeeNo = EmployeeNo });

                return Json(new { success = true, data = notifications.ToList() });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPendingMemoNotifications: {ex.Message}");
                return Json(new { success = false, data = new List<object>() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkMemoReceived(int notificationId)
        {
            try
            {
                if (notificationId <= 0)
                    return Json(new { success = false, message = "Invalid notification ID." });

                await _db.ExecuteAsync(@"
                    UPDATE s_notification
                    SET isRead = 1,
                        dtRead = NOW()
                    WHERE id                  = @notificationId
                      AND recipientEmployeeNo = @employeeNo
                      AND requestType         = 'memo'
                      AND isActive            = 1",
                    new { notificationId, employeeNo = EmployeeNo });

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MarkMemoReceived: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────

        private (bool valid, string message) ValidateRecipient(MemoModel model)
        {
            if (string.IsNullOrWhiteSpace(model.RecipientType))
                return (false, "Please select a recipient type.");

            switch (model.RecipientType.ToUpper())
            {
                case "ALL":
                    break;
                case "INDIVIDUAL":
                    if (string.IsNullOrWhiteSpace(model.RecipientTypeCode))
                        return (false, "Please select at least one employee recipient.");
                    break;
                case "EMPLOYMENT_STATUS":
                case "BRANCH":
                case "DEPARTMENT":
                case "RANK":
                    if (string.IsNullOrWhiteSpace(model.RecipientTypeCode))
                        return (false, "Please select a value for the chosen recipient type.");
                    break;
                default:
                    return (false, "Invalid recipient type.");
            }

            return (true, "");
        }

        private async Task InsertMemoNotificationsAsync(int memoId, string recipientType, string recipientTypeCode)
        {
            try
            {
                var query = new StringBuilder("SELECT e.employeeNo FROM e_basicinfo e WHERE e.isActive = 1");
                var parameters = new DynamicParameters();
                var rType = (recipientType ?? "").ToUpper().Trim();
                var rCode = recipientTypeCode ?? "";

                switch (rType)
                {
                    case "ALL":
                        break;

                    case "INDIVIDUAL":
                        if (string.IsNullOrWhiteSpace(rCode)) return;
                        var empNos = rCode.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        if (!empNos.Any()) return;
                        query.Append(" AND e.employeeNo IN @empNos");
                        parameters.Add("@empNos", empNos);
                        break;

                    case "EMPLOYMENT_STATUS":
                        query.Append(" AND e.employmentStatus = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    case "BRANCH":
                        query.Append(" AND e.branchCode = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    case "DEPARTMENT":
                        query.Append(" AND e.departmentCode = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    case "RANK":
                        query.Append(" AND e.positionCode = @rCode");
                        parameters.Add("@rCode", rCode);
                        break;

                    default:
                        return;
                }

                var employeeNos = (await _db.QueryAsync<string>(query.ToString(), parameters)).ToList();
                if (!employeeNos.Any()) return;

                var memo = await _db.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT title, seriesNo FROM ad_memo WHERE id = @memoId", new { memoId });
                if (memo == null) return;

                string memoTitle = memo.title ?? "";
                string seriesNo = memo.seriesNo ?? "";
                string message = $"A new memo has been issued: [{seriesNo}] {memoTitle}";

                foreach (var empNo in employeeNos)
                {
                    // Avoid duplicate notifications for the same memo + employee
                    var alreadyExists = await _db.ExecuteScalarAsync<int>(@"
                        SELECT COUNT(*) FROM s_notification
                        WHERE recipientEmployeeNo = @empNo
                          AND requestType         = 'memo'
                          AND requestId           = @memoId
                          AND isActive            = 1",
                        new { empNo, memoId });

                    if (alreadyExists > 0) continue;

                    var notifCode = $"MEMO-{memoId}-{empNo}-{DateTime.Now:yyyyMMddHHmmss}";

                    await _db.ExecuteAsync(@"
                        INSERT INTO s_notification
                            (notificationCode, recipientEmployeeNo, requestType, requestId,
                             requestorEmployeeNo, actionType, message, isRead, dtCreated, isActive)
                        VALUES
                            (@notificationCode, @recipientEmployeeNo, 'memo', @memoId,
                             @createdBy, 'new_memo', @message, 0, NOW(), 1)",
                        new
                        {
                            notificationCode = notifCode,
                            recipientEmployeeNo = empNo,
                            memoId,
                            createdBy = EmployeeNo ?? "SYSTEM",
                            message
                        });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InsertMemoNotificationsAsync: {ex.Message}");
            }
        }

        private async Task<(bool success, string message)> SaveAttachmentsAsync(
            string memoId, List<IFormFile> files)
        {
            try
            {
                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
                const long maxSize = 5 * 1024 * 1024;

                var uploadPath = Path.Combine(
                    Directory.GetCurrentDirectory(), "wwwroot", "uploads", "memo");
                if (!Directory.Exists(uploadPath))
                    Directory.CreateDirectory(uploadPath);

                foreach (var file in files)
                {
                    if (file == null || file.Length == 0) continue;

                    if (file.Length > maxSize)
                        return (false, $"File '{file.FileName}' exceeds the 5 MB size limit.");

                    var ext = Path.GetExtension(file.FileName).ToLower();
                    if (!allowedExtensions.Contains(ext))
                        return (false, $"File '{file.FileName}' has an unsupported format.");

                    var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                    var physicalPath = Path.Combine(uploadPath, fileName);
                    var relativePath = $"/uploads/memo/{fileName}";

                    using (var stream = new FileStream(physicalPath, FileMode.Create))
                        await file.CopyToAsync(stream);

                    await _db.ExecuteAsync(@"
                        INSERT INTO e_attachment
                            (employeeNo, attachmentDescription, attachmentTypeCode, attachmentPath, isActive, dtAdded)
                        VALUES
                            (@memoId, 'Memo Attachment', 'MEMO', @relativePath, 1, NOW())",
                        new { memoId, relativePath });
                }

                return (true, "Attachments saved successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving attachments: {ex.Message}");
                return (false, $"Error saving attachments: {ex.Message}");
            }
        }
    }
}