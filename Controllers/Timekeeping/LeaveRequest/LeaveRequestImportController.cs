using Dapper;
using KEISAN_HRIS_v2.Models.Timekeeping;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.LeaveRequest
{
    public class LeaveRequestImportController : Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LeaveRequestImportController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        // Returns the import view
        public IActionResult Index()
        {
            return View("~/Views/Timekeeping/LeaveRequestImport.cshtml");
        }

        // Downloads the Excel template for importing leave requests
        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            try
            {
                var excelFile = GenerateTemplateFile();
                var fileName = $"LeaveRequest_Template_{DateTime.Now:yyyyMMdd}.xlsx";

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

        // Imports validated leave requests into the database
        [HttpPost]
        public IActionResult ImportData([FromBody] List<ImportLeaveRequestModel> requests)
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
            var ws = package.Workbook.Worksheets.Add("Leave Request");

            // Title
            ws.Cells[1, 1].Value = "Leave Request Import Template";
            ws.Cells[1, 1, 1, 8].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

            // Instructions
            ws.Cells[2, 1].Value = "Instructions: Fill in the required fields below. Fields marked with * are mandatory.";
            ws.Cells[2, 1, 2, 8].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 10;
            ws.Cells[2, 1].Style.Font.Italic = true;

            // Headers (Row 4)
            var headers = new[]
            {
                "Employee No*",
                "Leave Type*",
                "Leave Date From* (YYYY-MM-DD)",
                "Leave Date To* (YYYY-MM-DD)",
                "Leave Duration (whole/first/second)*",
                "Leave Count Days",
                "Leave Reason*",
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
            ws.Cells[5, 2].Value = "VL";
            ws.Cells[5, 3].Value = "2026-02-01";
            ws.Cells[5, 4].Value = "2026-02-03";
            ws.Cells[5, 5].Value = "whole";
            ws.Cells[5, 6].Value = 3.0;
            ws.Cells[5, 7].Value = "Family vacation";
            ws.Cells[5, 8].Value = "Pre-approved";

            // Notes section
            ws.Cells[7, 1].Value = "Notes:";
            ws.Cells[7, 1].Style.Font.Bold = true;
            ws.Cells[8, 1].Value = "• Employee No and Leave Type (Code or Name) must exist in the system";
            ws.Cells[9, 1].Value = "• Leave dates must be in YYYY-MM-DD format";
            ws.Cells[10, 1].Value = "• Leave Date To must be equal to or later than Leave Date From";
            ws.Cells[11, 1].Value = "• Leave Duration must be: whole, first, or second";
            ws.Cells[12, 1].Value = "• Leave Count Days will be calculated automatically if left blank";
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
            var result = new ValidationResult { Errors = new List<string>(), Data = new List<ImportLeaveRequestModel>() };

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

                // Get valid employees and leave types for validation
                var validEmployees = GetValidEmployeeNumbers();
                var validLeaveTypes = GetValidLeaveTypesCodesAndNames();
                var validLeaveTypeValues = new[] { "whole", "first", "second" };

                for (int row = startRow; row <= rowCount; row++)
                {
                    var rowErrors = new List<string>();
                    var employeeNo = ws.Cells[row, 1].Text?.Trim();
                    var leaveTypeInput = ws.Cells[row, 2].Text?.Trim();
                    var dateFromText = ws.Cells[row, 3].Text?.Trim();
                    var dateToText = ws.Cells[row, 4].Text?.Trim();
                    var leaveType = ws.Cells[row, 5].Text?.Trim().ToLower();
                    var leaveCountDaysText = ws.Cells[row, 6].Text?.Trim();
                    var leaveReason = ws.Cells[row, 7].Text?.Trim();
                    var remarks = ws.Cells[row, 8].Text?.Trim();

                    // Skip completely empty rows
                    if (string.IsNullOrWhiteSpace(employeeNo) && string.IsNullOrWhiteSpace(leaveTypeInput))
                        continue;

                    // Validate required fields
                    if (string.IsNullOrWhiteSpace(employeeNo))
                        rowErrors.Add("Employee No is required");
                    else if (!validEmployees.Contains(employeeNo))
                        rowErrors.Add($"Employee No '{employeeNo}' not found");

                    string leaveCode = null;
                    if (string.IsNullOrWhiteSpace(leaveTypeInput))
                    {
                        rowErrors.Add("Leave Type is required");
                    }
                    else
                    {
                        // Try to find by code or name
                        leaveCode = FindLeaveCode(leaveTypeInput, validLeaveTypes);
                        if (leaveCode == null)
                            rowErrors.Add($"Leave Type '{leaveTypeInput}' not found");
                    }

                    if (!DateTime.TryParse(dateFromText, out DateTime leaveDateFrom))
                        rowErrors.Add("Invalid Leave Date From format (use YYYY-MM-DD)");

                    if (!DateTime.TryParse(dateToText, out DateTime leaveDateTo))
                        rowErrors.Add("Invalid Leave Date To format (use YYYY-MM-DD)");
                    else if (leaveDateTo < leaveDateFrom)
                        rowErrors.Add("Leave Date To cannot be earlier than Leave Date From");

                    if (string.IsNullOrWhiteSpace(leaveType))
                        rowErrors.Add("Leave Duration (whole/first/second) is required");
                    else if (!validLeaveTypeValues.Contains(leaveType))
                        rowErrors.Add("Leave Duration must be: whole, first, or second");

                    decimal leaveCountDays = 0;
                    if (string.IsNullOrWhiteSpace(leaveCountDaysText))
                    {
                        // Calculate automatically
                        if (!rowErrors.Any() && leaveDateFrom != default && leaveDateTo != default)
                        {
                            int totalDays = (int)(leaveDateTo - leaveDateFrom).TotalDays + 1;
                            decimal multiplier = leaveType == "whole" ? 1m : 0.5m;
                            leaveCountDays = totalDays * multiplier;
                        }
                    }
                    else if (!decimal.TryParse(leaveCountDaysText, out leaveCountDays) || leaveCountDays <= 0)
                    {
                        rowErrors.Add("Leave Count Days must be a positive number");
                    }

                    decimal leaveCountHours = 0;

                    if (string.IsNullOrWhiteSpace(leaveReason))
                        rowErrors.Add("Leave Reason is required");

                    if (rowErrors.Any())
                    {
                        result.Errors.Add($"Row {row}: {string.Join(", ", rowErrors)}");
                    }
                    else
                    {
                        result.Data.Add(new ImportLeaveRequestModel
                        {
                            EmployeeNo = employeeNo,
                            LeaveCode = leaveCode,
                            LeaveDateFrom = leaveDateFrom,
                            LeaveDateTo = leaveDateTo,
                            LeaveType = leaveType,
                            LeaveCountDays = leaveCountDays,
                            LeaveCountHours = leaveCountHours,
                            LeaveReason = leaveReason,
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

        // Find leave code by code or name
        private string FindLeaveCode(string input, Dictionary<string, string> validLeaveTypes)
        {
            // First try exact match with code
            if (validLeaveTypes.ContainsKey(input))
                return input;

            // Then try case-insensitive match with name
            var matchByName = validLeaveTypes.FirstOrDefault(x =>
                x.Value.Equals(input, StringComparison.OrdinalIgnoreCase));

            if (!matchByName.Equals(default(KeyValuePair<string, string>)))
                return matchByName.Key;

            return null;
        }

        // Processes and imports validated data
        private ImportResult ProcessImportData(List<ImportLeaveRequestModel> requests)
        {
            var result = new ImportResult { Errors = new List<string>() };
            var employeeNo = HttpContext.Session.GetString("employeeNo") ?? "SYSTEM";

            foreach (var request in requests)
            {
                try
                {
                    var sql = @"
                        INSERT INTO rq_leave 
                        (employeeNo, leaveCode, leaveDateFrom, leaveDateTo, leaveCountDays, leaveCountHours, 
                         leaveReason, leaveType, statusLevel2, statusLevel3, statusLevel4, remarks, 
                         creditDeductionOnly, isActive, dtAdded, addedByUser, 
                         dtStatus, statusByUser, dtStatusLevel2, statusByLevel2, 
                         dtStatusLevel3, statusByLevel3, dtStatusLevel4, statusByLevel4) 
                        VALUES 
                        (@employeeNo, @leaveCode, @leaveDateFrom, @leaveDateTo, @leaveCountDays, @leaveCountHours, 
                         @leaveReason, @leaveType, 'Pending', 'Pending', 'Pending', @remarks, 
                         0, 1, NOW(), @addedByUser, 
                         NOW(), @addedByUser, NOW(), @addedByUser, 
                         NOW(), @addedByUser, NOW(), @addedByUser);
                        SELECT LAST_INSERT_ID();";

                    int newId = _db.QuerySingle<int>(sql, new
                    {
                        request.EmployeeNo,
                        request.LeaveCode,
                        request.LeaveDateFrom,
                        request.LeaveDateTo,
                        request.LeaveCountDays,
                        request.LeaveCountHours,
                        request.LeaveReason,
                        request.LeaveType,
                        remarks = request.Remarks ?? "",
                        addedByUser = employeeNo
                    });

                    _auditTrail.Log("rq_leave", newId, "IMPORTED",
                        $"Imported leave request for {request.EmployeeNo}: {request.LeaveDateFrom:yyyy-MM-dd} to {request.LeaveDateTo:yyyy-MM-dd} ({request.LeaveCode})");

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

        // Get both leave codes and names
        private Dictionary<string, string> GetValidLeaveTypesCodesAndNames()
        {
            var sql = @"SELECT leaveCode, leaveName FROM s_leave 
                       WHERE isActive = 1 AND dtDeleted IS NULL";
            return _db.Query<(string Code, string Name)>(sql)
                .ToDictionary(x => x.Code, x => x.Name);
        }
    }

    // Supporting classes
    public class ImportLeaveRequestModel
    {
        public string EmployeeNo { get; set; }
        public string LeaveCode { get; set; }
        public DateTime LeaveDateFrom { get; set; }
        public DateTime LeaveDateTo { get; set; }
        public string LeaveType { get; set; }
        public decimal LeaveCountDays { get; set; }
        public decimal LeaveCountHours { get; set; }
        public string LeaveReason { get; set; }
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
        public List<ImportLeaveRequestModel> Data { get; set; }
    }

    public class ImportResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> Errors { get; set; }
    }
}