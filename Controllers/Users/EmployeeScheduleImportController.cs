using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.DataValidation;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    public class EmployeeScheduleImportController : Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public EmployeeScheduleImportController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Users/EmployeeScheduleImport.cshtml");
        }

        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            try
            {
                var excelFile = GenerateTemplateFile();
                var fileName = $"EmployeeSchedule_Template_{DateTime.Now:yyyyMMdd}.xlsx";
                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Template generation failed: {ex.Message}" });
            }
        }

        [HttpPost]
        public IActionResult ValidateImport(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { success = false, message = "Please upload a valid Excel file" });

                if (!IsValidExcelFile(file))
                    return BadRequest(new { success = false, message = "Invalid file format. Please upload .xlsx file" });

                var validationResult = ValidateExcelData(file);

                return Json(new
                {
                    success = validationResult.IsValid,
                    message = validationResult.Message,
                    totalRows = validationResult.TotalRows,
                    validRows = validationResult.ValidRows,
                    distinctEmployeeCount = validationResult.DistinctEmployeeCount,
                    errors = validationResult.Errors,
                    warnings = validationResult.Warnings,
                    data = validationResult.Data
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Validation failed: {ex.Message}" });
            }
        }

        [HttpPost]
        public IActionResult ImportData([FromBody] List<ImportEmployeeScheduleModel> schedules)
        {
            try
            {
                if (schedules == null || !schedules.Any())
                    return BadRequest(new { success = false, message = "No data to import" });

                var results = ProcessImportData(schedules);

                return Json(new
                {
                    success = results.SuccessCount > 0,
                    message = $"Import completed: {results.SuccessCount} succeeded, {results.FailureCount} failed",
                    successCount = results.SuccessCount,
                    failureCount = results.FailureCount,
                    skippedCount = results.SkippedCount,
                    errors = results.Errors
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Import failed: {ex.Message}" });
            }
        }

        // ── Template Generation ────────────────────────────────────────────────────
        private byte[] GenerateTemplateFile()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Employee Schedule");

            // ── Hidden reference sheet for employee dropdown ───────────────────────
            var refSheet = package.Workbook.Worksheets.Add("_EmployeeList");
            refSheet.Hidden = eWorkSheetHidden.Hidden;

            var employees = GetEmployeeListForTemplate();
            for (int i = 0; i < employees.Count; i++)
                refSheet.Cells[i + 1, 1].Value = $"{employees[i].EmployeeNo} | {employees[i].EmployeeName}";

            var empListRange = refSheet.Cells[1, 1, employees.Count == 0 ? 1 : employees.Count, 1];
            package.Workbook.Names.Add("EmployeeList", empListRange);

            // ── Hidden reference sheet for schedule type dropdown ─────────────────
            var schedSheet = package.Workbook.Worksheets.Add("_ScheduleTypeList");
            schedSheet.Hidden = eWorkSheetHidden.Hidden;

            var scheduleTypes = GetScheduleTypeListForTemplate();
            for (int i = 0; i < scheduleTypes.Count; i++)
                schedSheet.Cells[i + 1, 1].Value = $"{scheduleTypes[i].Code} | {scheduleTypes[i].Name}";

            var schedListRange = schedSheet.Cells[1, 1, scheduleTypes.Count == 0 ? 1 : scheduleTypes.Count, 1];
            package.Workbook.Names.Add("ScheduleTypeList", schedListRange);

            // Pre-resolve example row values — used in Row 5 (working) and Row 6 (rest day)
            var nonRest = scheduleTypes.FirstOrDefault(s => !s.Name.ToUpper().Contains("REST"));
            var restType = scheduleTypes.FirstOrDefault(s => s.Name.ToUpper().Contains("REST"));

            // ── Hidden reference sheet for time dropdown (30-min intervals, 12hr format) ──
            // Generates: 12:00 AM, 12:30 AM, 01:00 AM ... 11:30 PM (48 entries)
            var timeSheet = package.Workbook.Worksheets.Add("_TimeList");
            timeSheet.Hidden = eWorkSheetHidden.Hidden;

            var timeSlots = new List<string>();
            for (int h = 0; h < 24; h++)
            {
                foreach (int m in new[] { 0, 30 })
                {
                    var dt = new DateTime(2000, 1, 1, h, m, 0);
                    timeSlots.Add(dt.ToString("hh:mm tt")); // e.g. "08:00 AM", "11:30 PM"
                }
            }
            for (int i = 0; i < timeSlots.Count; i++)
                timeSheet.Cells[i + 1, 1].Value = timeSlots[i];

            var timeListRange = timeSheet.Cells[1, 1, timeSlots.Count, 1];
            package.Workbook.Names.Add("TimeList", timeListRange);

            // ── Main sheet layout ─────────────────────────────────────────────────

            // Row 1 – Title
            ws.Cells[1, 1].Value = "Employee Schedule Import Template";
            ws.Cells[1, 1, 1, 8].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.Font.Name = "Arial";
            ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            ws.Row(1).Height = 28;

            // Row 2 – Instructions
            ws.Cells[2, 1].Value = "Instructions: Fill in the required fields below. Fields marked with * are mandatory. Weekday is auto-derived from Effectivity Date.";
            ws.Cells[2, 1, 2, 8].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 10;
            ws.Cells[2, 1].Style.Font.Italic = true;
            ws.Cells[2, 1].Style.Font.Name = "Arial";
            ws.Row(2).Height = 18;

            // Row 3 – blank spacer
            ws.Row(3).Height = 8;

            // Row 4 – Headers
            // COLUMNS: 1=EmployeeNo, 2=EffectivityDate, 3=ScheduleTypeCode,
            //          4=TimeIn, 5=TimeOut, 6=RenderHours, 7=Breaktime, 8=IsRestDay
            var headers = new[]
            {
                "Employee No*",
                "Effectivity Date*",
                "Schedule Type Code*",
                "Time-In (hh:MM AM/PM)",
                "Time-Out (hh:MM AM/PM)",
                "Render Hours",
                "Breaktime (mins)",
                "Is Rest Day? (YES/NO)"
            };

            var headerColor = System.Drawing.Color.FromArgb(68, 114, 196);
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cells[4, i + 1];
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Name = "Arial";
                cell.Style.Font.Size = 10;
                cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(headerColor);
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = OfficeOpenXml.Style.ExcelVerticalAlignment.Center;
            }
            ws.Row(4).Height = 20;

            // ── Row 5: Example — regular working schedule ─────────────────────────
            // Values are set BEFORE the formula loop so the loop's .Formula assignments
            // are the last write on each cell — EPPlus will not overwrite them.
            ws.Cells[5, 1].Value = employees.Count > 0 ? $"{employees[0].EmployeeNo} | {employees[0].EmployeeName}" : "EMP001 | Dela Cruz, Juan";
            ws.Cells[5, 2].Value = DateTime.Today.ToString("yyyy-MM-dd");
            ws.Cells[5, 3].Value = nonRest != default ? $"{nonRest.Code} | {nonRest.Name}" : "REG | Regular";
            ws.Cells[5, 4].Value = new DateTime(2000, 1, 1, 8, 0, 0);   // 08:00 AM
            ws.Cells[5, 5].Value = new DateTime(2000, 1, 1, 17, 0, 0);  // 05:00 PM

            // ── Row 6: Example — rest day ─────────────────────────────────────────
            ws.Cells[6, 1].Value = employees.Count > 0 ? $"{employees[0].EmployeeNo} | {employees[0].EmployeeName}" : "EMP001 | Dela Cruz, Juan";
            ws.Cells[6, 2].Value = DateTime.Today.AddDays(6).ToString("yyyy-MM-dd");
            ws.Cells[6, 3].Value = restType != default ? $"{restType.Code} | {restType.Name}" : "REST | Rest Day";

            // ── Example rows border styling ───────────────────────────────────────
            var borderColor = System.Drawing.Color.FromArgb(189, 189, 189);
            foreach (int exRow in new[] { 5, 6 })
            {
                for (int col = 1; col <= 9; col++)
                {
                    var cell = ws.Cells[exRow, col];
                    cell.Style.Font.Name = "Arial";
                    cell.Style.Border.Top.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    cell.Style.Border.Bottom.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    cell.Style.Border.Left.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    cell.Style.Border.Right.Style = OfficeOpenXml.Style.ExcelBorderStyle.Thin;
                    cell.Style.Border.Top.Color.SetColor(borderColor);
                    cell.Style.Border.Bottom.Color.SetColor(borderColor);
                    cell.Style.Border.Left.Color.SetColor(borderColor);
                    cell.Style.Border.Right.Color.SetColor(borderColor);
                }
                ws.Row(exRow).Height = 18;
            }

            // ── Rows 5–500: Formulas applied AFTER example values are set ────────
            // Setting .Value first then .Formula means the formula is the final state
            // EPPlus writes to the cell — Excel recalculates all three auto-columns
            // (Render Hours, Breaktime, Is Rest Day) correctly on every row 5–500.
            for (int dataRow = 5; dataRow <= 500; dataRow++)
            {
                string r = dataRow.ToString();

                // Col 4 & 5: time serial format so dropdown selections display as "hh:mm AM/PM"
                ws.Cells[dataRow, 4].Style.Numberformat.Format = "hh:mm AM/PM";
                ws.Cells[dataRow, 5].Style.Numberformat.Format = "hh:mm AM/PM";

                // Col 6: Render Hours — MOD handles overnight/graveyard shifts
                ws.Cells[dataRow, 6].Formula =
                    $"=IF(OR(D{r}=\"\",E{r}=\"\"),\"\",MOD(E{r}-D{r},1)*24)";
                ws.Cells[dataRow, 6].Style.Numberformat.Format = "0.00";

                // Col 7: Breaktime — auto-defaults to 60 mins when both time columns are filled
                ws.Cells[dataRow, 7].Formula =
                    $"=IF(OR(D{r}=\"\",E{r}=\"\"),\"\",60)";

                // Col 8: Is Rest Day — YES if schedule type contains "REST", otherwise NO
                ws.Cells[dataRow, 8].Formula =
                    $"=IF(C{r}=\"\",\"\",IF(ISNUMBER(SEARCH(\"REST\",C{r})),\"YES\",\"NO\"))";
            }

            // ── Data validation: Employee No dropdown (col 1, rows 5–500) ─────────
            if (employees.Count > 0)
            {
                var empValidation = ws.DataValidations.AddListValidation(ws.Cells[5, 1, 500, 1].Address);
                empValidation.ShowErrorMessage = true;
                empValidation.ErrorTitle = "Invalid Employee";
                empValidation.Error = "Please select an employee from the dropdown list.";
                empValidation.ShowInputMessage = true;
                empValidation.PromptTitle = "Employee No";
                empValidation.Prompt = "Click the dropdown to select an employee.";
                empValidation.Formula.ExcelFormula = "EmployeeList";
            }

            // ── Data validation: Schedule Type dropdown (col 3, rows 5–500) ───────
            if (scheduleTypes.Count > 0)
            {
                var schedValidation = ws.DataValidations.AddListValidation(ws.Cells[5, 3, 500, 3].Address);
                schedValidation.ShowErrorMessage = true;
                schedValidation.ErrorTitle = "Invalid Schedule Type";
                schedValidation.Error = "Please select a schedule type from the dropdown list.";
                schedValidation.ShowInputMessage = true;
                schedValidation.PromptTitle = "Schedule Type";
                schedValidation.Prompt = "Click the dropdown to select a schedule type.";
                schedValidation.Formula.ExcelFormula = "ScheduleTypeList";
            }

            // ── Data validation: Time-In dropdown (col 4, rows 5–500) ─────────────
            var timeInValidation = ws.DataValidations.AddListValidation(ws.Cells[5, 4, 500, 4].Address);
            timeInValidation.ShowErrorMessage = false; // allow manual entry too
            timeInValidation.ShowInputMessage = true;
            timeInValidation.PromptTitle = "Time-In";
            timeInValidation.Prompt = "Select or type time in 12-hour format (e.g. 08:00 AM)";
            timeInValidation.Formula.ExcelFormula = "TimeList";

            // ── Data validation: Time-Out dropdown (col 5, rows 5–500) ────────────
            var timeOutValidation = ws.DataValidations.AddListValidation(ws.Cells[5, 5, 500, 5].Address);
            timeOutValidation.ShowErrorMessage = false; // allow manual entry too
            timeOutValidation.ShowInputMessage = true;
            timeOutValidation.PromptTitle = "Time-Out";
            timeOutValidation.Prompt = "Select or type time in 12-hour format (e.g. 05:00 PM)";
            timeOutValidation.Formula.ExcelFormula = "TimeList";

            // ── Notes section (rows 8 onwards, after example rows) ───────────────
            ws.Cells[8, 1].Value = "Notes:";
            ws.Cells[8, 1].Style.Font.Bold = true;
            ws.Cells[8, 1].Style.Font.Name = "Arial";

            var notes = new[]
            {
                "• Employee No: Select from the dropdown — format shown is 'EmployeeNo | Employee Name' (only EmployeeNo is saved)",
                "• Schedule Type: Select from the dropdown — format shown is 'Code | Name' (only the Code is saved)",
                "• Weekday Name is automatically derived from Effectivity Date — no need to fill it in",
                "• Effectivity Date must be in YYYY-MM-DD format",
                "• Time-In and Time-Out must be in 12-hour format (e.g. 08:00 AM, 05:00 PM, 11:00 PM)",
                "• Time-In and Time-Out cannot be the same value",
                "• Render Hours are auto-calculated from Time-In and Time-Out — supports overnight/graveyard shifts",
                "• Breaktime auto-defaults to 60 minutes when Time-In and Time-Out are filled — you can still edit it",
                "• Breaktime (minutes) must not be negative and cannot exceed render hours",
                "• Is Rest Day is automatically set to YES if Schedule Type contains 'REST', otherwise NO",
                "• For rest day schedules: Time-In, Time-Out, Render Hours and Breaktime are optional",
                "• Duplicate entries (same employee + weekday + effectivity date) will be skipped automatically",
                "• Re-download this template if new employees need to be scheduled (employee list is a snapshot of today)",
                "• Rows 5-6 are example rows — replace them with your actual data before importing"
            };

            for (int i = 0; i < notes.Length; i++)
            {
                ws.Cells[9 + i, 1].Value = notes[i];
                ws.Cells[9 + i, 1, 9 + i, 8].Merge = true;
                ws.Cells[9 + i, 1].Style.Font.Name = "Arial";
                ws.Cells[9 + i, 1].Style.Font.Size = 9;
            }

            // ── Column widths ─────────────────────────────────────────────────────
            ws.Column(1).Width = 30;  // Employee No (wider for "EmpNo | Name")
            ws.Column(2).Width = 18;  // Effectivity Date
            ws.Column(3).Width = 22;  // Schedule Type Code
            ws.Column(4).Width = 16;  // Time-In
            ws.Column(5).Width = 16;  // Time-Out
            ws.Column(6).Width = 14;  // Render Hours
            ws.Column(7).Width = 16;  // Breaktime
            ws.Column(8).Width = 22;  // Is Rest Day

            // ── Download timestamp ────────────────────────────────────────────────
            // Tells users when the employee list was generated — re-download if new hires need scheduling
            ws.Cells[2, 1].Value = $"Instructions: Fill in the required fields below. Fields marked with * are mandatory. Weekday is auto-derived from Effectivity Date. | Employee list as of: {DateTime.Now:MMMM dd, yyyy hh:mm tt}";

            return package.GetAsByteArray();
        }

        // ── Validation ─────────────────────────────────────────────────────────────
        private bool IsValidExcelFile(IFormFile file)
            => Path.GetExtension(file.FileName).ToLowerInvariant() == ".xlsx";

        /// <summary>
        /// Parses time strings in either 12-hour (08:00 AM / 05:00 PM) or
        /// 24-hour (08:00 / 17:00) format into a TimeSpan.
        /// </summary>
        private bool TryParseTime12Or24(string input, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var trimmed = input.Trim();

            // ── Case 1: Excel time serial read as decimal string (e.g. "0.333333") ──
            // EPPlus .Text on a time-formatted cell sometimes returns the raw decimal
            if (double.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double serial)
                && serial >= 0 && serial < 1)
            {
                result = TimeSpan.FromDays(serial);
                return true;
            }

            // ── Case 2: 12-hour format (e.g. "08:00 AM", "5:00 PM", "12:00 AM") ────
            if (DateTime.TryParseExact(trimmed,
                new[] { "hh:mm tt", "h:mm tt", "hh:mm:ss tt", "h:mm:ss tt",
                "hh:mmtt", "h:mmtt" },    // handle "8:00AM" without space too
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime parsed12))
            {
                result = parsed12.TimeOfDay;
                return true;
            }

            // ── Case 3: 24-hour format fallback (e.g. "08:00", "17:00") ─────────────
            if (TimeSpan.TryParse(trimmed, out TimeSpan parsed24))
            {
                result = parsed24;
                return true;
            }

            return false;
        }

        private ScheduleValidationResult ValidateExcelData(IFormFile file)
        {
            var result = new ScheduleValidationResult
            {
                Errors = new List<string>(),
                Warnings = new List<string>(),
                Data = new List<ImportEmployeeScheduleModel>()
            };

            using var stream = new MemoryStream();
            file.CopyTo(stream);

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage(stream);
            var ws = package.Workbook.Worksheets.FirstOrDefault();

            if (ws == null) { result.Message = "No worksheet found in the Excel file"; return result; }

            int startRow = 5;
            int rowCount = ws.Dimension?.Rows ?? 0;

            if (rowCount < startRow) { result.Message = "No data rows found in the Excel file"; return result; }

            int lastDataRow = startRow - 1;
            for (int r = rowCount; r >= startRow; r--)
            {
                var col1 = ws.Cells[r, 1].Text?.Trim();
                var col2 = ws.Cells[r, 2].Text?.Trim();
                if (!string.IsNullOrWhiteSpace(col1) || !string.IsNullOrWhiteSpace(col2))
                {
                    lastDataRow = r;
                    break;
                }
            }

            if (lastDataRow < startRow)
            {
                result.Message = "No data rows found in the Excel file";
                return result;
            }

            rowCount = lastDataRow;
            result.TotalRows = rowCount - startRow + 1;

            var validEmployees = GetEmployeeMapForValidation();
            var validScheduleTypes = GetValidScheduleTypesCodeAndName();

            var seenInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var seenEmployeeDateType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int row = startRow; row <= rowCount; row++)
            {
                var employeeRaw = ws.Cells[row, 1].Text?.Trim();
                var effectivityText = ws.Cells[row, 2].Text?.Trim();

                var schedTypeRaw = ws.Cells[row, 3].Text?.Trim();
                var schedTypeCode = schedTypeRaw ?? string.Empty;
                if (schedTypeCode.Contains(" | "))
                    schedTypeCode = schedTypeCode.Split(new[] { " | " }, StringSplitOptions.None)[0].Trim();

                var timeInText = ws.Cells[row, 4].Text?.Trim();
                var timeOutText = ws.Cells[row, 5].Text?.Trim();
                var breaktimeText = ws.Cells[row, 7].Text?.Trim();
                var isRestDayText = ws.Cells[row, 8].Text?.Trim().ToUpper();

                if (string.IsNullOrWhiteSpace(employeeRaw) && string.IsNullOrWhiteSpace(effectivityText))
                    continue;

                var rowErrors = new List<string>();
                bool isRestDay = isRestDayText == "YES" || isRestDayText == "Y";

                string employeeNo = employeeRaw ?? string.Empty;
                if (employeeNo.Contains(" | "))
                    employeeNo = employeeNo.Split(new[] { " | " }, StringSplitOptions.None)[0].Trim();

                string resolvedEmployeeName = null;
                if (string.IsNullOrWhiteSpace(employeeNo))
                    rowErrors.Add("Employee No is required");
                else if (!validEmployees.TryGetValue(employeeNo.ToUpper(), out resolvedEmployeeName))
                    rowErrors.Add($"Employee No '{employeeNo}' not found or inactive");

                bool effectivityParsed = DateTime.TryParse(effectivityText, out DateTime effectivityDate);
                if (!effectivityParsed)
                    rowErrors.Add("Invalid Effectivity Date format (use YYYY-MM-DD)");

                string derivedWeekday = null;
                if (effectivityParsed)
                    derivedWeekday = effectivityDate.DayOfWeek.ToString();

                string scheduleTypeName = null;
                if (string.IsNullOrWhiteSpace(schedTypeCode))
                    rowErrors.Add("Schedule Type Code is required");
                else if (!validScheduleTypes.TryGetValue(schedTypeCode, out scheduleTypeName))
                    rowErrors.Add($"Schedule Type Code '{schedTypeCode}' not found");

                TimeSpan timeIn = TimeSpan.Zero;
                TimeSpan timeOut = TimeSpan.Zero;
                double renderHours = 0;
                double breaktime = 0;

                if (!isRestDay)
                {
                    bool timeInParsed = false;
                    bool timeOutParsed = false;

                    if (string.IsNullOrWhiteSpace(timeInText))
                        rowErrors.Add("Time-In is required for working schedules");
                    else if (!TryParseTime12Or24(timeInText, out timeIn))
                        rowErrors.Add("Invalid Time-In format (use 12-hour format e.g. 08:00 AM or 11:00 PM)");
                    else
                        timeInParsed = true;

                    if (string.IsNullOrWhiteSpace(timeOutText))
                        rowErrors.Add("Time-Out is required for working schedules");
                    else if (!TryParseTime12Or24(timeOutText, out timeOut))
                        rowErrors.Add("Invalid Time-Out format (use 12-hour format e.g. 05:00 PM or 12:00 AM)");
                    else
                        timeOutParsed = true;

                    if (timeInParsed && timeOutParsed)
                    {
                        if (timeIn == timeOut)
                        {
                            rowErrors.Add("Time-In and Time-Out cannot be the same (render hours would be 0)");
                        }
                        else
                        {
                            double rawHours = timeOut > timeIn
                                ? (timeOut - timeIn).TotalHours
                                : (timeOut.Add(TimeSpan.FromHours(24)) - timeIn).TotalHours;

                            renderHours = Math.Round(rawHours, 2);

                            if (renderHours <= 0)
                                rowErrors.Add("Computed render hours must be greater than 0");

                            if (renderHours > 24)
                                rowErrors.Add($"Computed render hours ({renderHours}) cannot exceed 24 hours");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(breaktimeText))
                    {
                        if (!double.TryParse(breaktimeText, out breaktime))
                            rowErrors.Add("Breaktime must be a valid number");
                        else if (breaktime < 0)
                            rowErrors.Add("Breaktime cannot be negative");
                        else if (renderHours > 0)
                        {
                            double renderMinutes = renderHours * 60;
                            if (breaktime >= renderMinutes)
                                rowErrors.Add($"Breaktime ({breaktime} mins) cannot be equal to or exceed total render hours ({renderHours} hrs = {renderMinutes} mins)");
                        }
                    }
                }

                // ── Build last-name display for all rows ──────────────────────────
                string lastNameOnly = string.Empty;
                if (!string.IsNullOrWhiteSpace(resolvedEmployeeName))
                    lastNameOnly = resolvedEmployeeName.Split(',')[0].Trim();

                string employeeDisplay = string.IsNullOrWhiteSpace(lastNameOnly)
                    ? employeeNo
                    : $"{employeeNo} | {lastNameOnly}";

                if (rowErrors.Any())
                {
                    var errorMsg = string.Join(", ", rowErrors);
                    result.Errors.Add($"Row {row}: {errorMsg}");

                    // Include error row in Data so preview table can highlight it red
                    result.Data.Add(new ImportEmployeeScheduleModel
                    {
                        RowNumber = row,
                        EmployeeNo = string.IsNullOrWhiteSpace(employeeNo) ? "(missing)" : employeeNo,
                        EmployeeName = string.IsNullOrWhiteSpace(employeeNo) ? "(missing)" : employeeDisplay,
                        EffectivityDate = effectivityText ?? string.Empty,
                        ScheduleTypeCode = schedTypeCode,
                        ScheduleTypeName = scheduleTypeName ?? schedTypeCode,
                        TimeIn = timeInText,
                        TimeOut = timeOutText,
                        HasError = true,
                        ErrorMessage = errorMsg
                    });
                    continue;
                }

                string effectivityDateStr = effectivityDate.ToString("yyyy-MM-dd");

                // ── Intra-file duplicate check ────────────────────────────────────
                string dupeKey = $"{employeeNo.ToUpper()}|{derivedWeekday}|{effectivityDateStr}";
                if (seenInFile.TryGetValue(dupeKey, out int firstSeenRow))
                {
                    result.Errors.Add($"Row {row}: Duplicate within file — {employeeNo} / {derivedWeekday} / {effectivityDateStr} " +
                                      $"already exists at Row {firstSeenRow}. Remove one before importing.");
                    continue;
                }
                seenInFile[dupeKey] = row;

                // ── Mixed schedule type warning ───────────────────────────────────
                if (!string.IsNullOrWhiteSpace(schedTypeCode))
                {
                    string empDateKey = $"{employeeNo.ToUpper()}|{effectivityDateStr}";
                    if (seenEmployeeDateType.TryGetValue(empDateKey, out string existingType))
                    {
                        if (!existingType.Equals(schedTypeCode, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Warnings.Add($"Row {row}: Employee {employeeNo} has mixed schedule types " +
                                                $"({existingType} and {schedTypeCode}) on {effectivityDateStr}. " +
                                                $"This is allowed but may be unintentional — please review.");
                        }
                    }
                    else
                    {
                        seenEmployeeDateType[empDateKey] = schedTypeCode;
                    }
                }

                // ── Overnight shift support ───────────────────────────────────────
                string effectivityDateTo = effectivityDateStr;
                if (!isRestDay && timeOut < timeIn)
                    effectivityDateTo = effectivityDate.AddDays(1).ToString("yyyy-MM-dd");

                result.Data.Add(new ImportEmployeeScheduleModel
                {
                    EmployeeNo = employeeNo,
                    EmployeeName = employeeDisplay,
                    WeekdayName = derivedWeekday,
                    EffectivityDate = effectivityDateStr,
                    EffectivityDateTo = effectivityDateTo,
                    ScheduleTypeCode = schedTypeCode,
                    ScheduleTypeName = scheduleTypeName ?? schedTypeCode,
                    TimeIn = isRestDay ? null : timeIn.ToString(@"hh\:mm\:ss"),
                    TimeOut = isRestDay ? null : timeOut.ToString(@"hh\:mm\:ss"),
                    TotalRenderHour = isRestDay ? 0 : renderHours,
                    TotalBreaktimeMinute = isRestDay ? 0 : breaktime,
                    IsRestDay = isRestDay,
                    HasWarning = false,
                    RowNumber = row
                });
                result.ValidRows++;
            }

            // ── Post-pass: flag HasWarning on mixed schedule type rows ────────────
            var mixedTypeKeys = result.Data
                .Where(d => !d.HasError && !string.IsNullOrWhiteSpace(d.ScheduleTypeCode))
                .GroupBy(
                    d => $"{d.EmployeeNo.ToUpper()}|{d.EffectivityDate}",
                    StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(x => x.ScheduleTypeCode.ToUpper()).Distinct().Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in result.Data)
            {
                if (!item.HasError &&
                    mixedTypeKeys.Contains($"{item.EmployeeNo.ToUpper()}|{item.EffectivityDate}"))
                    item.HasWarning = true;
            }

            // ── Distinct employee count from valid rows only ──────────────────────
            result.DistinctEmployeeCount = result.Data
                .Where(d => !d.HasError)
                .Select(d => d.EmployeeNo.ToUpper())
                .Distinct()
                .Count();

            result.IsValid = result.ValidRows > 0;
            result.Message = result.IsValid
                ? $"Validation successful: {result.ValidRows} of {result.TotalRows} rows are valid"
                : "Validation failed: No valid rows found";

            return result;
        }

        // ── Import ─────────────────────────────────────────────────────────────────
        private ScheduleImportResult ProcessImportData(List<ImportEmployeeScheduleModel> schedules)
        {
            var result = new ScheduleImportResult { Errors = new List<string>() };
            var addedByUser = HttpContext.Session.GetString("employeeNo") ?? "SYSTEM";

            foreach (var schedule in schedules)
            {
                try
                {
                    // Never insert error rows or rows with missing weekday
                    if (schedule.HasError || string.IsNullOrWhiteSpace(schedule.WeekdayName))
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    // EmployeeName carries "R00008 | Carreon" for display — strip for DB
                    var cleanEmployeeNo = schedule.EmployeeNo.Contains(" | ")
                        ? schedule.EmployeeNo.Split(new[] { " | " }, StringSplitOptions.None)[0].Trim()
                        : schedule.EmployeeNo;

                    var exists = _db.ExecuteScalar<int>(@"
                        SELECT COUNT(*) 
                        FROM e_schedule 
                        WHERE employeeNo            = @employeeNo 
                        AND   weekdayName           = @weekdayName 
                        AND   DATE(effectivityDate) = DATE(@effectivityDate)
                        AND   isActive = 1",
                        new
                        {
                            employeeNo = cleanEmployeeNo,
                            weekdayName = schedule.WeekdayName,
                            effectivityDate = schedule.EffectivityDate
                        });

                    if (exists > 0)
                    {
                        result.SkippedCount++;
                        result.Errors.Add($"Row {schedule.RowNumber}: Skipped — schedule already exists for " +
                                          $"{cleanEmployeeNo} on {schedule.WeekdayName} ({schedule.EffectivityDate})");
                        continue;
                    }

                    // INSERT only — no SELECT in the same statement to avoid
                    // MySQL Connector "fatal error reading resultset" with Dapper
                    var insertSql = @"
                        INSERT INTO e_schedule 
                        (employeeNo, weekdayName, effectivityDate, effectivityDateTo, scheduleTypeCode,
                         timeIn, timeOut, totalRenderHour, totalBreaktimeMinute,
                         isRestDay, dtAdded, addedByUser, isActive)
                        VALUES 
                        (@employeeNo, @weekdayName, @effectivityDate, @effectivityDateTo, @scheduleTypeCode,
                         @timeIn, @timeOut, @totalRenderHour, @totalBreaktimeMinute,
                         @isRestDay, NOW(), @addedByUser, 1)";

                    _db.Execute(insertSql, new
                    {
                        employeeNo = cleanEmployeeNo,
                        weekdayName = schedule.WeekdayName,
                        effectivityDate = schedule.EffectivityDate,
                        effectivityDateTo = schedule.EffectivityDateTo,
                        scheduleTypeCode = schedule.ScheduleTypeCode,
                        timeIn = schedule.IsRestDay ? null : schedule.TimeIn,
                        timeOut = schedule.IsRestDay ? null : schedule.TimeOut,
                        totalRenderHour = schedule.TotalRenderHour,
                        totalBreaktimeMinute = schedule.TotalBreaktimeMinute,
                        isRestDay = schedule.IsRestDay ? 1 : (int?)null,
                        addedByUser
                    });

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    var fullError = ex.InnerException != null
                        ? $"{ex.Message} → {ex.InnerException.Message}"
                        : ex.Message;
                    result.Errors.Add($"Row {schedule.RowNumber}: {fullError}");
                }
            }

            return result;
        }

        // ── DB helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a case-insensitive dictionary of employeeNo (uppercase) → fullName
        /// used during validation to verify employee existence and resolve display name.
        /// </summary>
        private Dictionary<string, string> GetEmployeeMapForValidation()
            => _db.Query<(string No, string Name)>(
                    "SELECT employeeNo, CONCAT(lastName, ', ', firstName) AS employeeName FROM e_basicinfo WHERE isActive = 1")
                  .GroupBy(x => x.No.Trim().ToUpper())
                  .ToDictionary(
                      g => g.Key,
                      g => g.First().Name,
                      StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns a flat list of employees for the dropdown in the template.
        /// </summary>
        private List<(string EmployeeNo, string EmployeeName)> GetEmployeeListForTemplate()
            => _db.Query<(string EmployeeNo, string EmployeeName)>(
                    "SELECT employeeNo, CONCAT(lastName, ', ', firstName) AS employeeName FROM e_basicinfo WHERE isActive = 1 ORDER BY lastName, firstName")
                  .ToList();

        private Dictionary<string, string> GetValidScheduleTypesCodeAndName()
            => _db.Query<(string Code, string Name)>(
            "SELECT scheduleTypeCode, scheduleTypeName FROM s_scheduleType WHERE isActive = 1")
          .GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
          .ToDictionary(
              g => g.Key,
              g => g.First().Name,
              StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns a flat list of schedule types for the dropdown in the template.
        /// </summary>
        private List<(string Code, string Name)> GetScheduleTypeListForTemplate()
            => _db.Query<(string Code, string Name)>(
                    "SELECT scheduleTypeCode, scheduleTypeName FROM s_scheduleType WHERE isActive = 1 ORDER BY scheduleTypeName")
                  .ToList();
    }

    // ── Supporting models ──────────────────────────────────────────────────────────

    public class ImportEmployeeScheduleModel
    {
        public string EmployeeNo { get; set; }
        public string EmployeeName { get; set; }       // resolved from DB during validation
        public string WeekdayName { get; set; }        // auto-derived from EffectivityDate
        public string EffectivityDate { get; set; }
        public string EffectivityDateTo { get; set; }
        public string ScheduleTypeCode { get; set; }
        public string ScheduleTypeName { get; set; }
        public string TimeIn { get; set; }
        public string TimeOut { get; set; }
        public double TotalRenderHour { get; set; }
        public double TotalBreaktimeMinute { get; set; }
        public bool IsRestDay { get; set; }
        public bool HasWarning { get; set; }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
        public int RowNumber { get; set; }
    }

    public class ScheduleValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int DistinctEmployeeCount { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public List<ImportEmployeeScheduleModel> Data { get; set; }
    }

    public class ScheduleImportResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> Errors { get; set; }
    }
}