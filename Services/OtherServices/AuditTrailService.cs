// Services/AuditTrailService.cs
using Dapper;
using System.Data;

namespace KEISAN_HRIS_v2.Services.OtherServices
{
    public class AuditTrailService : IAuditTrailService
    {
        private readonly IDbConnection _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditTrailService(IDbConnection db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(string tableName, int referenceId, string action, string remarks = "")
        {
            try
            {
                string usercode = GetCurrentUser();

                string sql = @"INSERT INTO audit_trail 
                              (tableName, referenceID, action, remarks, usercode, dtAdded) 
                              VALUES (@tableName, @referenceID, @action, @remarks, @usercode, NOW())";

                await _db.ExecuteAsync(sql, new
                {
                    tableName,
                    referenceID = referenceId,
                    action,
                    remarks,
                    usercode
                });
            }
            catch (Exception ex)
            {
                // Log error but don't break the main operation
                Console.WriteLine($"Audit trail error: {ex.Message}");
            }
        }

        public void Log(string tableName, int referenceId, string action, string remarks = "")
        {
            Task.Run(() => LogAsync(tableName, referenceId, action, remarks));
        }

        public string GetCurrentUser()
        {
            var context = _httpContextAccessor.HttpContext;

            // Get the logged-in user from session
            var userCode = context?.Session?.GetString("employeeNo");

            // Fallback to SYSTEM if no user is logged in
            return !string.IsNullOrEmpty(userCode) ? userCode : "SYSTEM";
        }
    }
}