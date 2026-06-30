using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.ChangeScheduleRequest
{
    public class ChangeScheduleRequestImportController : Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public ChangeScheduleRequestImportController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        // Returns the import view
        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/ChangeScheduleRequestImport.cshtml");
        }

        // Downloads the Excel template for importing change schedule requests
        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            try
            {
                var excelFile = GenerateTemplateFile();
                var fileName = $"ChangeScheduleRequest_Template_{DateTime.Now:yyyyMMdd}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Template generation failed: {ex.Message}" });
            }
        }

        // Validates the uploaded Excel file and returns validation results
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
                    errors = validationResult.Errors,
                    data = validationResult.Data
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Validation failed: {ex.Message}" });
            }
        }

        // Imports validated change schedule requests into the database
        [HttpPost]
        public IActionResult ImportData([FromBody] List<ImportChangeScheduleRequestModel> requests)
        {
            try
            {
                if (requests == null || !requests.Any())
                    return BadRequest(new { success = false, message = "No data to import" });

                var results = ProcessImportData(requests);

                return Json(new
                {
                    success = results.SuccessCount > 0,
                    message = $"Import completed: {results.SuccessCount} succeeded, {results.FailureCount} failed",
                    successCount = results.SuccessCount,
                    failureCount = results.FailureCount,
                    errors = results.Errors
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Import failed: {ex.Message}" });
            }
        }

        // Generates Excel template with headers and instructions
        private byte[] GenerateTemplateFile()
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Change Schedule Request");

            // Title
            ws.Cells[1, 1].Value = "Change Schedule Request Import Template";
            ws.Cells[1, 1, 1, 7].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

            // Instructions
            ws.Cells[2, 1].Value = "Instructions: Fill in the required fields below. Fields marked with * are mandatory.";
            ws.Cells[2, 1, 2, 7].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 10;
            ws.Cells[2, 1].Style.Font.Italic = true;

            // Headers (Row 4)
            var headers = new[]
            {
                "Employee No*",
                "Effectivity Date* (YYYY-MM-DD)",
                "Time-In* (HH:MM)",
                "Time-Out* (HH:MM)",
                "Schedule Type Code*",
                "Reason*",
                "Remarks"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cells[4, i + 1];
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                cell.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
            }

            // Example row (Row 5)
            ws.Cells[5, 1].Value = "EMP001";
            ws.Cells[5, 2].Value = "2026-03-01";
            ws.Cells[5, 3].Value = "09:00";
            ws.Cells[5, 4].Value = "18:00";
            ws.Cells[5, 5].Value = "REG";
            ws.Cells[5, 6].Value = "Schedule adjustment for project deadline";
            ws.Cells[5, 7].Value = "Approved by manager";

            // Notes section
            ws.Cells[7, 1].Value = "Notes:";
            ws.Cells[7, 1].Style.Font.Bold = true;
            ws.Cells[8, 1].Value = "• Employee No must exist in the system";
            ws.Cells[9, 1].Value = "• Effectivity Date must be in YYYY-MM-DD format";
            ws.Cells[10, 1].Value = "• Time-In and Time-Out must be in HH:MM format (24-hour)";
            ws.Cells[11, 1].Value = "• Schedule Type Code must exist in the system (e.g., REG, FLEX, NIGHT)";
            ws.Cells[12, 1].Value = "• Reason is mandatory - provide detailed explanation";
            ws.Cells[13, 1].Value = "• All imported requests will have 'Pending' status across all approval levels";

            ws.Cells.AutoFitColumns();
            return package.GetAsByteArray();
        }

        // Validates Excel file format
        private bool IsValidExcelFile(IFormFile file)
        {
            var allowedExtensions = new[] { ".xlsx" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return allowedExtensions.Contains(extension);
        }

        // Validates Excel data and returns validation results
        private ValidationResult ValidateExcelData(IFormFile file)
        {
            var result = new ValidationResult { Errors = new List<string>(), Data = new List<ImportChangeScheduleRequestModel>() };

            using (var stream = new MemoryStream())
            {
                file.CopyTo(stream);
                using var package = new ExcelPackage(stream);
                var ws = package.Workbook.Worksheets.FirstOrDefault();

                if (ws == null)
                {
                    result.Message = "No worksheet found in the Excel file";
                    return result;
                }

                // Find header row (should be row 4 based on template)
                int headerRow = 4;
                int startRow = 5; // Start reading from row 5 (first data row after headers)
                int rowCount = ws.Dimension?.Rows ?? 0;

                if (rowCount < startRow)
                {
                    result.Message = "No data rows found in the Excel file";
                    return result;
                }

                result.TotalRows = rowCount - startRow + 1;

                // Get valid employees and schedule types for validation
                var validEmployees = GetValidEmployeeNumbers();
                var validScheduleTypes = GetValidScheduleTypeCodes();

                for (int row = startRow; row <= rowCount; row++)
                {
                    var rowErrors = new List<string>();
                    var employeeNo = ws.Cells[row, 1].Text?.Trim();
                    var effectivityDateText = ws.Cells[row, 2].Text?.Trim();
                    var timeInText = ws.Cells[row, 3].Text?.Trim();
                    var timeOutText = ws.Cells[row, 4].Text?.Trim();
                    var scheduleTypeCode = ws.Cells[row, 5].Text?.Trim();
                    var reason = ws.Cells[row, 6].Text?.Trim();
                    var remarks = ws.Cells[row, 7].Text?.Trim();

                    // Skip completely empty rows
                    if (string.IsNullOrWhiteSpace(employeeNo) && string.IsNullOrWhiteSpace(effectivityDateText))
                        continue;

                    // Validate required fields
                    if (string.IsNullOrWhiteSpace(employeeNo))
                        rowErrors.Add("Employee No is required");
                    else if (!validEmployees.Contains(employeeNo))
                        rowErrors.Add($"Employee No '{employeeNo}' not found");

                    if (!DateTime.TryParse(effectivityDateText, out DateTime effectivityDate))
                        rowErrors.Add("Invalid Effectivity Date format (use YYYY-MM-DD)");

                    TimeSpan timeIn = TimeSpan.Zero;
                    if (string.IsNullOrWhiteSpace(timeInText))
                        rowErrors.Add("Time-In is required");
                    else if (!TimeSpan.TryParse(timeInText, out timeIn))
                        rowErrors.Add("Invalid Time-In format (use HH:MM)");

                    TimeSpan timeOut = TimeSpan.Zero;
                    if (string.IsNullOrWhiteSpace(timeOutText))
                        rowErrors.Add("Time-Out is required");
                    else if (!TimeSpan.TryParse(timeOutText, out timeOut))
                        rowErrors.Add("Invalid Time-Out format (use HH:MM)");

                    if (string.IsNullOrWhiteSpace(scheduleTypeCode))
                        rowErrors.Add("Schedule Type Code is required");
                    else if (!validScheduleTypes.Contains(scheduleTypeCode))
                        rowErrors.Add($"Schedule Type Code '{scheduleTypeCode}' not found");

                    if (string.IsNullOrWhiteSpace(reason))
                        rowErrors.Add("Reason is required");

                    if (rowErrors.Any())
                    {
                        result.Errors.Add($"Row {row}: {string.Join(", ", rowErrors)}");
                    }
                    else
                    {
                        // Calculate weekday name from effectivity date
                        string weekdayName = effectivityDate.DayOfWeek.ToString();

                        result.Data.Add(new ImportChangeScheduleRequestModel
                        {
                            EmployeeNo = employeeNo,
                            WeekdayName = weekdayName,
                            EffectivityDate = effectivityDate,
                            TimeIN = timeIn,
                            TimeOUT = timeOut,
                            ScheduleTypeCode = scheduleTypeCode,
                            Reason = reason,
                            Remarks = remarks,
                            RowNumber = row
                        });
                        result.ValidRows++;
                    }
                }

                result.IsValid = result.ValidRows > 0;
                result.Message = result.IsValid
                    ? $"Validation successful: {result.ValidRows} of {result.TotalRows} rows are valid"
                    : "Validation failed: No valid rows found";
            }

            return result;
        }

        // Processes and imports validated data
        private ImportResult ProcessImportData(List<ImportChangeScheduleRequestModel> requests)
        {
            var result = new ImportResult { Errors = new List<string>() };
            var employeeNo = HttpContext.Session.GetString("employeeNo") ?? "SYSTEM";

            foreach (var request in requests)
            {
                try
                {
                    var sql = @"
                        INSERT INTO rq_changeschedule 
                        (employeeNo, weekdayName, effectivityDate, timeIN, timeOUT, Reason, 
                         scheduleTypeCode, statusLevel2, statusLevel3, statusLevel4, remarks, 
                         isActive, dtAdded, addedByUser, requestedByUser, 
                         dtStatus, statusByUser, dtStatusLevel2, statusByLevel2, 
                         dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                        VALUES 
                        (@employeeNo, @weekdayName, @effectivityDate, @timeIN, @timeOUT, @Reason, 
                         @scheduleTypeCode, 'Pending', 'Pending', 'Pending', @remarks, 
                         1, NOW(), @addedByUser, @requestedByUser, 
                         NOW(), @addedByUser, NOW(), @addedByUser, 
                         NOW(), @addedByUser, NOW(), @addedByUser);
                        SELECT LAST_INSERT_ID();";

                    int newId = _db.QuerySingle<int>(sql, new
                    {
                        request.EmployeeNo,
                        request.WeekdayName,
                        request.EffectivityDate,
                        request.TimeIN,
                        request.TimeOUT,
                        request.Reason,
                        request.ScheduleTypeCode,
                        remarks = request.Remarks ?? "",
                        addedByUser = employeeNo,
                        requestedByUser = employeeNo
                    });

                    _auditTrail.Log("rq_changeschedule", newId, "IMPORTED",
                        $"Imported change schedule request for {request.EmployeeNo}: {request.EffectivityDate:yyyy-MM-dd} ({request.TimeIN}-{request.TimeOUT})");

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    result.Errors.Add($"Row {request.RowNumber}: {ex.Message}");
                }
            }

            return result;
        }

        // Helper methods
        private HashSet<string> GetValidEmployeeNumbers()
        {
            var sql = "SELECT employeeNo FROM e_basicinfo WHERE isActive = 1";
            return _db.Query<string>(sql).ToHashSet();
        }

        private HashSet<string> GetValidScheduleTypeCodes()
        {
            var sql = "SELECT scheduleTypeCode FROM s_scheduleType WHERE isActive = 1";
            return _db.Query<string>(sql).ToHashSet();
        }
    }

    // Supporting classes
    public class ImportChangeScheduleRequestModel
    {
        public string EmployeeNo { get; set; }
        public string WeekdayName { get; set; }
        public DateTime EffectivityDate { get; set; }
        public TimeSpan TimeIN { get; set; }
        public TimeSpan TimeOUT { get; set; }
        public string ScheduleTypeCode { get; set; }
        public string Reason { get; set; }
        public string Remarks { get; set; }
        public int RowNumber { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public List<string> Errors { get; set; }
        public List<ImportChangeScheduleRequestModel> Data { get; set; }
    }

    public class ImportResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> Errors { get; set; }
    }
}