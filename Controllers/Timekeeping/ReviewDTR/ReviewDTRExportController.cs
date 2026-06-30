using KEISAN_HRIS_v2.Helpers;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Dapper;
using System.Data;
using KEISAN_HRIS_v2.Services.TimeKeeping;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.ReviewDTR
{
    [ModuleAuthorize("TreviewDTRM")]
    public class ReviewDTRExportController : BaseController
    {
        private readonly ReviewDTRService _service;
        private readonly IDbConnection _db;

        // -------------------------------------------------------
        // Column group definitions — order matches the View table.
        //
        // View order:
        //   Attendance | Regular OT | Rest Day |
        //   Special Holiday | Special Holiday Rest Day |
        //   Regular Holiday | Regular Holiday Rest Day
        // -------------------------------------------------------
        private static readonly List<ColumnGroup> AllColumnGroups = new()
        {
            // ── Attendance (matches View: Late, Undertime, Present, Absent) ──
            new ColumnGroup("Attendance", "Attendance", new[]
            {
                new ColDef("Late (Minutes)",      "Total Late (Minutes)"),
                new ColDef("Undertime (Minutes)", "Total Undertime (Minutes)"),
                new ColDef("Present (Days)", "Total Present (Days)"), // allows 0.5
                new ColDef("Absent (Days)",  "Total Absent (Days)"),  // allows 0.5
            }),

            // ── Regular OT (renamed from "Night Differential" to match View) ──
            new ColumnGroup("NightDifferential", "Regular OT", new[]
            {
                new ColDef("ND (Hours)",    "ND Hours"),
                new ColDef("OT (Hours)",    "OT Hours"),
                new ColDef("OT ND (Hours)", "OT ND Hours"),
            }),

            // ── Rest Day ──────────────────────────────────────────────────────
            new ColumnGroup("RestDay", "Rest Day", new[]
            {
                new ColDef("RD (Hours)",       "RD Hours"),
                new ColDef("RD OT (Hours)",    "RD OT Hours"),
                new ColDef("RD ND (Hours)",    "RD ND Hours"),
                new ColDef("RD ND OT (Hours)", "RD ND OT Hours"),
            }),

            // ── Special Holiday ───────────────────────────────────────────────
            new ColumnGroup("SpecialHoliday", "Special Holiday", new[]
            {
                new ColDef("SPL (Hours)",       "SPL Holiday Hours"),
                new ColDef("SPL OT (Hours)",    "SPL Holiday OT Hours"),
                new ColDef("SPL ND (Hours)",    "SPL Holiday ND Hours"),
                new ColDef("SPL ND OT (Hours)", "SPL Holiday ND OT Hours"),
            }),

            // ── Special Holiday Rest Day ──────────────────────────────────────
            new ColumnGroup("SpecialHolidayRestDay", "Special Holiday Rest Day", new[]
            {
                new ColDef("SPL RD (Hours)",       "SPL Holiday REST Hours"),
                new ColDef("SPL RD OT (Hours)",    "SPL Holiday REST OT Hours"),
                new ColDef("SPL RD ND (Hours)",    "SPL Holiday REST ND Hours"),
                new ColDef("SPL RD ND OT (Hours)", "SPL Holiday REST ND OT Hours"),
            }),

            // ── Regular Holiday ───────────────────────────────────────────────
            new ColumnGroup("RegularHoliday", "Regular Holiday", new[]
            {
                new ColDef("REG (Hours)",       "REG Holiday Hours"),
                new ColDef("REG OT (Hours)",    "REG Holiday OT Hours"),
                new ColDef("REG ND (Hours)",    "REG Holiday ND Hours"),
                new ColDef("REG ND OT (Hours)", "REG Holiday ND OT Hours"),
            }),

            // ── Regular Holiday Rest Day ──────────────────────────────────────
            new ColumnGroup("RegularHolidayRestDay", "Regular Holiday Rest Day", new[]
            {
                new ColDef("REG RD (Hours)",       "REG Holiday REST Hours"),
                new ColDef("REG RD OT (Hours)",    "REG Holiday REST OT Hours"),
                new ColDef("REG RD ND (Hours)",    "REG Holiday REST ND Hours"),
                new ColDef("REG RD ND OT (Hours)", "REG Holiday REST ND OT Hours"),
            }),
        };

        public ReviewDTRExportController(ReviewDTRService service, IDbConnection db)
        {
            _service = service;
            _db = db;
        }

        // -------------------------------------------------------
        // GET /ReviewDTRExport/ExportToExcel
        //
        // excludeGroups — comma-separated group keys to omit,
        // e.g. "SpecialHoliday,RegularHoliday"
        // -------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ExportToExcel(
            string? dateFrom,
            string? dateTo,
            string? branchCode,
            int offset = 0,
            int limit = -1,
            string? sortColumn = null,
            string? sortDirection = "asc",
            string? excludeGroups = null)
        {
            try
            {
                var employeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(employeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                var employeeInfo = GetEmployeeInfo(employeeNo);

                if (string.IsNullOrWhiteSpace(dateFrom) || string.IsNullOrWhiteSpace(dateTo))
                    return BadRequest(new { success = false, message = "Date range is required" });

                if (!DateTime.TryParse(dateFrom, out DateTime parsedDateFrom) ||
                    !DateTime.TryParse(dateTo, out DateTime parsedDateTo))
                    return BadRequest(new { success = false, message = "Invalid date format" });

                // Parse excluded group keys
                var excludedKeys = ParseExcludedGroups(excludeGroups);

                // Resolve which groups to include (preserving original order)
                var includedGroups = AllColumnGroups
                    .Where(g => !excludedKeys.Contains(g.Key))
                    .ToList();

                var data = await GetReviewDTRData(
                    parsedDateFrom, parsedDateTo, branchCode,
                    offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, employeeInfo, includedGroups);
                var fileName = $"ReviewDTR_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(
                    excelFile,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // -------------------------------------------------------
        // PRIVATE HELPERS
        // -------------------------------------------------------

        private static HashSet<string> ParseExcludedGroups(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new HashSet<string>(
                raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        private (string EmployeeNo, string EmployeeName) GetEmployeeInfo(string employeeNo)
        {
            var userQuery = @"
                SELECT
                    userCode,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM s_user
                WHERE userCode = @employeeNo
                LIMIT 1";

            var userResult = _db.QueryFirstOrDefault<dynamic>(userQuery, new { employeeNo });
            if (userResult != null)
                return (userResult.userCode, userResult.employeeName);

            var empQuery = @"
                SELECT
                    employeeNo,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM e_basicinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1";

            var empResult = _db.QueryFirstOrDefault<dynamic>(empQuery, new { employeeNo });
            if (empResult != null)
                return (empResult.employeeNo, empResult.employeeName);

            return (employeeNo, "Unknown User");
        }

        private async Task<List<Dictionary<string, object>>> GetReviewDTRData(
            DateTime dateFrom, DateTime dateTo, string? branchCode,
            int offset, int limit, string? sortColumn, string? sortDirection)
        {
            branchCode = branchCode == "ALL" || string.IsNullOrWhiteSpace(branchCode) ? "" : branchCode;

            var summaryData = await _service.GetSummaryAsync(dateFrom, dateTo, branchCode, "");

            // Apply data scope — resolve allowed employees the same way ResolveScope() does
            if (RoleCode != "RL-000000")
            {
                var role = _db.QueryFirstOrDefault<dynamic>(@"
                    SELECT scopeType, allowedRanks, allowedBranches, allowedDepartments,
                           allowedPositions, allowedEmploymentStatuses, hiddenEmployees
                    FROM s_role
                    WHERE roleCode = @roleCode AND isActive = 1 LIMIT 1",
                    new { roleCode = RoleCode });

                string scopeType = (string)(role?.scopeType ?? "OWN_ONLY");

                var hiddenList = string.IsNullOrWhiteSpace((string)(role?.hiddenEmployees ?? ""))
                    ? new HashSet<string>()
                    : new HashSet<string>(((string)role.hiddenEmployees)
                        .Split(',').Select(x => x.Trim())
                        .Where(x => !string.IsNullOrEmpty(x)));

                HashSet<string>? allowedSet = null;

                switch (scopeType)
                {
                    case "ALL":
                        allowedSet = null; // no restriction
                        break;

                    case "OWN_ONLY":
                        allowedSet = new HashSet<string> { EmployeeNo };
                        break;

                    case "OWN_AND_ASSIGNED":
                        var assigned = DataScopeHelper.GetAssignedEmployeeNos(_db, EmployeeNo);
                        allowedSet = new HashSet<string>(assigned) { EmployeeNo };
                        break;

                    case "DEPARTMENT":
                        var myDept = _db.QueryFirstOrDefault<string>(
                            "SELECT departmentCode FROM e_basicinfo WHERE employeeNo = @e AND isActive = 1",
                            new { e = EmployeeNo });
                        var deptEmps = _db.Query<string>(
                            "SELECT employeeNo FROM e_basicinfo WHERE departmentCode = @d AND isActive = 1",
                            new { d = myDept ?? "" });
                        allowedSet = new HashSet<string>(deptEmps);
                        break;

                    case "BRANCH":
                        string rawBranches = (string)(role?.allowedBranches ?? "");
                        if (!string.IsNullOrWhiteSpace(rawBranches))
                        {
                            var branches = rawBranches.Split(',').Select(b => b.Trim()).ToArray();
                            var branchEmps = _db.Query<string>(
                                "SELECT employeeNo FROM e_basicinfo WHERE branchCode IN @branches AND isActive = 1",
                                new { branches });
                            allowedSet = new HashSet<string>(branchEmps);
                        }
                        break;

                    case "RANK_FILTER":
                        string rawRanks = (string)(role?.allowedRanks ?? "");
                        if (!string.IsNullOrWhiteSpace(rawRanks))
                        {
                            var ranks = rawRanks.Split(',').Select(r => r.Trim()).ToArray();
                            var rankEmps = _db.Query<string>(
                                "SELECT employeeNo FROM e_basicinfo WHERE rankCode IN @ranks AND isActive = 1",
                                new { ranks });
                            allowedSet = new HashSet<string>(rankEmps);
                        }
                        break;

                    case "POSITION_FILTER":
                        string rawPositions = (string)(role?.allowedPositions ?? "");
                        if (!string.IsNullOrWhiteSpace(rawPositions))
                        {
                            var positions = rawPositions.Split(',').Select(p => p.Trim()).ToArray();
                            var posEmps = _db.Query<string>(
                                "SELECT employeeNo FROM e_basicinfo WHERE positionCode IN @positions AND isActive = 1",
                                new { positions });
                            allowedSet = new HashSet<string>(posEmps);
                        }
                        break;

                    case "EMPLOYMENT_STATUS":
                        string rawStatuses = (string)(role?.allowedEmploymentStatuses ?? "");
                        if (!string.IsNullOrWhiteSpace(rawStatuses))
                        {
                            var statuses = rawStatuses.Split(',').Select(s => s.Trim()).ToArray();
                            var statusEmps = _db.Query<string>(
                                "SELECT employeeNo FROM e_basicinfo WHERE employmentStatus IN @statuses AND isActive = 1",
                                new { statuses });
                            allowedSet = new HashSet<string>(statusEmps);
                        }
                        break;

                    case "CUSTOM":
                        var customConditions = new List<string>();
                        var customParams = new DynamicParameters();
                        string cRanks = (string)(role?.allowedRanks ?? "");
                        string cBranches = (string)(role?.allowedBranches ?? "");
                        string cDepts = (string)(role?.allowedDepartments ?? "");
                        string cPositions = (string)(role?.allowedPositions ?? "");
                        string cStatuses = (string)(role?.allowedEmploymentStatuses ?? "");
                        if (!string.IsNullOrWhiteSpace(cRanks)) { customConditions.Add("rankCode IN @cRanks"); customParams.Add("@cRanks", cRanks.Split(',').Select(x => x.Trim()).ToArray()); }
                        if (!string.IsNullOrWhiteSpace(cBranches)) { customConditions.Add("branchCode IN @cBranches"); customParams.Add("@cBranches", cBranches.Split(',').Select(x => x.Trim()).ToArray()); }
                        if (!string.IsNullOrWhiteSpace(cDepts)) { customConditions.Add("departmentCode IN @cDepts"); customParams.Add("@cDepts", cDepts.Split(',').Select(x => x.Trim()).ToArray()); }
                        if (!string.IsNullOrWhiteSpace(cPositions)) { customConditions.Add("positionCode IN @cPositions"); customParams.Add("@cPositions", cPositions.Split(',').Select(x => x.Trim()).ToArray()); }
                        if (!string.IsNullOrWhiteSpace(cStatuses)) { customConditions.Add("employmentStatus IN @cStatuses"); customParams.Add("@cStatuses", cStatuses.Split(',').Select(x => x.Trim()).ToArray()); }
                        if (customConditions.Any())
                        {
                            var customEmps = _db.Query<string>(
                                $"SELECT employeeNo FROM e_basicinfo WHERE isActive = 1 AND ({string.Join(" OR ", customConditions)})",
                                customParams);
                            allowedSet = new HashSet<string>(customEmps);
                        }
                        break;

                    default:
                        allowedSet = new HashSet<string> { EmployeeNo };
                        break;
                }

                // Apply hidden employees exclusion (always show own record)
                summaryData = summaryData
                    .Where(d => (allowedSet == null || allowedSet.Contains(d.EmployeeNo))
                        && (!hiddenList.Contains(d.EmployeeNo) || d.EmployeeNo == EmployeeNo))
                    .ToList();
            }

            var dataList = summaryData.Select(row => new Dictionary<string, object>
            {
                // ── Fixed columns ──────────────────────────────────────────────
                { "Employee No",   row.EmployeeNo  ?? "" },
                { "Employee Name", row.FullName    ?? "" },
                { "Payroll Type",  row.PayrollType ?? "" },

                // ── Attendance (now first, matching View order) ────────────────
                { "Total Late (Minutes)",      Math.Round(row.TotalLateMinutes,      2) },
                { "Total Undertime (Minutes)", Math.Round(row.TotalUndertimeMinutes, 2) },
                { "Total Present (Days)",      row.TotalPresentDays },
                { "Total Absent (Days)",       row.TotalAbsentDays  },   // ← added

                // ── Regular OT (renamed from "Night Differential") ────────────
                { "ND Hours",    Math.Round(row.NDHours,           2) },
                { "OT Hours",    Math.Round(row.OTHours,           2) },
                { "OT ND Hours", Math.Round(GetOTNDHours(row),     2) },

                // ── Rest Day ──────────────────────────────────────────────────
                { "RD Hours",       Math.Round(row.RDHours,      2) },
                { "RD OT Hours",    Math.Round(row.RDOTHours,    2) },
                { "RD ND Hours",    Math.Round(row.RDNDHours,    2) },
                { "RD ND OT Hours", Math.Round(row.RDNDOTHours,  2) },

                // ── Special Holiday ───────────────────────────────────────────
                { "SPL Holiday Hours",        Math.Round(row.SPLHolidayHours,      2) },
                { "SPL Holiday OT Hours",     Math.Round(row.SPLHolidayOTHours,    2) },
                { "SPL Holiday ND Hours",     Math.Round(row.SPLHolidayNDHours,    2) },
                { "SPL Holiday ND OT Hours",  Math.Round(row.SPLHolidayNDOTHours,  2) },

                // ── Special Holiday Rest Day ──────────────────────────────────
                { "SPL Holiday REST Hours",       Math.Round(row.SPLHolidayRESTHours,      2) },
                { "SPL Holiday REST OT Hours",    Math.Round(row.SPLHolidayRESTOTHours,    2) },
                { "SPL Holiday REST ND Hours",    Math.Round(row.SPLHolidayRESTNDHours,    2) },
                { "SPL Holiday REST ND OT Hours", Math.Round(row.SPLHolidayRESTNDOTHours,  2) },

                // ── Regular Holiday ───────────────────────────────────────────
                { "REG Holiday Hours",        Math.Round(row.REGHolidayHours,      2) },
                { "REG Holiday OT Hours",     Math.Round(row.REGHolidayOTHours,    2) },
                { "REG Holiday ND Hours",     Math.Round(row.REGHolidayNDHours,    2) },
                { "REG Holiday ND OT Hours",  Math.Round(row.REGHolidayNDOTHours,  2) },

                // ── Regular Holiday Rest Day ──────────────────────────────────
                { "REG Holiday REST Hours",       Math.Round(row.REGHolidayRESTHours,      2) },
                { "REG Holiday REST OT Hours",    Math.Round(row.REGHolidayRESTOTHours,    2) },
                { "REG Holiday REST ND Hours",    Math.Round(row.REGHolidayRESTNDHours,    2) },
                { "REG Holiday REST ND OT Hours", Math.Round(row.REGHolidayRESTNDOTHours,  2) },
            }).ToList();

            // ── Sorting ───────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(sortColumn))
            {
                var columnKey = GetColumnKey(sortColumn);
                if (!string.IsNullOrEmpty(columnKey))
                {
                    bool isDescending = sortDirection?.ToUpper() == "DESC";
                    dataList = isDescending
                        ? dataList.OrderByDescending(x => x[columnKey]).ToList()
                        : dataList.OrderBy(x => x[columnKey]).ToList();
                }
            }

            // ── Pagination ────────────────────────────────────────────────────
            if (limit > 0 && offset >= 0)
                dataList = dataList.Skip(offset).Take(limit).ToList();

            return dataList;
        }

        // OT ND Hours — pulled from the summary model property.
        // If OTNDHours is not yet aggregated in GetSummaryAsync(), add it there.
        private static double GetOTNDHours(
            KEISAN_HRIS_v2.Models.Timekeeping.ReviewDTREmployeeSummaryViewModel row)
            => row.OTNDHours;

        private string GetColumnKey(string sortColumn)
        {
            return sortColumn?.ToLower() switch
            {
                "employeeno" => "Employee No",
                "fullname" => "Employee Name",
                "payrolltype" => "Payroll Type",
                // Attendance
                "totallateminutes" => "Total Late (Minutes)",
                "totalundertimeminutes" => "Total Undertime (Minutes)",
                "totalpresentdays" => "Total Present (Days)",
                "totalabsentdays" => "Total Absent (Days)",        // ← added
                // Regular OT
                "ndhours" => "ND Hours",
                "othours" => "OT Hours",
                "otndhours" => "OT ND Hours",
                // Rest Day
                "rdhours" => "RD Hours",
                "rdothours" => "RD OT Hours",
                "rdndhours" => "RD ND Hours",
                // Special Holiday
                "splholidayhours" => "SPL Holiday Hours",
                "splholidayothours" => "SPL Holiday OT Hours",
                "splholidayndhours" => "SPL Holiday ND Hours",
                // Special Holiday Rest Day
                "splholidayresthours" => "SPL Holiday REST Hours",
                "splholidayrestothours" => "SPL Holiday REST OT Hours",
                "splholidayrestndhours" => "SPL Holiday REST ND Hours",
                "splholidayrestndothours" => "SPL Holiday REST ND OT Hours",
                // Regular Holiday
                "regholidayhours" => "REG Holiday Hours",
                "regholidayothours" => "REG Holiday OT Hours",
                "regholidayndhours" => "REG Holiday ND Hours",
                // Regular Holiday Rest Day
                "regholidayresthours" => "REG Holiday REST Hours",
                "regholidayrestothours" => "REG Holiday REST OT Hours",
                "regholidayrestndhours" => "REG Holiday REST ND Hours",
                "regholidayrestndothours" => "REG Holiday REST ND OT Hours",
                _ => ""
            };
        }

        // -------------------------------------------------------
        // EXCEL GENERATION — fully dynamic based on includedGroups
        // -------------------------------------------------------
        private byte[] GenerateExcelFile(
            List<Dictionary<string, object>> data,
            (string EmployeeNo, string EmployeeName) employeeInfo,
            List<ColumnGroup> includedGroups)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Review DTR");

            if (data.Count == 0) return package.GetAsByteArray();

            // Fixed columns always present
            var fixedCols = new[]
            {
                new ColDef("Employee No",  "Employee No"),
                new ColDef("Full Name",    "Employee Name"),
                new ColDef("Payroll Type", "Payroll Type"),
            };

            // Flatten all included sub-columns in order
            var dynamicCols = includedGroups.SelectMany(g => g.Columns).ToList();

            int totalCols = fixedCols.Length + dynamicCols.Count;
            int rowCount = data.Count;

            // ------ Row 1: Main Title ------
            ws.Cells[1, 1].Value = "Review Daily Time Record";
            ws.Cells[1, 1, 1, totalCols].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ------ Row 2: Export Info ------
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({employeeInfo.EmployeeNo}) {employeeInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, totalCols].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // Row 3: blank spacer

            // ------ Rows 4–5: Headers ------

            // Fixed columns span both header rows
            for (int i = 0; i < fixedCols.Length; i++)
            {
                int col = i + 1;
                ws.Cells[4, col, 5, col].Merge = true;
                ws.Cells[4, col].Value = fixedCols[i].Header;
                StyleHeader(ws.Cells[4, col, 5, col]);
            }

            // Dynamic group headers (row 4) + sub-headers (row 5)
            int currentCol = fixedCols.Length + 1;

            foreach (var group in includedGroups)
            {
                int groupStart = currentCol;
                int groupEnd = currentCol + group.Columns.Length - 1;

                // Group header (row 4) — spans all sub-columns in the group
                ws.Cells[4, groupStart, 4, groupEnd].Merge = true;
                ws.Cells[4, groupStart].Value = group.Label;
                StyleHeader(ws.Cells[4, groupStart, 4, groupEnd]);

                // Sub-column headers (row 5)
                foreach (var col in group.Columns)
                {
                    ws.Cells[5, currentCol].Value = col.Header;
                    StyleSubHeader(ws.Cells[5, currentCol]);
                    currentCol++;
                }
            }

            // ------ Data rows (start at row 6) ------
            for (int row = 0; row < rowCount; row++)
            {
                var rowData = data[row];
                int dataCol = 1;

                // Fixed columns
                foreach (var col in fixedCols)
                {
                    ws.Cells[row + 6, dataCol].Value = rowData[col.DataKey]?.ToString() ?? "";
                    dataCol++;
                }

                // Dynamic columns
                foreach (var col in dynamicCols)
                {
                    SetNumericCell(ws.Cells[row + 6, dataCol], rowData[col.DataKey], col.IsWholeNumber);
                    dataCol++;
                }
            }

            // Auto-fit
            ws.Cells[ws.Dimension.Address].AutoFitColumns();

            // Borders for the entire table (rows 4 → last data row, all columns)
            int lastRow = rowCount + 5;
            for (int r = 4; r <= lastRow; r++)
            {
                for (int c = 1; c <= totalCols; c++)
                {
                    var cell = ws.Cells[r, c];
                    cell.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    cell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    cell.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                }
            }

            return package.GetAsByteArray();
        }

        // -------------------------------------------------------
        // STYLING HELPERS
        // -------------------------------------------------------

        private void SetNumericCell(ExcelRange cell, object value, bool isWholeNumber = false)
        {
            if (value != null && double.TryParse(value.ToString(), out double numValue))
            {
                cell.Value = isWholeNumber
                    ? numValue.ToString("0")
                    : FormatHoursOrMinutes(cell, numValue);
            }
            else
            {
                cell.Value = isWholeNumber ? "0" : "0 hrs";
            }

            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        }

        private string FormatHoursOrMinutes(ExcelRange cell, double value)
        {
            var ws = cell.Worksheet;
            var headerCell = ws.Cells[5, cell.Start.Column];
            var header = headerCell.Value?.ToString() ?? "";

            bool isMinutes = header.Contains("Minutes", StringComparison.OrdinalIgnoreCase);
            bool isDays = header.Contains("Days", StringComparison.OrdinalIgnoreCase);

            if (isDays)
            {
                if (value == 0) return "0 days";
                if (value == 0.5) return "0.5 days";
                return value == 1 ? "1 day" : $"{value} days";
            }

            if (isMinutes)
            {
                if (value == 0) return "0 min";
                int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                return rounded == 1 ? "1 min" : $"{rounded} mins";
            }
            else
            {
                if (value == 0) return "0 hrs";
                int wholeHours = (int)value;
                int minutes = (int)Math.Round((value - wholeHours) * 60, MidpointRounding.AwayFromZero);

                if (minutes == 60) { wholeHours++; minutes = 0; }

                if (wholeHours == 0)
                    return minutes == 1 ? "1 min" : $"{minutes} mins";
                else if (minutes == 0)
                    return wholeHours == 1 ? "1 hr" : $"{wholeHours} hrs";
                else
                {
                    string hrPart = wholeHours == 1 ? "1 hr" : $"{wholeHours} hrs";
                    string minPart = minutes == 1 ? "1 min" : $"{minutes} mins";
                    return $"{hrPart} {minPart}";
                }
            }
        }

        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(136, 108, 192)); // #886cc0
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.WrapText = true;
        }

        private void StyleSubHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(107, 91, 149)); // #6b5b95
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.WrapText = true;
        }
    }

    // -------------------------------------------------------
    // COLUMN GROUP MODELS
    // -------------------------------------------------------

    /// <summary>Represents a logical group of columns (e.g. "Special Holiday").</summary>
    public record ColumnGroup(string Key, string Label, ColDef[] Columns);

    /// <summary>A single column definition — header label, data dictionary key, and formatting flag.</summary>
    public class ColDef
    {
        public string Header { get; }
        public string DataKey { get; }
        public bool IsWholeNumber { get; }

        public ColDef(string header, string dataKey, bool isWholeNumber = false)
        {
            Header = header;
            DataKey = dataKey;
            IsWholeNumber = isWholeNumber;
        }
    }
}