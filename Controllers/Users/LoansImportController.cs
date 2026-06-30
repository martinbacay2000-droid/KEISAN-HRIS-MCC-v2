using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    public class LoansImportController : Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LoansImportController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        // Returns the import view
        public IActionResult Index()
        {
            return View("~/Views/Users/Partials/_LoansImport.cshtml");
        }

        // Downloads the Excel template for importing loans
        [HttpGet]
        public IActionResult DownloadTemplate()
        {
            try
            {
                var excelFile = GenerateTemplateFile();
                var fileName = $"Loans_Template_{DateTime.Now:yyyyMMdd}.xlsx";

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

        // Imports validated loans into the database
        [HttpPost]
        public IActionResult ImportData([FromBody] List<ImportLoanModel> loans)
        {
            try
            {
                if (loans == null || !loans.Any())
                    return BadRequest(new { success = false, message = "No data to import" });

                var results = ProcessImportData(loans);

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
            var ws = package.Workbook.Worksheets.Add("Loan Import");

            // Title
            ws.Cells[1, 1].Value = "Loan Import Template";
            ws.Cells[1, 1, 1, 9].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

            // Instructions
            ws.Cells[2, 1].Value = "Instructions: Fill in the required fields below. Fields marked with * are mandatory.";
            ws.Cells[2, 1, 2, 9].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 10;
            ws.Cells[2, 1].Style.Font.Italic = true;

            // Headers (Row 4)
            var headers = new[]
            {
                "Employee No*",
                "Loan Type*",
                "Principal Amount*",
                "Months to Pay*",
                "Deduction per Cutoff*",
                "Date Granted* (YYYY-MM-DD)",
                "Deduction Start Date* (YYYY-MM-DD)",
                "Deduction Schedule*",
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

            // Example rows (Row 5-7)
            ws.Cells[5, 1].Value = "R00001";
            ws.Cells[5, 2].Value = "SSS";
            ws.Cells[5, 3].Value = 50000.00;
            ws.Cells[5, 4].Value = 24;
            ws.Cells[5, 5].Value = 2500.00;
            ws.Cells[5, 6].Value = "2026-01-15";
            ws.Cells[5, 7].Value = "2026-02-01";
            ws.Cells[5, 8].Value = "1st Cutoff";
            ws.Cells[5, 9].Value = "Sample loan";

            ws.Cells[6, 1].Value = "R00001";
            ws.Cells[6, 2].Value = "SSS";
            ws.Cells[6, 3].Value = 1000.00;
            ws.Cells[6, 4].Value = 12;
            ws.Cells[6, 5].Value = 250.00;
            ws.Cells[6, 6].Value = "2026-01-16";
            ws.Cells[6, 7].Value = "2026-02-02";
            ws.Cells[6, 8].Value = "1st and 2nd Cutoff";
            ws.Cells[6, 9].Value = "Sample loan 1";

            ws.Cells[7, 1].Value = "R00002";
            ws.Cells[7, 2].Value = "SSS";
            ws.Cells[7, 3].Value = 7000.00;
            ws.Cells[7, 4].Value = 5;
            ws.Cells[7, 5].Value = 2500.00;
            ws.Cells[7, 6].Value = "2026-01-17";
            ws.Cells[7, 7].Value = "2026-02-03";
            ws.Cells[7, 8].Value = "2nd Cutoff";
            ws.Cells[7, 9].Value = "Sample loan 2";

            // Notes section
            ws.Cells[9, 1].Value = "Notes:";
            ws.Cells[9, 1].Style.Font.Bold = true;
            ws.Cells[10, 1].Value = "• Employee No and Loan Type (Code or Name) must exist in the system";
            ws.Cells[11, 1].Value = "• Dates must be in YYYY-MM-DD format";
            ws.Cells[12, 1].Value = "• Deduction Start Date must be equal to or later than Date Granted";
            ws.Cells[13, 1].Value = "• Deduction Schedule must be: 1st Cutoff, 2nd Cutoff, or 1st and 2nd Cutoff";
            ws.Cells[14, 1].Value = "• Principal Amount, Months to Pay, and Deduction per Cutoff must be positive numbers";
            ws.Cells[15, 1].Value = "• All imported loans will be set to active status";

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
            var result = new ValidationResult { Errors = new List<string>(), Data = new List<ImportLoanModel>() };

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
                int startRow = 5; // Start reading from row 5 (first data row after header)
                int rowCount = ws.Dimension?.Rows ?? 0;

                if (rowCount < startRow)
                {
                    result.Message = "No data rows found in the Excel file";
                    return result;
                }

                // Get valid employees and loan types for validation
                var validEmployees = GetValidEmployeeNumbers();
                var validLoanTypes = GetValidLoanTypesCodesAndNames();

                // Valid schedules to match the exact values
                var validSchedules = new[] { "1st Cutoff", "2nd Cutoff", "1st and 2nd Cutoff" };

                // First pass: count actual data rows (skip empty rows)
                int actualDataRows = 0;
                for (int row = startRow; row <= rowCount; row++)
                {
                    var employeeNo = ws.Cells[row, 1].Text?.Trim();
                    var loanTypeInput = ws.Cells[row, 2].Text?.Trim();

                    // Skip completely empty rows
                    if (string.IsNullOrWhiteSpace(employeeNo) && string.IsNullOrWhiteSpace(loanTypeInput))
                        continue;

                    actualDataRows++;
                }

                result.TotalRows = actualDataRows;

                // Second pass: validate actual data
                for (int row = startRow; row <= rowCount; row++)
                {
                    var rowErrors = new List<string>();
                    var employeeNo = ws.Cells[row, 1].Text?.Trim();
                    var loanTypeInput = ws.Cells[row, 2].Text?.Trim();
                    var principalAmountText = ws.Cells[row, 3].Text?.Trim();
                    var monthsToPayText = ws.Cells[row, 4].Text?.Trim();
                    var amortizationAmountText = ws.Cells[row, 5].Text?.Trim();
                    var dateGrantedText = ws.Cells[row, 6].Text?.Trim();
                    var deductionStartDateText = ws.Cells[row, 7].Text?.Trim();
                    var deductionSchedule = ws.Cells[row, 8].Text?.Trim();
                    var remarks = ws.Cells[row, 9].Text?.Trim();

                    // Skip completely empty rows
                    if (string.IsNullOrWhiteSpace(employeeNo) && string.IsNullOrWhiteSpace(loanTypeInput))
                        continue;

                    // Validate required fields
                    if (string.IsNullOrWhiteSpace(employeeNo))
                        rowErrors.Add("Employee No is required");
                    else if (!validEmployees.Contains(employeeNo))
                        rowErrors.Add($"Employee No '{employeeNo}' not found");

                    string loanCode = null;
                    if (string.IsNullOrWhiteSpace(loanTypeInput))
                    {
                        rowErrors.Add("Loan Type is required");
                    }
                    else
                    {
                        // Try to find by code or name
                        loanCode = FindLoanCode(loanTypeInput, validLoanTypes);
                        if (loanCode == null)
                            rowErrors.Add($"Loan Type '{loanTypeInput}' not found");
                    }

                    if (string.IsNullOrWhiteSpace(principalAmountText))
                        rowErrors.Add("Principal Amount is required");
                    else if (!decimal.TryParse(principalAmountText, out decimal principalAmount) || principalAmount <= 0)
                        rowErrors.Add("Principal Amount must be a positive number");

                    if (string.IsNullOrWhiteSpace(monthsToPayText))
                        rowErrors.Add("Months to Pay is required");
                    else if (!int.TryParse(monthsToPayText, out int monthsToPay) || monthsToPay <= 0)
                        rowErrors.Add("Months to Pay must be a positive number");

                    if (string.IsNullOrWhiteSpace(amortizationAmountText))
                        rowErrors.Add("Deduction per Cutoff is required");
                    else if (!decimal.TryParse(amortizationAmountText, out decimal amortizationAmount) || amortizationAmount <= 0)
                        rowErrors.Add("Deduction per Cutoff must be a positive number");

                    DateTime dateGranted = default;
                    if (string.IsNullOrWhiteSpace(dateGrantedText))
                        rowErrors.Add("Date Granted is required");
                    else if (!DateTime.TryParse(dateGrantedText, out dateGranted))
                        rowErrors.Add("Invalid Date Granted format (use YYYY-MM-DD)");

                    DateTime deductionStartDate = default;
                    if (string.IsNullOrWhiteSpace(deductionStartDateText))
                        rowErrors.Add("Deduction Start Date is required");
                    else if (!DateTime.TryParse(deductionStartDateText, out deductionStartDate))
                        rowErrors.Add("Invalid Deduction Start Date format (use YYYY-MM-DD)");
                    else if (dateGranted != default && deductionStartDate < dateGranted)
                        rowErrors.Add("Deduction Start Date cannot be earlier than Date Granted");

                    if (string.IsNullOrWhiteSpace(deductionSchedule))
                        rowErrors.Add("Deduction Schedule is required");
                    else if (!validSchedules.Contains(deductionSchedule))
                        rowErrors.Add("Deduction Schedule must be: 1st Cutoff, 2nd Cutoff, or 1st and 2nd Cutoff");

                    if (rowErrors.Any())
                    {
                        result.Errors.Add($"Row {row}: {string.Join(", ", rowErrors)}");
                    }
                    else
                    {
                        result.Data.Add(new ImportLoanModel
                        {
                            EmployeeNo = employeeNo,
                            LoanCode = loanCode,
                            PrincipalAmount = decimal.Parse(principalAmountText),
                            MonthsToPay = int.Parse(monthsToPayText),
                            AmortizationAmount = decimal.Parse(amortizationAmountText),
                            DateGranted = dateGranted,
                            DeductionStartDate = deductionStartDate,
                            DeductionSchedule = deductionSchedule,
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

        // Find loan code by code or name
        private string FindLoanCode(string input, Dictionary<string, string> validLoanTypes)
        {
            // First try exact match with code
            if (validLoanTypes.ContainsKey(input))
                return input;

            // Then try case-insensitive match with name
            var matchByName = validLoanTypes.FirstOrDefault(x =>
                x.Value.Equals(input, StringComparison.OrdinalIgnoreCase));

            if (!matchByName.Equals(default(KeyValuePair<string, string>)))
                return matchByName.Key;

            return null;
        }

        // Processes and imports validated data
        private ImportResult ProcessImportData(List<ImportLoanModel> loans)
        {
            var result = new ImportResult { Errors = new List<string>() };
            var userName = User.Identity?.Name ?? "SYSTEM";

            foreach (var loan in loans)
            {
                try
                {
                    var sql = @"
                        INSERT INTO e_loan (
                            employeeNo,
                            loanCode,
                            dateGranted,
                            principalAmount,
                            totalLoanAmount,
                            monthsToPay,
                            deductionStartDate,
                            amortizationAmount,
                            deductionSchedule,
                            remarks,
                            dtAdded,
                            addedByUser,
                            isActive
                        )
                        VALUES (
                            @employeeNo,
                            @loanCode,
                            @dateGranted,
                            @principalAmount,
                            @totalLoanAmount,
                            @monthsToPay,
                            @deductionStartDate,
                            @amortizationAmount,
                            @deductionSchedule,
                            @remarks,
                            NOW(),
                            @addedByUser,
                            1
                        );
                        SELECT LAST_INSERT_ID();";

                    int newId = _db.QuerySingle<int>(sql, new
                    {
                        loan.EmployeeNo,
                        loan.LoanCode,
                        loan.DateGranted,
                        loan.PrincipalAmount,
                        totalLoanAmount = loan.PrincipalAmount,
                        loan.MonthsToPay,
                        loan.DeductionStartDate,
                        loan.AmortizationAmount,
                        loan.DeductionSchedule,
                        remarks = loan.Remarks ?? "",
                        addedByUser = userName
                    });

                    _auditTrail.Log("e_loan", newId, "IMPORTED",
                        $"Imported loan for {loan.EmployeeNo}: {loan.LoanCode}, Principal: {loan.PrincipalAmount:N2}, Months: {loan.MonthsToPay}");

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    result.Errors.Add($"Row {loan.RowNumber}: {ex.Message}");
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

        // Get both loan codes and names
        private Dictionary<string, string> GetValidLoanTypesCodesAndNames()
        {
            var sql = "SELECT loanCode, loanName FROM s_loan WHERE isActive = 1";
            return _db.Query<(string Code, string Name)>(sql)
                .ToDictionary(x => x.Code, x => x.Name);
        }
    }

    // Supporting classes
    public class ImportLoanModel
    {
        public string EmployeeNo { get; set; }
        public string LoanCode { get; set; }
        public decimal PrincipalAmount { get; set; }
        public int MonthsToPay { get; set; }
        public decimal AmortizationAmount { get; set; }
        public DateTime DateGranted { get; set; }
        public DateTime DeductionStartDate { get; set; }
        public string DeductionSchedule { get; set; }
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
        public List<ImportLoanModel> Data { get; set; }
    }

    public class ImportResult
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<string> Errors { get; set; }
    }
}