using Dapper;
using KEISAN_HRIS_v2.Services.Payroll;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class PayrollRegisterPostedPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly PayrollRegisterPdfService _pdfService;

        public PayrollRegisterPostedPdfController(IDbConnection db, PayrollRegisterPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // Preview PDF in browser
        [HttpGet]
        public IActionResult PreviewPdf(string employeeNo, string cutOffType, string dateMonth, string dateYear, string statusName)
        {
            try
            {
                // Validate required parameters
                if (string.IsNullOrWhiteSpace(employeeNo) || string.IsNullOrWhiteSpace(cutOffType) ||
                    string.IsNullOrWhiteSpace(dateMonth) || string.IsNullOrWhiteSpace(dateYear))
                {
                    return BadRequest("Missing required parameters");
                }

                // Fetch payroll data - POSTED status
                var data = GetPayrollRegisterData(employeeNo, cutOffType, dateMonth, dateYear, "Posted");

                if (data == null)
                {
                    return NotFound("Posted payroll record not found");
                }

                // Generate PDF
                var pdfBytes = _pdfService.GeneratePayrollRegisterPdf(data);

                // Return PDF for inline viewing
                return File(pdfBytes, "application/pdf", $"Payslip_Posted_{employeeNo}_{dateYear}{dateMonth}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating posted payroll PDF: {ex.Message}");
                return StatusCode(500, "Error generating PDF");
            }
        }

        // Download PDF file
        [HttpGet]
        public IActionResult DownloadPdf(string employeeNo, string cutOffType, string dateMonth, string dateYear, string statusName)
        {
            try
            {
                // Validate required parameters
                if (string.IsNullOrWhiteSpace(employeeNo) || string.IsNullOrWhiteSpace(cutOffType) ||
                    string.IsNullOrWhiteSpace(dateMonth) || string.IsNullOrWhiteSpace(dateYear))
                {
                    return BadRequest("Missing required parameters");
                }

                // Fetch payroll data - POSTED status
                var data = GetPayrollRegisterData(employeeNo, cutOffType, dateMonth, dateYear, "Posted");

                if (data == null)
                {
                    return NotFound("Posted payroll record not found");
                }

                // Generate PDF
                var pdfBytes = _pdfService.GeneratePayrollRegisterPdf(data);

                // Return PDF for download
                return File(pdfBytes, "application/pdf", $"Payslip_Posted_{employeeNo}_{dateYear}{dateMonth}.pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating posted payroll PDF: {ex.Message}");
                return StatusCode(500, "Error generating PDF");
            }
        }

        // Fetch POSTED payroll register data from database
        private PayrollRegisterPdfData? GetPayrollRegisterData(string employeeNo, string cutOffType, string dateMonth, string dateYear, string statusName)
        {
            try
            {
                var sql = @"
                    SELECT 
                        p.employeeNo AS employeeNo,
                        CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,

                        CAST(IFNULL(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))         AS dailyRate,
                        CAST(IFNULL(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))      AS basicPaySemi,

                        CAST(IFNULL(p.nonBasicPay,0)  AS DECIMAL(10,2))            AS nonBasicPay,

                        CAST(IFNULL(p.totalAmountLate,0)  AS DECIMAL(10,2))        AS amountLate,
                        CAST(IFNULL(p.totalAmountUndertime,0) AS DECIMAL(10,2))    AS amountUndertime,
                        CAST(IFNULL(p.absentAmount,0)   AS DECIMAL(10,2))          AS absentAmount,

                        CAST(IFNULL(p.presentCount,0) AS DECIMAL(10,2))            AS presentCount,
                        CAST(IFNULL(p.totalRenderLate,0) AS DECIMAL(10,2))         AS renderLate,
                        CAST(IFNULL(p.totalRenderUndertime,0) AS DECIMAL(10,2))    AS renderUndertime,
                        CAST(IFNULL(p.absentCount,0) AS DECIMAL(10,2))             AS absentCount,

                        CAST(IFNULL(p.renderOT,0) AS DECIMAL(10,2))                AS renderOT,
                        CAST(IFNULL(p.amountOT,0)  AS DECIMAL(10,2))               AS amountOT,

                        CAST(IFNULL(p.totalAllowance,0)  AS DECIMAL(10,2))         AS totalAllowance,
                        CAST(IFNULL(p.otherIncome,0)  AS DECIMAL(10,2))            AS otherIncome,
                        CAST(IFNULL(p.otherEmployeePayable,0) AS DECIMAL(10,2))    AS otherEmployeePayable,

                        CAST(IFNULL(p.deductionSSSemployee,0) AS DECIMAL(10,2))    AS deductionSSSemployee,
                        CAST(IFNULL(p.deductionPHIemployee,0) AS DECIMAL(10,2))    AS deductionPHIemployee,
                        CAST(IFNULL(p.deductionPIFemployee,0) AS DECIMAL(10,2))    AS deductionPIFemployee,

                        CAST(IFNULL(p.cashadvance,0) AS DECIMAL(10,2))      AS cashadvance,
                        CAST(IFNULL(p.hdmfLoan,0) AS DECIMAL(10,2))         AS hdmfLoan,
                        CAST(IFNULL(p.hdmfCalamity,0) AS DECIMAL(10,2))     AS hdmfCalamity,
                        CAST(IFNULL(p.sssLoan,0) AS DECIMAL(10,2))          AS sssLoan,
                        CAST(IFNULL(p.sssCalamity,0) AS DECIMAL(10,2))      AS sssCalamity,
                        CAST(IFNULL(p.otherLoan,0) AS DECIMAL(10,2))        AS otherLoan,

                        CAST(IFNULL(p.withHeldTax,0) AS DECIMAL(10,2))      AS withHeldTax,

                        CAST(IFNULL(CAST(AES_DECRYPT(p.grossIncome,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))   AS grossIncome,
                        CAST(IFNULL(p.totalDeduction,0) AS DECIMAL(10,2))                                               AS totalDeduction,
                        CAST(IFNULL(CAST(AES_DECRYPT(p.totalNetPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))   AS totalNetPay,

                        CAST(IFNULL(p.healthcard,0) AS DECIMAL(10,2))               AS healthcard,
                        CAST(IFNULL(p.parking,0) AS DECIMAL(10,2))                  AS parking,
                        CAST(IFNULL(p.meals,0) AS DECIMAL(10,2))                    AS meals,
                        CAST(IFNULL(p.fixedOthers,0) AS DECIMAL(10,2))              AS fixedOthers,
                        CAST(IFNULL(p.totalFixedDeduction,0) AS DECIMAL(10,2))      AS totalFixedDeduction,
                        CAST(IFNULL(p.additionalMbos,0) AS DECIMAL(10,2))           AS additionalMbos,
                        CAST(IFNULL(p.totalMBOS,0) AS DECIMAL(10,2))                AS totalMBOS,

                        IFNULL(p.bankCode,'')       AS bankCode,
                        IFNULL(p.accountNo,'')      AS accountNo,

                        IFNULL(pay.sssNo,'')        AS SSSNumber,
                        IFNULL(pay.tinNo,'')        AS TINNumber,
                        IFNULL(pay.philHealthNo,'') AS PhilHealthNumber,
                        IFNULL(pay.hdmfNo,'')       AS PagIbigNumber,

                        CONCAT(DATE_FORMAT(p.dateFrom,'%m/%d/%Y'), ' - ', DATE_FORMAT(p.dateTo,'%m/%d/%Y')) AS payPeriod,
                        IFNULL(dep.departmentName, 'N/A') AS department

                    FROM p_biometrics p
                    JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                    LEFT JOIN e_payrolldetails pay ON pay.employeeNo = p.employeeNo
                    LEFT JOIN s_department dep ON dep.departmentCode = p.departmentCode

                    WHERE p.isActive = 1 
                    AND p.employeeNo = @employeeNo
                    AND p.cutOffType = @cutOffType
                    AND p.dateMonth = @dateMonth
                    AND p.dateYear = @dateYear
                    AND p.statusName = 'Posted'
                    LIMIT 1";

                return _db.QueryFirstOrDefault<PayrollRegisterPdfData>(sql, new
                {
                    employeeNo,
                    cutOffType,
                    dateMonth,
                    dateYear
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching posted payroll data: {ex.Message}");
                return null;
            }
        }
    }
}