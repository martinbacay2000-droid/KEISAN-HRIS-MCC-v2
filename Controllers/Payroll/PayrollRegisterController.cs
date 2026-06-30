using Dapper;
using KEISAN_HRIS_v2.Models.Payroll;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text;
using static Mysqlx.Expect.Open.Types.Condition.Types;


namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    [ModuleAuthorize("TopenPayrollRegisterM")]
    public class PayrollRegisterController : BaseController
    {
        private readonly IDbConnection _db;

        public PayrollRegisterController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payroll/PayrolLRegister.cshtml");

        }


        //private IDbConnection GetConnection()
        //{
        //    return _db;
        //}


        // CRUD Operations for Leave Request List
        [HttpGet]
        public JsonResult GetPayrollList(string branch, string department, string cutOffType, string dateYear, string dateMonth, string statusName)
        {
            var sb = new StringBuilder(@"
                SELECT 
                    p.employeeNo AS employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,

                    CAST(IFNULL(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))         AS dailyRate,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))      AS basicPaySemi,

                    CAST(IFNULL(p.amountL,0)  AS DECIMAL(10,2))         AS amountL,
                    CAST(IFNULL(p.amountNSDL,0)  AS DECIMAL(10,2))      AS amountNSDL,
                    CAST(IFNULL(p.amountOTL,0)  AS DECIMAL(10,2))       AS amountOTL,
                    CAST(IFNULL(p.amountREST,0)  AS DECIMAL(10,2))      AS amountREST,
                    CAST(IFNULL(p.amountNSD,0)  AS DECIMAL(10,2))       AS amountNSD,
                    CAST(IFNULL(p.amountNSDOT,0)  AS DECIMAL(10,2))     AS amountNSDOT,
                    CAST(IFNULL(p.amountOT,0)  AS DECIMAL(10,2))        AS amountOT,
                    CAST(IFNULL(p.amountS,0)  AS DECIMAL(10,2))         AS amountS,
                    CAST(IFNULL(p.amountNSDS,0)  AS DECIMAL(10,2))      AS amountNSDS,
                    CAST(IFNULL(p.amountOTS,0)  AS DECIMAL(10,2))       AS amountOTS,

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

                    CAST(IFNULL(p.reg_basic_al,0) AS DECIMAL(10,2))             AS reg_basic_al,
                    CAST(IFNULL(p.tardy_al,0) AS DECIMAL(10,2))                 AS tardy_al,
                    CAST(IFNULL(p.undertime_al,0) AS DECIMAL(10,2))             AS undertime_al,
                    CAST(IFNULL(p.absent_al,0) AS DECIMAL(10,2))                AS absent_al,
                    CAST(IFNULL(p.salary_adjustment_al,0) AS DECIMAL(10,2))     AS salary_adjustment_al,

                    CAST(IFNULL(p.lh_basic_al,0) AS DECIMAL(10,2))              AS lh_basic_al,
                    CAST(IFNULL(p.lh_nd_al,0) AS DECIMAL(10,2))                 AS lh_nd_al,
                    CAST(IFNULL(p.lh_ot_al,0) AS DECIMAL(10,2))                 AS lh_ot_al,

                    CAST(IFNULL(p.rd_basic_al,0) AS DECIMAL(10,2))              AS rd_basic_al,

                    CAST(IFNULL(p.reg_nd_al,0) AS DECIMAL(10,2))                AS reg_nd_al,
                    CAST(IFNULL(p.reg_ndot_al,0) AS DECIMAL(10,2))              AS reg_ndot_al,
                    CAST(IFNULL(p.reg_ot_al,0) AS DECIMAL(10,2))                AS reg_ot_al,

                    CAST(IFNULL(p.sh_basic_al,0) AS DECIMAL(10,2))              AS sh_basic_al,
                    CAST(IFNULL(p.sh_nd_al,0) AS DECIMAL(10,2))                 AS sh_nd_al,
                    CAST(IFNULL(p.sh_ndot_al,0) AS DECIMAL(10,2))               AS sh_ndot_al,
                    CAST(IFNULL(p.sh_ot_al,0) AS DECIMAL(10,2))                 AS sh_ot_al,

                    CAST(IFNULL(p.totalAllowance,0)  AS DECIMAL(10,2))         AS totalAllowance,
                    CAST(IFNULL(p.otherIncome,0)  AS DECIMAL(10,2))            AS otherIncome,
                    CAST(IFNULL(p.otherEmployeePayable,0) AS DECIMAL(10,2))    AS otherEmployeePayable,
                    CAST(IFNULL(p.otherEmployeeReceivable,0) AS DECIMAL(10,2))    AS otherEmployeeReceivable,

                    CAST(IFNULL(p.deductionSSSemployee,0) AS DECIMAL(10,2))    AS deductionSSSemployee,
                    CAST(IFNULL(p.deductionWISPemployee,0) AS DECIMAL(10,2))   AS deductionWISPemployee,
                    CAST(IFNULL(p.deductionPHIemployee,0) AS DECIMAL(10,2))    AS deductionPHIemployee,
                    CAST(IFNULL(p.deductionPIFemployee,0) AS DECIMAL(10,2))    AS deductionPIFemployee,

                    CAST(IFNULL(p.cashadvance,0) AS DECIMAL(10,2))      AS cashadvance,
                    CAST(IFNULL(p.hdmfLoan,0) AS DECIMAL(10,2))         AS hdmfLoan,
                    CAST(IFNULL(p.hdmfCalamity,0) AS DECIMAL(10,2))     AS hdmfCalamity,
                    CAST(IFNULL(p.sssLoan,0) AS DECIMAL(10,2))          AS sssLoan,
                    CAST(IFNULL(p.sssCalamity,0) AS DECIMAL(10,2))      AS sssCalamity,
                    CAST(IFNULL(p.csbLoan,0) AS DECIMAL(10,2))          AS csbLoan,
                    CAST(IFNULL(p.hmoLoan,0) AS DECIMAL(10,2))          AS hmoLoan,
                    CAST(IFNULL(p.employeeLedger,0) AS DECIMAL(10,2))   AS employeeLedger,

                    CAST(IFNULL(p.otherLoan1,0) AS DECIMAL(10,2))        AS otherLoan1,
                    CAST(IFNULL(p.otherLoan2,0) AS DECIMAL(10,2))        AS otherLoan2,
                    CAST(IFNULL(p.otherLoan3,0) AS DECIMAL(10,2))        AS otherLoan3,
                    CAST(IFNULL(p.otherLoan4,0) AS DECIMAL(10,2))        AS otherLoan4,

                    CAST(IFNULL(p.withHeldTax,0) AS DECIMAL(10,2))       AS withHeldTax,

                    CAST(IFNULL(CAST(AES_DECRYPT(p.grossIncome,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))   AS grossIncome,
                    CAST(IFNULL(p.totalDeduction,0) AS DECIMAL(10,2))                                               AS totalDeduction,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.totalNetPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))   AS totalNetPay,

                    IFNULL(p.bankCode,'')       AS bankCode,
                    IFNULL(p.accountNo,'')      AS accountNo,

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
            ");

            var p = new DynamicParameters();

            sb.Append(" AND p.dateYear = @dateYear ");
            p.Add("@dateYear", dateYear);


            if (!string.IsNullOrWhiteSpace(statusName))
            {
                sb.Append(" AND p.statusName = @statusname ");
                p.Add("@statusname", statusName);
            }

            if (!string.IsNullOrWhiteSpace(dateMonth))
            {
                sb.Append(" AND p.dateMonth = @dateMonth ");
                p.Add("@dateMonth", dateMonth);
            }

            if (!string.IsNullOrWhiteSpace(cutOffType))
            {
                sb.Append(" AND p.cutOffType = @cutOffType ");
                p.Add("@cutOffType", cutOffType);
            }

            if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND p.branchCode = @branch ");
                p.Add("@branch", branch);
            }

            if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND p.departmentCode = @department ");
                p.Add("@department", department);
            }

            //sb.Append(" GROUP BY p.employeeNo ORDER BY b.lastName ; ");
            sb.Append(" ORDER BY b.lastName ; ");

            var requests = _db.Query<PayrollProcessModel>(sb.ToString(), p).ToList();
            return Json(new { data = requests });
        }

        private const string FundingAccountNo = "0000067821527";

        [HttpGet]
        public IActionResult GenerateCpayFile(string branch, string department, string cutOffType, string dateYear, string dateMonth, string statusName)
        {
            if (string.IsNullOrWhiteSpace(cutOffType) || string.IsNullOrWhiteSpace(dateYear) || string.IsNullOrWhiteSpace(dateMonth))
                return Json(new { success = false, message = "Missing required parameters." });

            var sql = new StringBuilder(@"
                SELECT 
                    p.accountNo,
                    p.bankCode,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.totalNetPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS totalNetPay
                FROM p_biometrics p
                WHERE p.isActive = 1
                AND p.accountNo IS NOT NULL
                AND p.accountNo != ''
                AND (
                    p.bankCode = 'Security Bank'
                    OR p.bankCode IS NULL
                    OR p.bankCode = ''
                )
            ");

            var p = new DynamicParameters();

            sql.Append(" AND p.dateYear = @dateYear ");
            p.Add("@dateYear", dateYear);

            sql.Append(" AND p.dateMonth = @dateMonth ");
            p.Add("@dateMonth", dateMonth);

            sql.Append(" AND p.cutOffType = @cutOffType ");
            p.Add("@cutOffType", cutOffType);

            if (!string.IsNullOrWhiteSpace(statusName))
            {
                sql.Append(" AND p.statusName = @statusName ");
                p.Add("@statusName", statusName);
            }

            if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND p.branchCode = @branch ");
                p.Add("@branch", branch);
            }

            if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND p.departmentCode = @department ");
                p.Add("@department", department);
            }

            sql.Append(" ORDER BY p.accountNo; ");

            var records = _db.Query(sql.ToString(), p).ToList();

            if (records == null || records.Count == 0)
                return Json(new { success = false, message = "No Security Bank employees found for this payroll period." });

            // ── Build CPAY lines ─────────────────────────────────────────────
            string branchCode4 = FundingAccountNo.Substring(0, 4);         // "1234"
            string postingDate = DateTime.Now.ToString("MMddyy");          // "051226"

            double totalAmount = 0;
            var detailLines = new List<string>();

            foreach (var row in records)
            {
                double netPay = Convert.ToDouble(row.totalNetPay);
                string accountNo = Convert.ToString(row.accountNo).PadLeft(13, '0');
                string amountStr = FormatCpayAmount(netPay);

                // PHP + 10 + accountNo(13) + branchCode(4) + 00 + 700 + amount(13)
                string detailLine = $"PHP10{accountNo}{branchCode4}00700{amountStr}";
                detailLines.Add(detailLine);

                totalAmount += netPay;
            }

            string totalAmountStr = FormatCpayAmount(totalAmount);

            // PHP + 01 + fundingAccountNo(13) + postingDate(6) + 200 + totalAmount(13)
            string headerLine = $"PHP01{FundingAccountNo}{postingDate}200{totalAmountStr}";

            // ── Assemble file content ────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine(headerLine);
            foreach (var line in detailLines)
                sb.AppendLine(line);

            byte[] fileBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
            string fileName = $"CPAY_{dateMonth}_{(cutOffType == "1" ? "1ST" : "2ND")}CUTOFF_{dateYear}.txt";

            return File(fileBytes, "text/plain", fileName);
        }

        private string FormatCpayAmount(double amount)
        {
            long centavos = (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
            return centavos.ToString().PadLeft(13, '0');
        }

        [HttpPost]
        public JsonResult PostPayroll([FromBody] PayrollProcessModel model)
        {
            try
            {
                string updateSql = @"
                    UPDATE p_biometrics 
                    SET statusName = 'Posted',
                        dtLastModified = Now(), 
                        lastModifiedByUser = @employeeno
                    WHERE dateMonth = @datemonth
                    AND dateYear = @dateyear
                    AND cutOffType = @cutofftype
                    AND branchCode != 'CASUAL'
                    AND isActive = 1;

                    UPDATE p_biometricsline 
                    SET statusName = 'Posted', 
                        dtLastModified = Now(), 
                        lastModifiedByUser = @employeeno
                    WHERE dateMonth = @datemonth
                    AND dateYear = @dateyear
                    AND cutOffType = @cutofftype
                    AND branchCode != 'CASUAL'
                    AND isActive = 1;

                    UPDATE c_payable
                    SET statusName = 'Processed',
                        dtLastModified = NOW(),
                        lastModifiedByUser = @employeeno
                    WHERE statusName = 'Approved'
                    AND isActive = 1
                    AND dateToAdjustment BETWEEN @datefrom AND @dateto;

                    UPDATE e_loan 
                    SET statusName = 'Completed',
                        dtLastModified = NOW(),
                        lastModifiedByUser = @employeeno
                    WHERE statusName = 'For Completion'
                    AND isActive = 1
                ";

                _db.Execute(updateSql, new
                {
                    datemonth = model.dateMonth,
                    dateyear = model.dateYear,
                    cutofftype = model.cutOffType,
                    datefrom = model.dateFrom,
                    dateto = model.dateTo,
                    employeeno = EmployeeNo
                });

                _db.Execute(updateSql, new { datemonth = model.dateMonth, dateyear = model.dateYear, cutofftype = model.cutOffType });

                return Json(new { success = true, message = "Payroll Register Posted." });
            }

            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error in posting payroll: {ex.Message}" });
            }
        }
    }
}