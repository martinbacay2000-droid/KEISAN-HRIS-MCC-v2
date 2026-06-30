using Dapper;
using KEISAN_HRIS_v2.Models.Payroll;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class AdjustmentRequestListImportController : Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public AdjustmentRequestListImportController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payroll/AdjustmentRequestListImport.cshtml");
        }

        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            try
            {
                var excelFile = GenerateTemplateFile();
                var fileName = $"AdjustmentRequestList_Template_{DateTime.Now:yyyyMMdd}.xlsx";
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
                    errors = validationResult.Errors,
                    data = validationResult.Data
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Validation failed: {ex.Message}" });
            }
        }

        [HttpPost]
        public IActionResult ImportData([FromBody] List<ImportAdjustmentRequestListModel> requests)
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

        private byte[] GenerateTemplateFile()
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Adjustment Request List");

            ws.Cells[1, 1].Value = "Adjustment Request List Import Template";
            ws.Cells[1, 1, 1, 9].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

            ws.Cells[2, 1].Value = "Instructions: Fill in the required fields below. Fields marked with * are mandatory.";
            ws.Cells[2, 1, 2, 9].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 10;
            ws.Cells[2, 1].Style.Font.Italic = true;

            var headers = new[]
            {
                "Employee No*",
                "Adjustment Type*",
                "Requested Amount*",
                "Effectivity Date* (YYYY-MM-DD)",
                "Remarks",
                "Day Type",
                "Value",
                "Unit"
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

            ws.Cells[5, 1].Value = "EMP001";
            ws.Cells[5, 2].Value = "ADJ001";
            ws.Cells[5, 3].Value = 1000.00;
            ws.Cells[5, 4].Value = "2026-02-01";
            ws.Cells[5, 5].Value = "Sample adjustment";
            ws.Cells[5, 6].Value = "Regular Day - OT";
            ws.Cells[5, 7].Value = 2.5;
            ws.Cells[5, 8].Value = "Hour";

            ws.Cells[7, 1].Value = "Notes:";
            ws.Cells[7, 1].Style.Font.Bold = true;
            ws.Cells[8, 1].Value = "• Employee No and Adjustment Type (Code or Name) must exist in the system";
            ws.Cells[9, 1].Value = "• Effectivity Date must be in YYYY-MM-DD format";
            ws.Cells[10, 1].Value = "• For Time Keeping Adjustments, Day Type, Value, and Unit are required";
            ws.Cells[11, 1].Value = "• Requested Amount: Use positive numbers for additions, negative for deductions";

            ws.Cells.AutoFitColumns();
            return package.GetAsByteArray();
        }

        private bool IsValidExcelFile(IFormFile file)
        {
            var allowedExtensions = new[] { ".xlsx" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return allowedExtensions.Contains(extension);
        }

        private ValidationResult ValidateExcelData(IFormFile file)
        {
            var result = new ValidationResult { Errors = new List<string>(), Data = new List<ImportAdjustmentRequestListModel>() };

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

                int startRow = 5;
                int rowCount = ws.Dimension?.Rows ?? 0;

                if (rowCount < startRow)
                {
                    result.Message = "No data rows found in the Excel file";
                    return result;
                }

                result.TotalRows = rowCount - startRow + 1;

                var validEmployees = GetValidEmployeeNumbers();
                var validAdjustments = GetValidAdjustmentCodesAndNames();
                var timeKeepingBasicSalaryCode = GetTimeKeepingBasicSalaryCode();
                var timeKeepingAllowanceCode = GetTimeKeepingAllowanceCode();

                for (int row = startRow; row <= rowCount; row++)
                {
                    var rowErrors = new List<string>();
                    var employeeNo = ws.Cells[row, 1].Text?.Trim();
                    var adjustmentInput = ws.Cells[row, 2].Text?.Trim();
                    var amountText = ws.Cells[row, 3].Text?.Trim();
                    var dateText = ws.Cells[row, 4].Text?.Trim();
                    var remarks = ws.Cells[row, 5].Text?.Trim();
                    var dayType = ws.Cells[row, 6].Text?.Trim();
                    var valueText = ws.Cells[row, 7].Text?.Trim();
                    var unit = ws.Cells[row, 8].Text?.Trim();

                    if (string.IsNullOrWhiteSpace(employeeNo) && string.IsNullOrWhiteSpace(adjustmentInput))
                        continue;

                    if (string.IsNullOrWhiteSpace(employeeNo))
                        rowErrors.Add("Employee No is required");
                    else if (!validEmployees.Contains(employeeNo))
                        rowErrors.Add($"Employee No '{employeeNo}' not found");

                    string adjustmentCode = null;
                    if (string.IsNullOrWhiteSpace(adjustmentInput))
                    {
                        rowErrors.Add("Adjustment Type is required");
                    }
                    else
                    {
                        adjustmentCode = FindAdjustmentCode(adjustmentInput, validAdjustments);
                        if (adjustmentCode == null)
                            rowErrors.Add($"Adjustment Type '{adjustmentInput}' not found");
                    }

                    if (!double.TryParse(amountText, out double amount))
                        rowErrors.Add("Invalid amount format");

                    if (!DateTime.TryParse(dateText, out DateTime effectivityDate))
                        rowErrors.Add("Invalid date format (use YYYY-MM-DD)");

                    // Check if it's either time keeping type (basic salary or allowance)
                    bool isTimeKeeping = adjustmentCode == timeKeepingBasicSalaryCode
                                     || adjustmentCode == timeKeepingAllowanceCode;

                    if (isTimeKeeping)
                    {
                        if (string.IsNullOrWhiteSpace(dayType))
                            rowErrors.Add("Day Type is required for Time Keeping Adjustment");
                        if (string.IsNullOrWhiteSpace(valueText) || !double.TryParse(valueText, out _))
                            rowErrors.Add("Valid Value is required for Time Keeping Adjustment");
                        if (string.IsNullOrWhiteSpace(unit))
                            rowErrors.Add("Unit is required for Time Keeping Adjustment");
                    }

                    if (rowErrors.Any())
                    {
                        result.Errors.Add($"Row {row}: {string.Join(", ", rowErrors)}");
                    }
                    else
                    {
                        result.Data.Add(new ImportAdjustmentRequestListModel
                        {
                            EmployeeNo = employeeNo,
                            AdjustmentCode = adjustmentCode,
                            Amount = amount,
                            DateToAdjustment = effectivityDate,
                            Reason = remarks,
                            DayType = dayType,
                            Value = string.IsNullOrWhiteSpace(valueText) ? 0 : double.Parse(valueText),
                            Units = unit,
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

        private string FindAdjustmentCode(string input, Dictionary<string, string> validAdjustments)
        {
            if (validAdjustments.ContainsKey(input))
                return input;

            var matchByName = validAdjustments.FirstOrDefault(x =>
                x.Value.Equals(input, StringComparison.OrdinalIgnoreCase));

            if (!matchByName.Equals(default(KeyValuePair<string, string>)))
                return matchByName.Key;

            return null;
        }

        private ImportResult ProcessImportData(List<ImportAdjustmentRequestListModel> requests)
        {
            var result = new ImportResult { Errors = new List<string>() };
            var employeeNo = HttpContext.Session.GetString("employeeNo") ?? "SYSTEM";

            foreach (var request in requests)
            {
                try
                {
                    var sql = @"
                        INSERT INTO c_payable 
                        (employeeNo, adjustmentCode, amount, dateToAdjustment, reason, statusName, 
                         DayType, Value, Units, isActive, dtAdded, addedByUser, requestedByUser) 
                        VALUES 
                        (@employeeNo, @adjustmentCode, @amount, @dateToAdjustment, @reason, 'Pending', 
                         @DayType, @Value, @Units, 1, NOW(), @addedByUser, @requestedByUser);
                        SELECT LAST_INSERT_ID();";

                    int newId = _db.QuerySingle<int>(sql, new
                    {
                        request.EmployeeNo,
                        request.AdjustmentCode,
                        request.Amount,
                        request.DateToAdjustment,
                        reason = request.Reason ?? "",
                        DayType = request.DayType ?? "",
                        request.Value,
                        Units = request.Units ?? "",
                        addedByUser = employeeNo,
                        requestedByUser = employeeNo
                    });

                    _auditTrail.Log("c_payable", newId, "IMPORTED",
                        $"Imported adjustment request for {request.EmployeeNo}: {request.AdjustmentCode}, Amount: {request.Amount:N2}");

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

        private HashSet<string> GetValidEmployeeNumbers()
        {
            var sql = "SELECT employeeNo FROM e_basicinfo WHERE isActive = 1";
            return _db.Query<string>(sql).ToHashSet();
        }

        // Fetches both s_adjustment and s_allowance Time Keeping entries
        private Dictionary<string, string> GetValidAdjustmentCodesAndNames()
        {
            var sql = @"
                SELECT adjustmentCode AS Code, adjustmentName AS Name 
                FROM s_adjustment WHERE isActive = 1
                UNION ALL
                SELECT allowanceCode AS Code, allowanceName AS Name 
                FROM s_allowance 
                WHERE isActive = 1 
                AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                AND allowanceName LIKE '%Time Keeping%'";

            return _db.Query<(string Code, string Name)>(sql)
                .ToDictionary(x => x.Code, x => x.Name);
        }

        private string GetTimeKeepingBasicSalaryCode()
        {
            var sql = @"SELECT adjustmentCode FROM s_adjustment 
                        WHERE isActive = 1 
                        AND adjustmentName LIKE '%Time Keeping%'
                        AND adjustmentName LIKE '%Basic Salary%'
                        LIMIT 1";
            return _db.QueryFirstOrDefault<string>(sql) ?? "";
        }

        private string GetTimeKeepingAllowanceCode()
        {
            var sql = @"SELECT allowanceCode FROM s_allowance 
                        WHERE isActive = 1 
                        AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                        AND allowanceName LIKE '%Time Keeping%'
                        AND allowanceName LIKE '%Allowance%'
                        LIMIT 1";
            return _db.QueryFirstOrDefault<string>(sql) ?? "";
        }
    }

    public class ImportAdjustmentRequestListModel
    {
        public string EmployeeNo { get; set; }
        public string AdjustmentCode { get; set; }
        public double Amount { get; set; }
        public DateTime DateToAdjustment { get; set; }
        public string Reason { get; set; }
        public string DayType { get; set; }
        public double Value { get; set; }
        public string Units { get; set; }
        public int RowNumber { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public List<string> Errors { get; set; }
        public List<ImportAdjustmentRequestListModel> Data { get; set; }
    }

    public class ImportResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> Errors { get; set; }
    }
}