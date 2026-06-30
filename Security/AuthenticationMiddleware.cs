using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace KEISAN_HRIS_v2.Security
{
    /// Global authentication middleware to prevent URL bypass
    /// This middleware runs before any controller action and validates session
    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuthenticationMiddleware> _logger;

        // Public paths that don't require authentication
        private static readonly string[] _allowedPaths = new[]
        {
            "/auth/login",
            "/auth/userlogin",
            "/auth/page403",
            "/auth/logout"
        };

        // Static file extensions that should be allowed
        private static readonly string[] _staticFileExtensions = new[]
        {
            ".css", ".js", ".jpg", ".jpeg", ".png", ".gif", ".ico",
            ".svg", ".woff", ".woff2", ".ttf", ".eot", ".map", ".json"
        };

        public AuthenticationMiddleware(RequestDelegate next, ILogger<AuthenticationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

            // Allow static files
            if (IsStaticFile(path))
            {
                await _next(context);
                return;
            }

            // Allow public paths (login, logout, 403)
            if (IsAllowedPath(path))
            {
                await _next(context);
                return;
            }

            if (path == "/auth/getemployeeemail" || path == "/auth/sendresetpassword")
            {
                await _next(context);
                return;
            }

            // Check if user is authenticated via session
            var employeeNo = context.Session.GetString("employeeNo");
            var roleCode = context.Session.GetString("roleCode");

            if (string.IsNullOrEmpty(employeeNo) || string.IsNullOrEmpty(roleCode))
            {
                _logger.LogWarning("Unauthorized access attempt to: {Path} from IP: {IP}",
                    path, context.Connection.RemoteIpAddress);

                // If it's an AJAX request, return JSON
                if (IsAjaxRequest(context.Request))
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"success\":false,\"message\":\"Session expired. Please login again.\",\"redirect\":\"/Auth/Login\"}");
                    return;
                }

                // Otherwise redirect to login
                context.Response.Redirect("/Auth/Login");
                return;
            }

            // Session exists, proceed to next middleware/controller
            await _next(context);
        }

        private bool IsAllowedPath(string path)
        {
            foreach (var allowedPath in _allowedPaths)
            {
                if (path.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsStaticFile(string path)
        {
            foreach (var extension in _staticFileExtensions)
            {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsAjaxRequest(HttpRequest request)
        {
            return request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                   request.Headers["Accept"].ToString().Contains("application/json");
        }
    }

    /// Extension method to add the middleware to the pipeline
    public static class AuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseAuthenticationMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuthenticationMiddleware>();
        }
    }
}