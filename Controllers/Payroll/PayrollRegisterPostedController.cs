using Dapper;
using KEISAN_HRIS_v2.Models.Payroll;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Data;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    [ModuleAuthorize("TpostedPayrollRegisterM")]
    public class PayrollRegisterPostedController : Controller
    {
        private readonly IDbConnection _db;
        private IEmailService _emailService;

        public PayrollRegisterPostedController(IDbConnection db, IEmailService emailService)
        {
            _db = db;
            _emailService = (IEmailService)emailService;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payroll/PayrolLRegisterPosted.cshtml");
        }

        [HttpGet]
        public JsonResult GetPayrollList(string branch, string department, string cutOffType, string dateYear, string dateMonth)
        {
            var sb = new StringBuilder(@"
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
                    CAST(IFNULL(p.cashadvance,0) AS DECIMAL(10,2))             AS cashadvance,
                    CAST(IFNULL(p.hdmfLoan,0) AS DECIMAL(10,2))                AS hdmfLoan,
                    CAST(IFNULL(p.hdmfCalamity,0) AS DECIMAL(10,2))            AS hdmfCalamity,
                    CAST(IFNULL(p.sssLoan,0) AS DECIMAL(10,2))                 AS sssLoan,
                    CAST(IFNULL(p.sssCalamity,0) AS DECIMAL(10,2))             AS sssCalamity,
                    CAST(IFNULL(p.otherLoan,0) AS DECIMAL(10,2))               AS otherLoan,
                    CAST(IFNULL(p.withHeldTax,0) AS DECIMAL(10,2))             AS withHeldTax,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.grossIncome,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))   AS grossIncome,
                    CAST(IFNULL(p.totalDeduction,0) AS DECIMAL(10,2))                                               AS totalDeduction,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.totalNetPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))   AS totalNetPay,
                    CAST(IFNULL(p.healthcard,0) AS DECIMAL(10,2))              AS healthcard,
                    CAST(IFNULL(p.parking,0) AS DECIMAL(10,2))                 AS parking,
                    CAST(IFNULL(p.meals,0) AS DECIMAL(10,2))                   AS meals,
                    CAST(IFNULL(p.fixedOthers,0) AS DECIMAL(10,2))             AS fixedOthers,
                    CAST(IFNULL(p.totalFixedDeduction,0) AS DECIMAL(10,2))     AS totalFixedDeduction,
                    CAST(IFNULL(p.additionalMbos,0) AS DECIMAL(10,2))          AS additionalMbos,
                    CAST(IFNULL(p.totalMBOS,0) AS DECIMAL(10,2))               AS totalMBOS,
                    IFNULL(p.bankCode,'')   AS bankCode,
                    IFNULL(p.accountNo,'')  AS accountNo,
                    pay.sssNo,
                    pay.tinNo,
                    pay.philHealthNo,
                    pay.hdmfNo,
                    CONCAT(DATE_FORMAT(p.dateFrom,'%m/%d/%Y'), ' - ', DATE_FORMAT(p.dateTo,'%m/%d/%Y')) AS payPeriod,
                    dep.departmentName

                FROM p_biometrics p
                JOIN e_payrolldetails pay ON pay.employeeNo = p.employeeNo
                JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                LEFT JOIN s_department dep ON dep.departmentCode = p.departmentCode

                WHERE p.isActive = 1
                AND p.statusName = 'Posted'
            ");

            var parameters = new DynamicParameters();

            sb.Append(" AND p.dateYear = @dateYear ");
            parameters.Add("@dateYear", dateYear);

            if (!string.IsNullOrWhiteSpace(dateMonth))
            {
                sb.Append(" AND p.dateMonth = @dateMonth ");
                parameters.Add("@dateMonth", dateMonth);
            }

            if (!string.IsNullOrWhiteSpace(cutOffType))
            {
                sb.Append(" AND p.cutOffType = @cutOffType ");
                parameters.Add("@cutOffType", cutOffType);
            }

            if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND p.branchCode = @branch ");
                parameters.Add("@branch", branch);
            }

            if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND p.departmentCode = @department ");
                parameters.Add("@department", department);
            }

            sb.Append(" ORDER BY b.lastName ; ");

            var results = _db.Query<PayrollProcessModel>(sb.ToString(), parameters).ToList();
            return Json(new { data = results });
        }

        [HttpPost]
        public JsonResult PostPayroll([FromBody] PayrollProcessModel model)
        {
            try
            {
                string updateSql = @"
                    UPDATE p_biometrics 
                    SET statusName = 'Posted', dtLastModified = Now(), lastModifiedByUser = 'SYSTEM'
                    WHERE dateMonth = @datemonth
                    AND dateYear = @dateyear
                    AND cutOffType = @cutofftype
                    AND isActive = 1;

                    UPDATE p_biometricsline 
                    SET statusName = 'Posted', dtLastModified = Now(), lastModifiedByUser = 'SYSTEM'
                    WHERE dateMonth = @datemonth
                    AND dateYear = @dateyear
                    AND cutOffType = @cutofftype
                    AND isActive = 1;

                    UPDATE c_payable 
                    SET statusName = 'Posted', dtLastModified = Now(), lastModifiedByUser = 'SYSTEM'
                    WHERE dateMonth = @datemonth
                    AND dateYear = @dateyear
                    AND cutOffType = @cutofftype
                    AND isActive = 1 AND statusName = 'Processed';

                    UPDATE c_receivable 
                    SET statusName = 'Posted', dtLastModified = Now(), lastModifiedByUser = 'SYSTEM'
                    WHERE dateMonth = @datemonth
                    AND dateYear = @dateyear
                    AND cutOffType = @cutofftype
                    AND isActive = 1 AND statusName = 'Processed';
                ";

                _db.Execute(updateSql, new { datemonth = model.dateMonth, dateyear = model.dateYear, cutofftype = model.cutOffType });

                string employeeEmail = _emailService.GetApproverEmails(model.employeeNo, 2).ToString();
                string datemonth = model.dateMonth;
                string cutoff = model.cutOffType;
                string dateyear = model.dateYear;

                _emailService.SendPayslipInEmailAsync("Payslip for ", employeeEmail, datemonth, cutoff, dateyear);

                return Json(new { success = true, message = "Payroll Register Posted." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error in posting payroll: {ex.Message}" });
            }
        }
    }
}