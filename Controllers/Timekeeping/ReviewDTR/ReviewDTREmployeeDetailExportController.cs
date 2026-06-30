using Dapper;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.ReviewDTR
{
    public class ReviewDTREmployeeDetailExportController : Controller
    {
        private readonly ReviewDTRService _service;
        private readonly IDbConnection _db;

        // -------------------------------------------------------
        // Column definitions — order preserved in Excel
        // Each ColGroup has a display label and its sub-columns
        // -------------------------------------------------------
        private static readonly List<DetailColumnGroup> AllColumnGroups = new()
        {
            new DetailColumnGroup("Schedule", "Schedule", new[]
            {
                new DetailColDef("Sched In",  "scheduleTimeIn",  ColType.DateTime),
                new DetailColDef("Sched Out", "scheduleTimeOut", ColType.DateTime),
            }),
            new DetailColumnGroup("ActualTime", "Actual Time", new[]
            {
                new DetailColDef("Time In",  "biometricsDateIn",  ColType.DateTime),
                new DetailColDef("Time Out", "biometricsDateOut", ColType.DateTime),
            }),
            new DetailColumnGroup("Attendance", "Attendance", new[]
            {
                new DetailColDef("Remarks",           "remarks",                    ColType.Text,     isNumeric: false),
                new DetailColDef("Late (Min)",        "lateHoursFormatted",         ColType.Text,     isNumeric: true),
                new DetailColDef("Undertime (Min)",   "underTimeHoursFormatted",    ColType.Text,     isNumeric: true),
            }),
            new DetailColumnGroup("NightDifferential", "Night Differential", new[]
            {
                new DetailColDef("ND (Hours)", "ndHoursFormatted", ColType.Text, isNumeric: true),
            }),
            new DetailColumnGroup("Overtime", "Overtime", new[]
            {
                new DetailColDef("OT In",         "overtimeDateTimeIn",    ColType.DateTime),
                new DetailColDef("OT Out",        "overTimeDateTimeOUT",   ColType.DateTime),
                new DetailColDef("OT Reason",     "otReason",              ColType.Text,     isNumeric: false),
                new DetailColDef("OT (Hours)",    "otHoursFormatted",      ColType.Text,     isNumeric: true),
            }),
            new DetailColumnGroup("RestDay", "Rest Day", new[]
            {
                new DetailColDef("RD (Hours)",       "rdHoursFormatted",    ColType.Text, isNumeric: true),
                new DetailColDef("RD OT (Hours)",    "rdotHoursFormatted",  ColType.Text, isNumeric: true),
                new DetailColDef("RD ND OT (Hours)", "rdndotHoursFormatted",ColType.Text, isNumeric: true),
            }),
            new DetailColumnGroup("SpecialHoliday", "Special Holiday", new[]
            {
                new DetailColDef("Holiday Name",       "holidayName",                  ColType.Text, isNumeric: false),
                new DetailColDef("SPL (Hours)",        "splHolidayHoursFormatted",     ColType.Text, isNumeric: true),
                new DetailColDef("SPL OT (Hours)",     "splHolidayOTHoursFormatted",   ColType.Text, isNumeric: true),
                new DetailColDef("SPL ND (Hours)",     "splHolidayNDHoursFormatted",   ColType.Text, isNumeric: true),
                new DetailColDef("SPL ND OT (Hours)",  "splHolidayNDOTHoursFormatted", ColType.Text, isNumeric: true),
            }),
            new DetailColumnGroup("SpecialHolidayRestDay", "Special Holiday Rest Day", new[]
            {
                new DetailColDef("SPL RD (Hours)",       "splHolidayRESTHoursFormatted",      ColType.Text, isNumeric: true),
                new DetailColDef("SPL RD OT (Hours)",    "splHolidayRESTOTHoursFormatted",    ColType.Text, isNumeric: true),
                new DetailColDef("SPL RD ND (Hours)",    "splHolidayRESTNDHoursFormatted",    ColType.Text, isNumeric: true),
                new DetailColDef("SPL RD ND OT (Hours)", "splHolidayRESTNDOTHoursFormatted",  ColType.Text, isNumeric: true),
            }),
            new DetailColumnGroup("RegularHoliday", "Regular Holiday", new[]
            {
                new DetailColDef("REG (Hours)",       "regHolidayHoursFormatted",    ColType.Text, isNumeric: true),
                new DetailColDef("REG OT (Hours)",    "regHolidayOTHoursFormatted",  ColType.Text, isNumeric: true),
                new DetailColDef("REG ND (Hours)",    "regHolidayNDHoursFormatted",  ColType.Text, isNumeric: true),
                new DetailColDef("REG ND OT (Hours)", "regHolidayNDOTHoursFormatted",ColType.Text, isNumeric: true),
            }),
            new DetailColumnGroup("RegularHolidayRestDay", "Regular Holiday Rest Day", new[]
            {
                new DetailColDef("REG RD (Hours)",       "regHolidayRESTHoursFormatted",      ColType.Text, isNumeric: true),
                new DetailColDef("REG RD OT (Hours)",    "regHolidayRESTOTHoursFormatted",    ColType.Text, isNumeric: true),
                new DetailColDef("REG RD ND (Hours)",    "regHolidayRESTNDHoursFormatted",    ColType.Text, isNumeric: true),
                new DetailColDef("REG RD ND OT (Hours)", "regHolidayRESTNDOTHoursFormatted",  ColType.Text, isNumeric: true),
            }),
            new DetailColumnGroup("LeaveOther", "Leave & Other", new[]
            {
                new DetailColDef("Leave Type",   "leaveName",   ColType.Text, isNumeric: false),
                new DetailColDef("Leave Reason", "leaveReason", ColType.Text, isNumeric: false),
                new DetailColDef("OB Reason",    "obReason",    ColType.Text, isNumeric: false),
                new DetailColDef("WFH Reason",   "wfhReason",   ColType.Text, isNumeric: false),
            }),
        };

        public ReviewDTREmployeeDetailExportController(ReviewDTRService service, IDbConnection db)
        {
            _service = service;
            _db = db;
        }

        // -------------------------------------------------------
        // GET /ReviewDTREmployeeDetailExport/ExportToExcel
        // -------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> ExportToExcel(
            string? employeeNo,
            string? employeeName,
            string? dateFrom,
            string? dateTo,
            string? branchCode)
        {
            try
            {
                var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(sessionEmployeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                var exporterInfo = GetEmployeeInfo(sessionEmployeeNo);

                if (string.IsNullOrWhiteSpace(dateFrom) || string.IsNullOrWhiteSpace(dateTo))
                    return BadRequest(new { success = false, message = "Date range is required" });

                if (!DateTime.TryParse(dateFrom, out DateTime parsedDateFrom) ||
                    !DateTime.TryParse(dateTo, out DateTime parsedDateTo))
                    return BadRequest(new { success = false, message = "Invalid date format" });

                if (string.IsNullOrWhiteSpace(employeeNo))
                    return BadRequest(new { success = false, message = "Employee number is required" });

                branchCode = branchCode == "ALL" || string.IsNullOrWhiteSpace(branchCode) ? "" : branchCode;

                var rows = await _service.GetDailyRowsAsync(
                    parsedDateFrom, parsedDateTo, branchCode, employeeNo);

                if (rows.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                // Resolve display name — prefer passed-in value, fall back to DB lookup
                var displayName = !string.IsNullOrWhiteSpace(employeeName)
                    ? employeeName
                    : GetEmployeeDisplayName(employeeNo);

                var excelFile = GenerateExcelFile(rows, employeeNo, displayName, exporterInfo,
                    parsedDateFrom, parsedDateTo);

                var safeName = (displayName ?? employeeNo)
                    .Replace(",", "").Replace(" ", "_").Replace(".", "");
                var fileName =
                    $"EmployeeDTR_{employeeNo}_{safeName}_{parsedDateFrom:yyyyMMdd}_{parsedDateTo:yyyyMMdd}.xlsx";

                return File(
                    excelFile,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Employee Detail Export error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // -------------------------------------------------------
        // PRIVATE HELPERS
        // -------------------------------------------------------

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

        private string GetEmployeeDisplayName(string employeeNo)
        {
            var query = @"
                SELECT CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM e_basicinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1";
            var result = _db.QueryFirstOrDefault<dynamic>(query, new { employeeNo });
            return result?.employeeName ?? employeeNo;
        }

        // -------------------------------------------------------
        // EXCEL GENERATION
        // -------------------------------------------------------

        private byte[] GenerateExcelFile(
            List<KEISAN_HRIS_v2.Models.Timekeeping.ReviewDTRViewModel> rows,
            string employeeNo,
            string employeeName,
            (string EmployeeNo, string EmployeeName) exporterInfo,
            DateTime dateFrom,
            DateTime dateTo)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Employee DTR");

            // Fixed leading columns (Date, Weekday) — always present
            var leadingCols = new[]
            {
                new DetailColDef("Date",    "workDate",    ColType.Date),
                new DetailColDef("Weekday", "weekDayName", ColType.Text),
            };

            // Flatten all sub-columns from every group
            var groupCols = AllColumnGroups.SelectMany(g => g.Columns).ToList();

            int totalCols = leadingCols.Length + groupCols.Count;
            int rowCount = rows.Count;

            // ------ Row 1: Main Title ------
            ws.Cells[1, 1].Value = "Employee Daily Time Record";
            ws.Cells[1, 1, 1, totalCols].Merge = true;
            ws.Cells[1, 1].Style.Font.Name = "Calibri";
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ------ Row 2: Employee Info ------
            var empInfo = $"Employee: ({employeeNo}) {employeeName}     " +
                          $"Period: {dateFrom:MM/dd/yyyy} - {dateTo:MM/dd/yyyy}";
            ws.Cells[2, 1].Value = empInfo;
            ws.Cells[2, 1, 2, totalCols].Merge = true;
            ws.Cells[2, 1].Style.Font.Name = "Calibri";
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // ------ Row 3: Generated By ------
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({exporterInfo.EmployeeNo}) {exporterInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[3, 1].Value = exportInfo;
            ws.Cells[3, 1, 3, totalCols].Merge = true;
            ws.Cells[3, 1].Style.Font.Name = "Calibri";
            ws.Cells[3, 1].Style.Font.Size = 10;
            ws.Cells[3, 1].Style.Font.Italic = true;
            ws.Cells[3, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

            // Row 4: blank spacer

            // ------ Rows 5–6: Headers ------

            // Explicit row heights — makes the two-row header tall and readable
            ws.Row(5).Height = 28; // group label row
            ws.Row(6).Height = 32; // sub-column label row (slightly taller for wrap)

            // Leading columns span both header rows
            for (int i = 0; i < leadingCols.Length; i++)
            {
                int col = i + 1;
                ws.Cells[5, col, 6, col].Merge = true;
                ws.Cells[5, col].Value = leadingCols[i].Header;
                StyleHeader(ws.Cells[5, col, 6, col]);
            }

            // Group headers (row 5) + sub-column headers (row 6)
            int currentCol = leadingCols.Length + 1;

            foreach (var group in AllColumnGroups)
            {
                int groupStart = currentCol;
                int groupEnd = currentCol + group.Columns.Length - 1;

                ws.Cells[5, groupStart, 5, groupEnd].Merge = true;
                ws.Cells[5, groupStart].Value = group.Label;
                StyleHeader(ws.Cells[5, groupStart, 5, groupEnd]);

                foreach (var col in group.Columns)
                {
                    ws.Cells[6, currentCol].Value = col.Header;
                    StyleSubHeader(ws.Cells[6, currentCol]);
                    currentCol++;
                }
            }

            // ------ Data rows (start at row 7) ------
            ws.DefaultRowHeight = 18; // slightly taller than Excel default for readability
            for (int r = 0; r < rowCount; r++)
            {
                var row = rows[r];
                var rowMap = BuildRowMap(row);
                int excelRow = r + 7;
                int dataCol = 1;

                // Leading columns
                foreach (var col in leadingCols)
                {
                    SetCell(ws.Cells[excelRow, dataCol], rowMap, col);
                    dataCol++;
                }

                // Group columns
                foreach (var col in groupCols)
                {
                    SetCell(ws.Cells[excelRow, dataCol], rowMap, col);
                    dataCol++;
                }

                // Zebra striping for readability
                if (r % 2 == 1)
                {
                    ws.Cells[excelRow, 1, excelRow, totalCols].Style.Fill.PatternType =
                        ExcelFillStyle.Solid;
                    ws.Cells[excelRow, 1, excelRow, totalCols].Style.Fill.BackgroundColor
                        .SetColor(System.Drawing.Color.FromArgb(248, 246, 255));
                }
            }

            // ------ Auto-fit columns ------
            ws.Cells[ws.Dimension.Address].AutoFitColumns(8, 40);

            // ------ Borders for header + data rows ------
            int lastRow = rowCount + 6;
            for (int r = 5; r <= lastRow; r++)
            {
                for (int c = 1; c <= totalCols; c++)
                {
                    var cell = ws.Cells[r, c];
                    cell.Style.Font.Name = "Calibri";
                    cell.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    cell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    cell.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                }
            }

            // ------ Freeze panes: keep Date + Weekday visible ------
            ws.View.FreezePanes(7, leadingCols.Length + 1);

            return package.GetAsByteArray();
        }

        // -------------------------------------------------------
        // Build a flat string-keyed dictionary from a DTR row
        // -------------------------------------------------------
        private Dictionary<string, object?> BuildRowMap(
            KEISAN_HRIS_v2.Models.Timekeeping.ReviewDTRViewModel row)
        {
            return new Dictionary<string, object?>
            {
                ["workDate"] = row.workDate,
                ["weekDayName"] = row.weekDayName,
                ["scheduleTimeIn"] = row.scheduleTimeIn,
                ["scheduleTimeOut"] = row.scheduleTimeOut,
                ["biometricsDateIn"] = row.biometricsDateIn,
                ["biometricsDateOut"] = row.biometricsDateOut,
                ["remarks"] = row.remarks,
                ["lateHoursFormatted"] = row.LateMinutesFormatted,
                ["underTimeHoursFormatted"] = row.UnderTimeMinutesFormatted,
                ["ndHoursFormatted"] = row.NDHoursFormatted,
                ["overtimeDateTimeIn"] = row.OvertimeDateTimeIn,
                ["overTimeDateTimeOUT"] = row.OverTimeDateTimeOUT,
                ["otReason"] = row.OTReason,
                ["otHoursFormatted"] = row.OTHoursFormatted,
                ["rdHoursFormatted"] = row.RDHoursFormatted,
                ["rdotHoursFormatted"] = row.RDOTHoursFormatted,
                ["holidayName"] = row.holidayName,
                ["splHolidayHoursFormatted"] = row.SPLHolidayHoursFormatted,
                ["splHolidayOTHoursFormatted"] = row.SPLHolidayOTHoursFormatted,
                ["splHolidayNDHoursFormatted"] = row.SPLHolidayNDHoursFormatted,
                ["splHolidayNDOTHoursFormatted"] = row.SPLHolidayNDOTHoursFormatted,
                ["rdndotHoursFormatted"] = row.RDNDOTHoursFormatted,
                ["regHolidayHoursFormatted"] = row.REGHolidayHoursFormatted,
                ["regHolidayOTHoursFormatted"] = row.REGHolidayOTHoursFormatted,
                ["regHolidayNDHoursFormatted"] = row.REGHolidayNDHoursFormatted,
                ["regHolidayNDOTHoursFormatted"] = row.REGHolidayNDOTHoursFormatted,
                ["splHolidayRESTHoursFormatted"] = row.SPLHolidayRESTHoursFormatted,
                ["splHolidayRESTOTHoursFormatted"] = row.SPLHolidayRESTOTHoursFormatted,
                ["splHolidayRESTNDHoursFormatted"] = row.SPLHolidayRESTNDHoursFormatted,
                ["splHolidayRESTNDOTHoursFormatted"] = row.SPLHolidayRESTNDOTHoursFormatted,
                ["regHolidayRESTHoursFormatted"] = row.REGHolidayRESTHoursFormatted,
                ["regHolidayRESTOTHoursFormatted"] = row.REGHolidayRESTOTHoursFormatted,
                ["regHolidayRESTNDHoursFormatted"] = row.REGHolidayRESTNDHoursFormatted,
                ["regHolidayRESTNDOTHoursFormatted"] = row.REGHolidayRESTNDOTHoursFormatted,
                ["leaveName"] = row.leaveName,
                ["leaveReason"] = row.leaveReason,
                ["obReason"] = row.obReason,
                ["wfhReason"] = row.wfhReason,
            };
        }

        private void SetCell(ExcelRange cell, Dictionary<string, object?> rowMap, DetailColDef col)
        {
            rowMap.TryGetValue(col.DataKey, out var raw);

            switch (col.Type)
            {
                case ColType.Date:
                    if (raw is string ds && DateTime.TryParse(ds, out var dv))
                        cell.Value = dv.ToString("MM/dd/yyyy");
                    else
                        cell.Value = raw?.ToString() ?? "";
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    break;

                case ColType.DateTime:
                    if (raw is DateTime dt)
                        cell.Value = dt.ToString("MM/dd/yyyy hh:mm tt");
                    else if (raw is string dts && DateTime.TryParse(dts, out var dtv))
                        cell.Value = dtv.ToString("MM/dd/yyyy hh:mm tt");
                    else
                        cell.Value = "";
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    break;

                case ColType.Text:
                default:
                    cell.Value = raw?.ToString() ?? "";
                    // Numeric-formatted text (hrs/mins) → right-align; plain text → left-align
                    cell.Style.HorizontalAlignment = col.IsNumeric
                        ? ExcelHorizontalAlignment.Right
                        : ExcelHorizontalAlignment.Left;
                    break;
            }

            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        // -------------------------------------------------------
        // STYLING HELPERS
        // -------------------------------------------------------

        private void StyleHeader(ExcelRange range)
        {
            range.Style.Font.Name = "Calibri";
            range.Style.Font.Bold = true;
            range.Style.Font.Size = 10;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(
                System.Drawing.Color.FromArgb(136, 108, 192)); // #886cc0
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
            range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            range.Style.WrapText = true;
        }

        private void StyleSubHeader(ExcelRange cell)
        {
            cell.Style.Font.Name = "Calibri";
            cell.Style.Font.Bold = true;
            cell.Style.Font.Size = 9;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(
                System.Drawing.Color.FromArgb(107, 91, 149)); // #6b5b95
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.WrapText = true;
        }
    }

    // -------------------------------------------------------
    // SUPPORTING MODELS
    // -------------------------------------------------------

    public enum ColType { Text, Date, DateTime }

    public record DetailColumnGroup(string Key, string Label, DetailColDef[] Columns);

    public class DetailColDef
    {
        public string Header { get; }
        public string DataKey { get; }
        public ColType Type { get; }
        public bool IsNumeric { get; }

        public DetailColDef(string header, string dataKey,
                            ColType type = ColType.Text, bool isNumeric = false)
        {
            Header = header;
            DataKey = dataKey;
            Type = type;
            IsNumeric = isNumeric;
        }
    }
}