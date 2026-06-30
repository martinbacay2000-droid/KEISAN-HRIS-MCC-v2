using Dapper;
using KEISAN_HRIS_v2.Services.Payroll;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class LastPayPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly LastPayPdfService _pdfService;

        public LastPayPdfController(IDbConnection db, LastPayPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // ── Preview in browser ────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult PreviewPdf(
            string employeeNo,
            double amountLastCutoff = 0,
            double amountAdjustment = 0,
            double amount13th = 0,
            double amountTax = 0,
            double amountSL = 0,
            double amountVL = 0,
            double lastPayAmount = 0,
            string statusName = "Open",
            bool includeLastCutoff = false,
            bool includeAdjustment = false,
            bool include13th = false,
            bool includeTax = false,
            bool includeSL = false,
            bool includeVL = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeNo))
                    return BadRequest("Missing required parameter: employeeNo");

                var data = GetLastPayData(employeeNo);

                if (data == null)
                    return NotFound("Last pay record not found.");

                // Override DB amounts with live screen values passed from the UI
                data.AmountLastCutoff = amountLastCutoff;
                data.AmountAdjustment = amountAdjustment;
                data.Amount13thMonth = amount13th;
                data.AmountTaxRefund = amountTax;
                data.AmountSL = amountSL;
                data.AmountVL = amountVL;
                data.LastPayAmount = lastPayAmount;
                data.LastPayStatus = statusName;
                data.IncludeLastCutoff = includeLastCutoff;
                data.IncludeAdjustment = includeAdjustment;
                data.Include13thMonth = include13th;
                data.IncludeTax = includeTax;
                data.IncludeSL = includeSL;
                data.IncludeVL = includeVL;

                var pdfBytes = _pdfService.GenerateLastPayPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating Last Pay PDF: {ex.Message}");
                return StatusCode(500, "Error generating PDF");
            }
        }

        // ── Download as file ─────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult DownloadPdf(string employeeNo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeNo))
                    return BadRequest("Missing required parameter: employeeNo");

                var data = GetLastPayData(employeeNo);

                if (data == null)
                    return NotFound("Last pay record not found.");

                var pdfBytes = _pdfService.GenerateLastPayPdf(data);
                return File(pdfBytes, "application/pdf", $"LastPay_{employeeNo}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating Last Pay PDF: {ex.Message}");
                return StatusCode(500, "Error generating PDF");
            }
        }

        // ── Data fetch ────────────────────────────────────────────────────────────
        private LastPayPdfData? GetLastPayData(string employeeNo)
        {
            try
            {
                const string sql = @"
                    SELECT
                        b.employeeNo        AS EmployeeNo,
                        CONCAT(
                            IFNULL(b.firstName, ''), ' ',
                            IFNULL(CONCAT(b.middleName, ' '), ''),
                            IFNULL(b.lastName, '')
                        )                   AS EmployeeName,
                        DATE_FORMAT(b.dateHired,             '%m/%d/%Y') AS DateHired,
                        DATE_FORMAT(b.dateOfEmpTermInitial,  '%m/%d/%Y') AS DateResigned,
                        b.employmentStatus  AS EmpStatus,

                        IFNULL(r.amount_lastcutoff, 0)  AS AmountLastCutoff,
                        IFNULL(r.amount_adjustment,  0)  AS AmountAdjustment,
                        IFNULL(r.amount_13thmonth,   0)  AS Amount13thMonth,
                        IFNULL(r.amount_taxRefund,   0)  AS AmountTaxRefund,
                        IFNULL(r.amount_vl,          0)  AS AmountVL,
                        IFNULL(r.amount_sl,          0)  AS AmountSL,
                        IFNULL(r.lastpayAmount,      0)  AS LastPayAmount,
                        IFNULL(r.statusName,     'Open')  AS LastPayStatus,

                        IFNULL(sp.positionName,      'N/A') AS PositionName,
                        IFNULL(dep.departmentName,   'N/A') AS Department

                    FROM e_basicinfo b
                    LEFT JOIN rq_lastpay   r   ON r.employeeNo   = b.employeeNo
                    LEFT JOIN s_position   sp  ON sp.positionCode = b.positionCode
                    LEFT JOIN s_department dep ON dep.departmentCode = b.departmentCode

                    WHERE b.employeeNo = @employeeNo
                    LIMIT 1";

                return _db.QueryFirstOrDefault<LastPayPdfData>(sql, new { employeeNo });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching last pay data: {ex.Message}");
                return null;
            }
        }
    }
}