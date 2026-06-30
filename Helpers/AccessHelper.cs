using System.Data;
using System.Text.Json;
using Dapper;

namespace KEISAN_HRIS_v2.Helpers
{
    public static class AccessHelper
    {
        private const string ADMIN_ROLE = "RL-000000";

        private static string GetAccessLevel(HttpContext context, string moduleCode)
        {
            var roleCode = context.Session.GetString("roleCode");
            if (string.IsNullOrEmpty(roleCode))
                return null;

            if (roleCode == ADMIN_ROLE)
                return "FULL";

            var json = context.Session.GetString("ROLE_ACCESS");
            if (string.IsNullOrEmpty(json))
                return null;

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            return dict != null && dict.ContainsKey(moduleCode)
                ? dict[moduleCode]
                : "NO_ACCESS";
        }

        // Can view records
        public static bool CanView(HttpContext ctx, string module)
            => GetAccessLevel(ctx, module) is "READ" or "EDIT" or "READWRITE" or "FULL";

        // Can edit existing records (but NOT create new ones)
        public static bool CanEdit(HttpContext ctx, string module)
            => GetAccessLevel(ctx, module) is "EDIT" or "READWRITE" or "FULL";

        // Can create new records
        public static bool CanCreate(HttpContext ctx, string module)
            => GetAccessLevel(ctx, module) is "READWRITE" or "FULL";

        // Can delete records
        public static bool CanDelete(HttpContext ctx, string module)
            => GetAccessLevel(ctx, module) == "FULL";

        // Get the raw access level string
        public static string GetAccess(HttpContext ctx, string module)
            => GetAccessLevel(ctx, module);
    }

    // ── COE Print Access ─────────────────────────────────────────────────────
    // Both         – roleCode contains "HR DIRECTOR" (e.g. "HR DIRECTOR 01")
    //                Also granted to super-admin "RL-000000"
    // WithoutOnly  – roleCode contains "HR" but NOT "HR DIRECTOR"
    //                (e.g. "HR SUPERVISOR", "HR ASISTANT 1", "HR CASUAL")
    // None         – everything else
    // ────────────────────────────────────────────────────────────────────────
    public enum CoeAccessLevel { None, WithoutOnly, SupervisorWith, Both }

    public static class CoeAccessHelper
    {
        private const string AdminRole = "RL-000000";
        private const string HrDirectorKey = "HR DIRECTOR";
        private const string HrKey = "HR";

        public static CoeAccessLevel GetCoeAccess(string? roleCode)
        {
            if (string.IsNullOrWhiteSpace(roleCode))
                return CoeAccessLevel.None;

            if (roleCode == AdminRole)
                return CoeAccessLevel.Both;

            var upper = roleCode.Trim().ToUpperInvariant();

            // Check HR DIRECTOR before HR SUPERVISOR before generic HR
            if (upper.Contains(HrDirectorKey))
                return CoeAccessLevel.Both;

            // HR SUPERVISOR — can print With Compensation but with rank restrictions
            if (upper.Contains("HR SUPERVISOR"))
                return CoeAccessLevel.SupervisorWith;

            // Other HR roles — without compensation only
            if (upper.Contains(HrKey))
                return CoeAccessLevel.WithoutOnly;

            return CoeAccessLevel.None;
        }

        public static bool CanPrintWithout(string? roleCode)
        {
            var level = GetCoeAccess(roleCode);
            return level == CoeAccessLevel.Both
                || level == CoeAccessLevel.WithoutOnly
                || level == CoeAccessLevel.SupervisorWith;
        }

        public static bool CanPrintWith(string? roleCode)
            => GetCoeAccess(roleCode) == CoeAccessLevel.Both;

        /// <summary>
        /// Checks if the logged-in user can print COE With Compensation
        /// for a specific target employee identified by their rankCode.
        ///
        /// Rules:
        ///   - RL-000000 (admin) and HR DIRECTOR → always allowed
        ///   - HR SUPERVISOR → allowed ONLY if target employee's rankCode
        ///     is NOT in the excluded list (MANAGER, TEAM LEADER, TOP MANAGEMENT)
        ///   - Everyone else → denied
        /// </summary>
        public static bool CanPrintWithForEmployee(string? roleCode, string? employeeRankCode)
        {
            if (string.IsNullOrWhiteSpace(roleCode))
                return false;

            // Admin — no restrictions
            if (roleCode == AdminRole)
                return true;

            var upper = roleCode.Trim().ToUpperInvariant();

            // HR DIRECTOR — no rank restrictions
            if (upper.Contains(HrDirectorKey))
                return true;

            // HR SUPERVISOR — allowed unless employee rank is in the excluded list
            if (upper.Contains("HR SUPERVISOR"))
            {
                var rank = (employeeRankCode ?? "").Trim().ToUpperInvariant();
                string[] excludedRanks = ["MANAGER", "TEAM LEADER", "TOP MANAGEMENT"];
                return !excludedRanks.Contains(rank);
            }

            return false;
        }
    }

     // ── PhilHealth Certificate of Contributions Print Access ─────────────────
     // Allowed:  RL-000000 (admin), HR DIRECTOR, HR SUPERVISOR, HR ASSISTANT 1
     // Blocked:  HR ASSISTANT 2 and any other role not in the allowlist
     // ─────────────────────────────────────────────────────────────────────────
    public static class PhilHealthCertAccessHelper
    {
        private const string AdminRole = "RL-000000";

        // Exact uppercase strings that are permitted.
        // Add or remove entries here as roles change — no other code needs touching.
        private static readonly string[] AllowedRoleKeys =
        [
            "HR DIRECTOR",
             "HR SUPERVISOR",
             "HR ASISTANT 1"
        ];

        public static bool CanPrint(string? roleCode)
        {
            if (string.IsNullOrWhiteSpace(roleCode))
                return false;

            if (roleCode == AdminRole)
                return true;

            var upper = roleCode.Trim().ToUpperInvariant();

            // Must match at least one allowed key exactly as a substring.
            // "HR ASSISTANT 1" matches → allowed.
            // "HR ASSISTANT 2" does NOT match any key → blocked.
            return AllowedRoleKeys.Any(key => upper.Contains(key));
        }
    }
}
