using System.Threading.Tasks;

namespace KEISAN_HRIS_v2.Services.OtherServices
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
        Task SendRequestEmailAsync(string title, string requestor, string approverEmail, string dateFrom, string dateTo);
        Task SendRequestStatusEmailAsync(string Title, string employeeEmail, string dateFrom, string dateTo, string level1Status, string level2Status,
            string level3Status, string level4Status);
        Task SendPayslipInEmailAsync(string title, string employeeEmail, string datemonth, string cutoff, string dateyear);

        //used to get get employeeName sa mga request
        Task<string> GetEmployeeNameAsync(string employeeNo);
        Task<string> GetApproverEmails(string employeeNo, int approverLevel);
        Task<string> GetEmployeeEmail(string employeeNo);
        Task<int?> GetLeastApproverLevelAsync(string employeeNo);
    }
}