using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MySql.Data.MySqlClient;
using Dapper;

namespace KEISAN_HRIS_v2.Controllers
{
    /// <summary>
    /// Enhanced BaseController with improved session validation and notification support
    /// All controllers should inherit from this to ensure authentication
    /// </summary>
    public class BaseController : Controller
    {
        protected IConfiguration _configuration;
        protected ILogger _logger;

        protected string EmployeeNo =>
            HttpContext.Session.GetString("employeeNo");
        protected string RoleCode =>
            HttpContext.Session.GetString("roleCode");
        protected string UserFullName =>
            HttpContext.Session.GetString("userFullName");

        /// <summary>
        /// Validates session before every action execution
        /// Provides double-layer protection with the middleware
        /// </summary>
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Get configuration and logger from services if not set
            if (_configuration == null)
            {
                _configuration = context.HttpContext.RequestServices
                    .GetService(typeof(IConfiguration)) as IConfiguration;
            }
            if (_logger == null)
            {
                var loggerFactory = context.HttpContext.RequestServices
                    .GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                _logger = loggerFactory?.CreateLogger(GetType());
            }

            // Double-check authentication (middleware already checked, but this is extra protection)
            if (string.IsNullOrEmpty(EmployeeNo) || string.IsNullOrEmpty(RoleCode))
            {
                // Check if it's an AJAX request
                if (context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                    context.HttpContext.Request.Headers["Accept"].ToString().Contains("application/json"))
                {
                    context.Result = new JsonResult(new
                    {
                        success = false,
                        message = "Session expired. Please login again.",
                        redirect = "/Auth/Login"
                    })
                    {
                        StatusCode = 401
                    };
                    return;
                }
                // Regular request - redirect to 403
                context.Result = new RedirectToActionResult("Page403", "Auth", null);
                return;
            }
            base.OnActionExecuting(context);
        }

        /// <summary>
        /// Checks if current user is admin
        /// </summary>
        protected bool IsAdmin()
        {
            return RoleCode == "RL-000000";
        }

        /// <summary>
        /// Gets current user info as a formatted object
        /// </summary>
        protected object GetCurrentUserInfo()
        {
            return new
            {
                employeeNo = EmployeeNo,
                roleCode = RoleCode,
                userFullName = UserFullName,
                isAdmin = IsAdmin()
            };
        }

        #region Notification Methods

        /// <summary>
        /// Main method to notify users about request actions
        /// Call this method whenever a request status changes
        /// </summary>
        /// <param name="requestType">Type of request (leave, changeSchedule, officialBusiness, etc.)</param>
        /// <param name="requestId">ID of the request</param>
        /// <param name="requestorEmployeeNo">Employee who made the request</param>
        /// <param name="newStatus">New status (pending, approved, declined, cancelled)</param>
        /// <param name="approverEmployeeNo">Optional: Employee who approved/declined</param>
        protected void NotifyRequestAction(
            string requestType,
            int requestId,
            string requestorEmployeeNo,
            string newStatus,
            string approverEmployeeNo = null)
        {
            try
            {
                if (_configuration == null)
                {
                    _logger?.LogWarning("Configuration not available for notifications");
                    return;
                }

                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                // Determine recipients and action type
                var actionType = newStatus.ToLower();
                var recipients = new List<string>();

                if (actionType == "pending")
                {
                    // Notify approvers (admins or specific approvers based on your workflow)
                    var approvers = con.Query<string>(@"
                        SELECT DISTINCT u.userCode 
                        FROM s_user u 
                        WHERE u.roleCode = 'RL-000000' 
                        AND u.isActive = 1
                        AND u.userCode != @requestorEmployeeNo",
                        new { requestorEmployeeNo }).ToList();

                    recipients.AddRange(approvers);
                }
                else if (actionType == "approved" || actionType == "declined" || actionType == "cancelled")
                {
                    // Notify the requestor
                    recipients.Add(requestorEmployeeNo);
                }

                // Create notification for each recipient
                foreach (var recipientEmployeeNo in recipients)
                {
                    CreateNotification(
                        con,
                        recipientEmployeeNo,
                        requestType,
                        requestId,
                        requestorEmployeeNo,
                        actionType
                    );
                }

                _logger?.LogInformation(
                    "Notifications created: Type={RequestType}, Action={ActionType}, Recipients={RecipientCount}",
                    requestType, actionType, recipients.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in NotifyRequestAction");
            }
        }

        /// <summary>
        /// Creates a single notification record in the database
        /// </summary>
        private void CreateNotification(
            MySqlConnection con,
            string recipientEmployeeNo,
            string requestType,
            int requestId,
            string requestorEmployeeNo,
            string actionType)
        {
            try
            {
                var message = GenerateNotificationMessage(
                    con, requestType, actionType, requestorEmployeeNo);

                var notificationCode = $"NOTIF-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 8)}";

                var query = @"
                    INSERT INTO s_notification 
                    (notificationCode, recipientEmployeeNo, requestType, requestId, 
                     requestorEmployeeNo, actionType, message, isRead, dtCreated, isActive)
                    VALUES 
                    (@notificationCode, @recipientEmployeeNo, @requestType, @requestId, 
                     @requestorEmployeeNo, @actionType, @message, 0, NOW(), 1)";

                con.Execute(query, new
                {
                    notificationCode,
                    recipientEmployeeNo,
                    requestType,
                    requestId,
                    requestorEmployeeNo,
                    actionType,
                    message
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating individual notification");
            }
        }

        /// <summary>
        /// Generates a user-friendly notification message
        /// </summary>
        private string GenerateNotificationMessage(
            MySqlConnection con,
            string requestType,
            string actionType,
            string requestorEmployeeNo)
        {
            var requestorName = con.QueryFirstOrDefault<string>(
                "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                new { employeeNo = requestorEmployeeNo }) ?? "An employee";

            var requestTypeDisplay = GetRequestTypeDisplay(requestType);

            return actionType switch
            {
                "pending" => $"{requestorName} submitted a {requestTypeDisplay} request that requires your approval.",
                "approved" => $"Your {requestTypeDisplay} request has been approved.",
                "declined" => $"Your {requestTypeDisplay} request has been declined.",
                "cancelled" => $"Your {requestTypeDisplay} request has been cancelled.",
                _ => $"Status update for your {requestTypeDisplay} request."
            };
        }

        /// <summary>
        /// Converts request type code to display-friendly name
        /// </summary>
        private string GetRequestTypeDisplay(string requestType)
        {
            return requestType switch
            {
                "leave" => "Leave",
                "changeSchedule" => "Change Schedule",
                "officialBusiness" => "Official Business",
                "cto" => "CTO",
                "offsetCredit" => "Offset Credit",
                "overtime" => "Overtime",
                "undertime" => "Undertime",
                "workFromHome" => "Work From Home",
                _ => "Request"
            };
        }

        /// <summary>
        /// Helper method to get requestor employee number from any request table
        /// </summary>
        protected string GetRequestorEmployeeNo(string tableName, int requestId)
        {
            try
            {
                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                var query = $"SELECT employeeNo FROM {tableName} WHERE id = @requestId LIMIT 1";
                return con.QueryFirstOrDefault<string>(query, new { requestId });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting requestor employee number");
                return null;
            }
        }

        #endregion
    }
}