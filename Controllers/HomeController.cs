using Dapper;
using KEISAN_HRIS_v2.Models;
using KEISAN_HRIS_v2.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Diagnostics;

namespace KEISAN_HRIS_v2.Controllers
{
    public class HomeController : BaseController
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;

        public HomeController(
            ILogger<HomeController> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        
        [AllowAnonymous]
        public IActionResult Index()
        {
            CheckProbationaryNotifications();
            return View(); // Login page
        }

        private void CheckProbationaryNotifications()
        {
            try
            {
                var currentRoleCode = RoleCode ?? "";
                if (!currentRoleCode.ToUpper().Contains("HR"))
                    return;

                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var probationaryEmployees = con.Query<dynamic>(@"
                    SELECT 
                        e.employeeNo,
                        CONCAT(e.firstName, ' ', e.lastName) AS fullName
                    FROM e_basicinfo e
                    INNER JOIN s_employmentstatus s 
                        ON s.employmentStatusCode = e.employmentStatus
                    WHERE e.isActive = 1
                      AND e.probationaryStartDate IS NOT NULL
                      AND LOWER(s.employmentStatusName) LIKE '%probationary%'
                      AND DATEDIFF(CURDATE(), e.probationaryStartDate) >= 120
                ").ToList();

                if (!probationaryEmployees.Any()) return;

                var hrUsers = con.Query<string>(@"
                    SELECT u.userCode
                    FROM s_user u
                    WHERE u.isActive = 1
                      AND UPPER(u.roleCode) LIKE '%HR%'
                ").ToList();

                if (!hrUsers.Any()) return;

                foreach (var employee in probationaryEmployees)
                {
                    string employeeNo = employee.employeeNo;
                    string fullName = employee.fullName;

                    foreach (var hrUserCode in hrUsers)
                    {
                        var alreadyNotified = con.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) 
                    FROM s_notification
                    WHERE requestType         = 'probationary'
                      AND requestorEmployeeNo = @employeeNo
                      AND recipientEmployeeNo = @hrUserCode
                      AND isActive            = 1
                ", new { employeeNo, hrUserCode });

                        if (alreadyNotified > 0) continue;

                        var notificationCode = $"NOTIF-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 8)}";
                        var message = $"Employee {fullName} ({employeeNo}) is nearing the end of their probationary period. Please review for regularization.";

                        con.Execute(@"
                    INSERT INTO s_notification
                    (notificationCode, recipientEmployeeNo, requestType, requestId,
                     requestorEmployeeNo, actionType, message, isRead, dtCreated, isActive)
                    VALUES
                    (@notificationCode, @hrUserCode, 'probationary', 0,
                     @employeeNo, 'probationary_alert', @message, 0, NOW(), 1)
                ", new { notificationCode, hrUserCode, employeeNo, message });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckProbationaryNotifications.");
            }
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

    }
}
