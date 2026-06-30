using System.Data;
using System.Net;
using System.Net.Mail;
using Dapper;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace KEISAN_HRIS_v2.Services.OtherServices
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _emailSettings;
        private readonly string _connectionString;

        public EmailService(IOptions<EmailSettings> emailSettings, IConfiguration configuration)
        {
            _emailSettings = emailSettings.Value;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        private IDbConnection CreateConnection()
        {
            return new MySqlConnection(_connectionString);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            using var mail = new MailMessage();
            mail.From = new MailAddress(_emailSettings.EmailFrom);
            mail.To.Add(toEmail);
            mail.Subject = subject;
            mail.Body = body;
            mail.IsBodyHtml = true;

            using var smtp = new SmtpClient(_emailSettings.SmtpAddress, _emailSettings.PortNumber);
            smtp.Credentials = new NetworkCredential(_emailSettings.EmailFrom, _emailSettings.Password);
            smtp.EnableSsl = _emailSettings.EnableSSL;

            await smtp.SendMailAsync(mail);
        }

        public async Task SendRequestEmailAsync(string title, string requestor, string approverEmail, string dateFrom, string dateTo)
        {
            string subject = title;
            string body = BuildRequestTemplate(title, requestor, dateFrom, dateTo);
            await SendEmailAsync(approverEmail, subject, body);
        }

        public async Task SendRequestStatusEmailAsync(
            string title,
            string employeeEmail,
            string dateFrom,
            string dateTo,
            string level1Status,
            string level2Status,
            string level3Status,
            string level4Status)
        {
            string subject = title;
            string body = BuildRequestStatusTemplate(title, employeeEmail, dateFrom, dateTo, level1Status, level2Status, level3Status, level4Status);
            await SendEmailAsync(employeeEmail, subject, body);
        }

        private string BuildRequestTemplate(string title, string requestor, string dateFrom, string dateTo)
        {
            return $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
            <p><strong>{title}</strong></p>
            <p>This is an automated notification to inform you that <strong>{requestor}</strong> has submitted a request.</p>
            <p><strong>Date From:</strong> {dateFrom}<br><strong>Date To:</strong> {dateTo}</p>
            <p>Please review the request in the HRIS portal.</p>
            <p>Thank you.</p>
            <hr>
            <p style='color:gray'><em>This is an automated message. Please do not reply.</em></p>
            </body>
            </html>";
        }

        private string BuildRequestStatusTemplate(
            string title,
            string employeeEmail,
            string dateFrom,
            string dateTo,
            string level1Status,
            string level2Status,
            string level3Status,
            string level4Status)
        {
            return $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
            <p><strong>{title}</strong></p>
            <p>This is an automated notification to inform you that your request for:</p>
            <p><strong>Date From:</strong> {dateFrom}<br><strong>Date To:</strong> {dateTo}</p>
            <p>has the following status:</p>
            <p>Status1: {level1Status}</p>
            <p>Status2: {level2Status}</p>
            <p>Status3: {level3Status}</p>
            <p>Status4: {level4Status}</p>
            <p>Please review the request in the HRIS portal.</p>
            <p>Thank you.</p>
            <hr>
            <p style='color:gray'><em>This is an automated message. Please do not reply.</em></p>
            </body>
            </html>";
        }

        public async Task SendPayslipInEmailAsync(string title, string employeeEmail, string datemonth, string cutoff, string dateyear)
        {
            string subject = title;
            string body = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
            <p><strong>{title}</strong></p>
            <p>This is an automated notification to inform you that your payslip for <strong>{datemonth} {cutoff} {dateyear}</strong> is now available for viewing</p>
            <p>Thank you.</p>
            <hr>
            <p style='color:gray'><em>This is an automated message. Please do not reply.</em></p>
            </body>
            </html>";

            await SendEmailAsync(employeeEmail, subject, body);
        }

        public async Task<string> GetEmployeeNameAsync(string employeeNo)
        {
            using var db = CreateConnection();

            return await db.QueryFirstOrDefaultAsync<string>(@"
                SELECT CONCAT(e.lastName, ',', e.firstName, ' ', e.middleName)
                FROM e_basicinfo e
                WHERE e.employeeNo = @employeeNo
            ", new { employeeNo }) ?? "";
        }

        public async Task<string> GetApproverEmails(string employeeNo, int approverLevel)
        {
            using var db = CreateConnection();

            return await db.QueryFirstOrDefaultAsync<string>(@"
                SELECT pi.emailAddress
                FROM e_approver ap
                INNER JOIN e_personalinfo pi ON pi.employeeNo = ap.approverNo
                WHERE ap.employeeNo = @employeeNo
                  AND ap.approverLevel = @approverLevel
                  AND ap.isActive = 1
                  AND IFNULL(pi.emailAddress, '') <> ''
            ", new { employeeNo, approverLevel }) ?? "";
        }

        public async Task<string> GetEmployeeEmail(string employeeNo)
        {
            using var db = CreateConnection();

            return await db.QueryFirstOrDefaultAsync<string>(@"
                SELECT IFNULL(emailAddress, '')
                FROM e_personalinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1
            ", new { employeeNo }) ?? "";
        }

        public async Task<int?> GetLeastApproverLevelAsync(string employeeNo)
        {
            using var db = CreateConnection();

            return await db.QueryFirstOrDefaultAsync<int?>(@"
                SELECT MIN(approverLevel)
                FROM e_approver
                WHERE employeeNo = @employeeNo
                  AND isActive = 1
            ", new { employeeNo });
        }

        public class EmailSettings
        {
            public string SmtpAddress { get; set; } = "";
            public int PortNumber { get; set; }
            public bool EnableSSL { get; set; }
            public string EmailFrom { get; set; } = "";
            public string Password { get; set; } = "";
        }
    }
}