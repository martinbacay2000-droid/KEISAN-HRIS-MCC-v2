using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Data;
using System.Text.Json;

namespace KEISAN_HRIS_v2.Security
{
    /// Module authorization attribute that enforces role-based access control
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class ModuleAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _moduleCode;
        private const string ADMIN_ROLE = "RL-000000";

        public ModuleAuthorizeAttribute(string moduleCode)
        {
            _moduleCode = moduleCode;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // 1. CHECK IF USER IS AUTHENTICATED
            var employeeNo = context.HttpContext.Session.GetString("employeeNo");
            var roleCode = context.HttpContext.Session.GetString("roleCode");

            if (string.IsNullOrEmpty(employeeNo) || string.IsNullOrEmpty(roleCode))
            {
                // Not authenticated - redirect to login
                context.Result = new RedirectToActionResult("Login", "Auth", null);
                return;
            }

            // 2. ADMIN BYPASS - Admin role has access to everything
            if (roleCode == ADMIN_ROLE)
                return;

            // 3. GET ROLE ACCESS FROM SESSION
            var json = context.HttpContext.Session.GetString("ROLE_ACCESS");
            if (string.IsNullOrEmpty(json))
            {
                // No role access configured - deny access
                context.Result = new RedirectToActionResult("Page403", "Auth", null);
                return;
            }

            // 4. DESERIALIZE AND CHECK MODULE ACCESS
            Dictionary<string, string> dict;
            try
            {
                dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            catch
            {
                // Invalid JSON in session - deny access
                context.Result = new RedirectToActionResult("Page403", "Auth", null);
                return;
            }

            // 5. VERIFY MODULE ACCESS EXISTS AND IS NOT NO_ACCESS
            if (!dict.ContainsKey(_moduleCode))
            {
                // Module not in role access - deny
                context.Result = new RedirectToActionResult("Page403", "Auth", null);
                return;
            }

            var accessLevel = dict[_moduleCode];

            // 6. CHECK IF ACCESS LEVEL IS VALID (NOT NO_ACCESS)
            if (accessLevel is not ("READ" or "EDIT" or "READWRITE" or "FULL"))
            {
                // No access or invalid access level - deny
                context.Result = new RedirectToActionResult("Page403", "Auth", null);
                return;
            }

            // 7. ACCESS GRANTED - User has valid access level
            // The access level (READ, EDIT, READWRITE, FULL) will be used by AccessHelper
            // in the controller to determine specific permissions
        }
    }
}