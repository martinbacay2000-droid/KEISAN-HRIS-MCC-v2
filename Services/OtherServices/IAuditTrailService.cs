// Services/IAuditTrailService.cs
namespace KEISAN_HRIS_v2.Services.OtherServices
{
    public interface IAuditTrailService
    {
        Task LogAsync(string tableName, int referenceId, string action, string remarks = "");
        void Log(string tableName, int referenceId, string action, string remarks = "");
    }
}