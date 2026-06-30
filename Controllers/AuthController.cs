using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Models;
using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Dapper;
using System.Net.Mail;
using System.Net;

namespace KEISAN_HRIS_v2.Controllers
{
    public class AuthController : Controller
    {

        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;

        public AuthController(
            ILogger<HomeController> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [AllowAnonymous]
        public IActionResult Login()
        {
            return View(); // Login page
        }

        [AllowAnonymous]
        public IActionResult Page403()
        {
            return View(); // 403 Forbidden page
        }

        [AllowAnonymous]
        [HttpPost]
        public IActionResult UserLogin(usersLogin acc)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, responseText = "Please complete all required fields" });
            }

            try
            {
                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                con.Open();

                string query = @"
                    SELECT 
                        u.userCode,
                        u.roleCode,
                        CONCAT(u.lastName, ', ', u.firstName) AS userFullName

                    FROM s_user u
                    WHERE u.userCode = @UserCode
                      AND CAST(AES_DECRYPT(u.password, 'portal123') AS CHAR) = @Password
                      AND u.isActive = 1
                    LIMIT 1;
                ";

                using var cmd = new MySqlCommand(query, con);
                cmd.Parameters.AddWithValue("@UserCode", acc.UserCode);
                cmd.Parameters.AddWithValue("@Password", acc.Password);


                using var reader = cmd.ExecuteReader();

                if (!reader.Read())
                {
                    return Json(new
                    {
                        success = false,
                        responseText = "Invalid Username or Password!"
                    });
                }

                // ✅ REQUIRED SESSIONS
                HttpContext.Session.SetString("employeeNo", reader["UserCode"].ToString());
                HttpContext.Session.SetString("userFullName", reader["userFullName"].ToString());
                HttpContext.Session.SetString("roleCode", reader["roleCode"].ToString());

                // SECURITY: Store login timestamp
                HttpContext.Session.SetString("loginTime", DateTime.UtcNow.ToString("o"));
                HttpContext.Session.SetString("ipAddress", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");

                LoadRoleAccessToSession(reader["roleCode"].ToString());

                _logger.LogInformation("User {UserCode} logged in successfully from IP {IP}",
                    reader["UserCode"].ToString(),
                    HttpContext.Connection.RemoteIpAddress);

                return Json(new
                {
                    success = true,
                    responseText = "Successfully logged in!"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed");
                return Json(new
                {
                    success = false,
                    responseText = "An error occurred during login."
                    //responseText = ex.Message
                });
            }
        }


        private void LoadRoleAccessToSession(string roleCode)
        {
            using var con = new MySqlConnection(
                _configuration.GetConnectionString("DefaultConnection"));
            con.Open();

            var data = con.Query<(string moduleCode, string accessLevel)>(@"
                SELECT moduleCode, accessLevel
                FROM s_roleaccess
                WHERE roleCode = @roleCode
            ", new { roleCode });

            var dict = data.ToDictionary(
                x => x.moduleCode,
                x => x.accessLevel
            );

            HttpContext.Session.SetString(
                "ROLE_ACCESS",
                System.Text.Json.JsonSerializer.Serialize(dict)
            );
        }


        [HttpPost]
        public IActionResult Logout()
        {
            var userCode = HttpContext.Session.GetString("employeeNo");

            _logger.LogInformation("User {UserCode} logged out", userCode ?? "Unknown");

            HttpContext.Session.Clear();
            return Json(new { success = true });
        }

        /// Keep session alive endpoint
        [HttpPost]
        public IActionResult KeepAlive()
        {
            var employeeNo = HttpContext.Session.GetString("employeeNo");

            if (string.IsNullOrEmpty(employeeNo))
            {
                return Json(new { success = false, message = "Session expired" });
            }

            // Just accessing the session keeps it alive
            return Json(new { success = true, message = "Session refreshed" });
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        [HttpPost]
        public JsonResult GetEmployeeEmail(string employeeNo)
        {
            using var con = new MySqlConnection(
               _configuration.GetConnectionString("DefaultConnection"));
            con.Open();

            try
            {
                var emailAddress = con.QueryFirstOrDefault<string>(@"
                SELECT emailAddress FROM e_personalinfo WHERE employeeNo = @employeeNo
                ", new { employeeNo }) ?? "";

                return Json(new { emailAddress });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeEmail: {ex.Message}");
                return Json(new { emailAddress = "", error = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult sendresetpassword(string employeeNo, string emailAddress)
        {
            using var con = new MySqlConnection(
               _configuration.GetConnectionString("DefaultConnection"));
            con.Open();

            try
            {
                string newpassword = GetRandomString();
                var resetemailAddress = con.QueryFirstOrDefault<string>(@"
                UPDATE s_user SET password = AES_ENCRYPT(@newpassword,'portal123') WHERE userCode = @employeeNo
                ", new { newpassword, employeeNo }) ?? "";

                SendEmail(newpassword, emailAddress);

                return Json(new { resetemailAddress });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeEmail: {ex.Message}");
                return Json(new { emailAddress = "", error = ex.Message });
            }

        }

        public static void SendEmail(string newpassword, string email)
        {

            try
            {
                // Set up the email details
                string smtpAddress = "smtp.hostinger.com"; // Change to your SMTP server address
                int portNumber = 587; // Change to the port you are using (587 for TLS or 465 for SSL)
                bool enableSSL = true;

                string emailFrom = "noreply-keisan-payroll@northlogic.com.ph"; // Your email address
                string password = "Nl@54321"; // Password ng email
                string emailTo = email; // Recipient email address
                string subject = "KEISAN RESET PASSWORD";
                string body = @"
             <html>
             <body>
            
             <p></p>re
             <p>Please use this as your new password ' " + newpassword + @" ', we also encourage you to change your password once you loggedin </p>
             <p>If you have any questions or require further assistance, please contact the HR department.</p>
             <p>Thank you.</p>
             <hr>
             <p><em>This is an automated message. Please do not reply directly to this email.</em></p>
             </body>
             </html>";

                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress(emailFrom);
                    mail.To.Add(emailTo);
                    mail.Subject = subject;
                    mail.Body = body;
                    mail.IsBodyHtml = true; // Set to false if the email body is not HTML

                    using (SmtpClient smtp = new SmtpClient(smtpAddress, portNumber))
                    {
                        smtp.Credentials = new NetworkCredential(emailFrom, password);
                        smtp.EnableSsl = enableSSL;
                        smtp.Send(mail);
                        Console.WriteLine("Email sent successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception caught in CreateTestMessage2(): {0}", ex.ToString());
            }

        }

        public static String GetRandomString()
        {
            var allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
            var length = 8;

            var chars = new char[length];
            var rd = new Random();

            for (var i = 0; i < length; i++)
            {
                chars[i] = allowedChars[rd.Next(0, allowedChars.Length)];
            }

            return new String(chars);
        }
    }
}