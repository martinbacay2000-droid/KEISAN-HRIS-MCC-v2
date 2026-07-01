using Dapper;
using KEISAN_HRIS_v2.Models.Payroll;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text;
using System.Transactions;
using static Mysqlx.Expect.Open.Types.Condition.Types;


namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    [ModuleAuthorize("TpayrollProcessM")]
    public class PayrollProcessController : BaseController
    {
        private readonly IDbConnection _db;

        public PayrollProcessController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payroll/PayrollProcess.cshtml");

        }


        //private IDbConnection GetConnection()
        //{
        //    return _db;
        //}


        // CRUD Operations for Leave Request List
        [HttpGet]
        public JsonResult GetPayrollList(string branch, string department, string cutOffType, string dateYear, string dateMonth)
        {
            var sb = new StringBuilder(@"
                SELECT 
                    rq.employeeNo AS employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,
                    '' StatusName

                FROM p_biometricsline rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo

                WHERE rq.isActive = 1 AND rq.statusName = 'Open'
                

            ");

            var p = new DynamicParameters();

            sb.Append(" AND rq.dateYear = @dateYear ");
            p.Add("@dateYear", dateYear);

            sb.Append(" AND (rq.branchCode = @branch OR @branch='ALL')");
            p.Add("@branch", branch);

            if (!string.IsNullOrWhiteSpace(dateMonth))
            {
                sb.Append(" AND rq.dateMonth = @dateMonth ");
                p.Add("@dateMonth", dateMonth);
            }

            if (!string.IsNullOrWhiteSpace(cutOffType))
            {
                sb.Append(" AND rq.cutOffType = @cutOffType ");
                p.Add("@cutOffType", cutOffType);
            }

            if (!string.IsNullOrWhiteSpace(department) && !department.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" AND rq.departmentCode = @department ");
                p.Add("@department", department);
            }

            //sb.Append(" GROUP BY rq.employeeNo ORDER BY b.lastName ; ");
            sb.Append(" GROUP BY rq.employeeNo, b.lastName, b.firstName, b.middleName ORDER BY b.lastName ; ");

            var requests = _db.Query<PayrollProcessModel>(sb.ToString(), p).ToList();
            return Json(new { data = requests });
        }

        [HttpPost]
        public JsonResult ProcessPayroll([FromBody] PayrollProcessModel model)
        {
            var userCode = HttpContext.Session.GetString("employeeNo");

            if (model == null)
            {
                return Json(new { success = false, message = "Model is null" });
            }

            string getInfoSql = @"SELECT 
                b.departmentCode,
                b.branchCode,
                b.employmentStatus, 
                b.rankCode, 
                b.positionCode, 
                b.departmentCode, 
                epd.bankCode, 
                epd.accountNo,
                b.isGAInstructor as isConfidential,
                b.isFlightInstructor as isFlexi,
                b.isActive as empStatus, 

                IFNULL(SUM(presentCount), 0) as presentCount,
                IFNULL(SUM(paidHoliday), 0) as paidHoliday,
                IFNULL(SUM(absentCount), 0) AS absentCount, 

                IFNULL(SUM(CASE WHEN pbl.holidayType = 'Legal Holiday' THEN 1 ELSE 0 END), 0) AS legalPresentCount,  
                IFNULL(SUM(CASE WHEN pbl.holidayType = 'Special Holiday' THEN 1 ELSE 0 END), 0) AS specialPresentCount,

                IFNULL(SUM(CASE WHEN (pbl.attendanceStatus IN ('ON LEAVE') 
                OR ( pbl.attendanceStatus IN ('LEGAL HOLIDAY', 'SPECIAL HOLIDAY') 
                AND IFNULL(pbl.timeIn,'') = '')) AND b.rankCode <> 'EXECOM'  THEN presentCount ELSE 0 END),0) AS wfhLeave,

                IFNULL(SUM(pbl.render), 0) as render, 
                IFNULL(SUM(pbl.renderOT), 0) as renderOT, 
                IFNULL(SUM(pbl.renderNSD), 0) as renderNSD, 
                IFNULL(SUM(pbl.renderNSDOT), 0) as renderNSDOT, 

                IFNULL(SUM(pbl.renderREST), 0) as renderREST, 
                IFNULL(SUM(pbl.renderRESTOT), 0) as renderRESTOT, 
                IFNULL(SUM(pbl.renderNSDREST), 0) as renderNSDREST, 
                IFNULL(SUM(pbl.renderNSDRESTOT), 0) as renderNSDRESTOT, 

                IFNULL(SUM(pbl.renderL), 0) as renderL, 
                IFNULL(SUM(pbl.renderOTL), 0) as renderOTL, 
                IFNULL(SUM(pbl.renderNSDL), 0) as renderNSDL, 
                IFNULL(SUM(pbl.renderNSDOTL), 0) as renderNSDOTL, 

                IFNULL(SUM(pbl.renderRESTL), 0) as renderRESTL, 
                IFNULL(SUM(pbl.renderRESTOTL), 0) as renderRESTOTL, 
                IFNULL(SUM(pbl.renderNSDRESTL), 0) as renderNSDRESTL, 
                IFNULL(SUM(pbl.renderNSDRESTOTL), 0) as renderNSDRESTOTL, 

                IFNULL(SUM(pbl.renderS), 0) as renderS, 
                IFNULL(SUM(pbl.renderOTS), 0) as renderOTS, 
                IFNULL(SUM(pbl.renderNSDS), 0) as renderNSDS, 
                IFNULL(SUM(pbl.renderNSDOTS), 0) as renderNSDOTS, 

                IFNULL(SUM(pbl.renderRESTS), 0) as renderRESTS, 
                IFNULL(SUM(pbl.renderRESTOTS), 0) as renderRESTOTS, 
                IFNULL(SUM(pbl.renderNSDRESTS), 0) as renderNSDRESTS, 
                IFNULL(SUM(pbl.renderNSDRESTOTS), 0) as renderNSDRESTOTS, 

 
                IFNULL(SUM(pbl.renderOvertime), 0) as renderOvertime, 

                IFNULL(SUM(pbl.renderLate), 0) as renderLate, 
                IFNULL(SUM(pbl.renderUndertime), 0) as renderUndertime, 
                IFNULL(UCASE(epd.payrollBasis),'') as payrollBasis, 

                CAST(IFNULL(CAST(AES_DECRYPT(epd.basicMonthlyPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) as basicPay, 
                CAST(IFNULL(CAST(AES_DECRYPT(epd.basicMonthlyPay,'portalkeisan') AS CHAR(200)),0) /2 AS DECIMAL(10,2)) as basicPaySemi, 
                CAST(IFNULL(CAST(AES_DECRYPT(epd.dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) as dailyRate, 
                CAST(IFNULL(CAST(AES_DECRYPT(epd.dailyRate,'portalkeisan') AS CHAR(200)),0) /8 AS DECIMAL(10,2)) as hourlyRate, 
                CAST(IFNULL(CAST(AES_DECRYPT(epd.dailyRate,'portalkeisan') AS CHAR(200)),0) /8/60 AS DECIMAL(10,2)) as minuteRate, 

                IFNULL(epd.payrollType, 'PAYROLL UNASSIGNED') as payrollType, 
                IFNULL(epd.accountNo, 'PAYROLL UNASSIGNED') as accountNo,  
                IFNULL(epd.isNoLate, 0) as isNoLate, 
                IFNULL(epd.isNoOTPremium, 0) as isNoOTPremium, 

                IFNULL(epd.isMinimumWageEarner, 0) as isMinimumWageEarner, 
                IFNULL(epd.contriPIFadditional, 0) as contriPIFadditional, 

                IFNULL(ebs.tax, 1) as tax, 
                IFNULL(ebs.sss, 1) as sss, 
                IFNULL(ebs.philhealth, 1) as philhealth, 
                IFNULL(ebs.pagibig, 1) as pagibig, 
                IFNULL(ebs.pf, 1) as provident, 

                -- CASE WHEN b.dateHired BETWEEN @datefrom AND @dateto THEN 'dailyrated' ELSE 'monthlyrated' END  
                '' AS payType

                FROM p_biometricsline pbl
                JOIN e_basicinfo b ON pbl.employeeNo = b.employeeNo
                LEFT JOIN e_payrolldetails epd ON pbl.employeeNo = epd.employeeNo 
                LEFT JOIN e_benefitssetting ebs ON pbl.employeeNo = ebs.employeeNo 

                WHERE pbl.employeeNo = @employeeno 
                AND pbl.dateMonth = @datemonth
                AND pbl.dateYear = @dateyear
                AND pbl.cutOffType = @cutofftype
                AND pbl.statusName = 'Open'
                AND pbl.isActive = 1

                GROUP BY pbl.employeeNo
                ";


            var empInfo = _db.QueryFirstOrDefault(getInfoSql, new { employeeno = model.employeeNo, datemonth = model.dateMonth, dateyear = model.dateYear, cutofftype = model.cutOffType, datefrom = model.dateFrom, dateto = model.dateTo });

            if (empInfo == null)
            {
                return Json(new { success = false, message = "Employee payroll data not found." });
            }

            string getRateSql = @"SELECT * FROM s_salaryRates";
            var rateInfo = _db.QueryFirstOrDefault<SalaryRates>(getRateSql);
            if (rateInfo == null)
            {
                return Json(new { success = false, message = "Salary rates are not set." });
            }

            if (empInfo.isNoOTPremium == 1)
            {
                rateInfo.ApplyNoOTPremium();
            }

            //Double allowanceDailyRate = 0, allowanceHourlyRate=0;
            #region Get Allowances
            var getAllowanceSql = @"
                    SELECT 
	                    cp.allowanceCode,
	                    cp.allowanceAmount,
	                    CAST(cp.allowanceAmount/313*12 AS DECIMAL(10,2)) AS allowanceDailyRate,
	                    CAST(cp.allowanceAmount/313*12/8 AS DECIMAL(10,2)) AS allowanceHourlyRate,
	                    cp.effectivityDate,
	                    s.isTaxable,
	                    s.basis,
    
                        CASE WHEN s.basis = 'Monthly' AND cp.effectivityDate > @datefrom THEN
	                        DATEDIFF(cp.effectivityDate,DATE(@datefrom)) *
		                     CAST(cp.allowanceAmount/313*12 AS DECIMAL(10,2)) 
	                    ELSE 0 END AS toDeduct
	
                    FROM e_allowance cp
                    JOIN s_allowance s ON s.allowanceCode = cp.allowanceCode
                    WHERE cp.employeeNo = @employeeno
                    AND cp.isActive = 1
                    AND cp.effectivityDate <= DATE(@dateTo)
                ";

            var allowanceList = _db.Query<AllowanceList>(getAllowanceSql, new
            {
                employeeno = model.employeeNo,
                dateto = model.dateTo,
                datefrom = model.dateFrom,
            }).ToList();

            // Loop through each allowance
            foreach (var allowance in allowanceList)
            {
                double AllowanceAmount = 0;
                if (allowance.basis == "Daily")
                {
                    AllowanceAmount = Math.Round(Convert.ToDouble(allowance.allowanceAmount) * Convert.ToDouble((empInfo.presentCount - empInfo.wfhLeave)), 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    AllowanceAmount = Math.Round(Convert.ToDouble(allowance.allowanceAmount / 2) - Convert.ToDouble(allowance.toDeduct), 2, MidpointRounding.AwayFromZero);
                }

                if (allowance.isTaxableAllowance == 1)
                {
                    model.allowanceTaxable += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    model.allowanceNonTaxable += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
                }

                if (allowance.allowanceCode == "COMMUNICATION")
                {
                    model.communicationAllowance += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
                }
                else if (allowance.allowanceCode == "TRANSPORTATION" || allowance.allowanceCode == "TRANSPORTATION DAILY")
                {
                    model.travelAllowance += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
                }
                else if (allowance.allowanceCode == "SUBSIDIZED MEAL")
                {
                    model.riceAllowanceAmount += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
                }
                else if (allowance.allowanceCode == "Basic Allowance")
                {
                    model.allowanceDailyRate += Math.Round(Convert.ToDouble(allowance.allowanceDailyRate), 2, MidpointRounding.AwayFromZero);
                    model.allowanceHourlyRate += Math.Round(Convert.ToDouble(allowance.allowanceHourlyRate), 2, MidpointRounding.AwayFromZero);
                    model.reg_basic_al += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
                }
                else
                {
                    model.otherAllowance += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
                }

                model.totalAllowance += Math.Round(AllowanceAmount, 2, MidpointRounding.AwayFromZero);
            }
            #endregion
            model.dailyRate = Math.Round(Convert.ToDouble(empInfo.dailyRate), 2, MidpointRounding.AwayFromZero);

            model.amountLate = Math.Round(Convert.ToDouble(empInfo.renderLate) * Convert.ToDouble(empInfo.minuteRate), 2, MidpointRounding.AwayFromZero);
            model.tardy_al = Math.Round(Convert.ToDouble(empInfo.renderLate) * Convert.ToDouble(model.allowanceHourlyRate / 60), 2, MidpointRounding.AwayFromZero);

            model.amountUndertime = Math.Round(Convert.ToDouble(empInfo.renderUndertime) * Convert.ToDouble(empInfo.minuteRate), 2, MidpointRounding.AwayFromZero);
            model.undertime_al = Math.Round(Convert.ToDouble(empInfo.renderUndertime) * Convert.ToDouble(model.allowanceHourlyRate / 60), 2, MidpointRounding.AwayFromZero);

            model.absentAmount = Math.Round(Convert.ToDouble(empInfo.absentCount) * Convert.ToDouble(empInfo.dailyRate), 2, MidpointRounding.AwayFromZero);
            model.absent_al = Math.Round(Convert.ToDouble(empInfo.absentCount) * model.allowanceDailyRate, 2, MidpointRounding.AwayFromZero);

            model.totalDeductionLateUndertimeAbsent = Math.Round(Convert.ToDouble(model.amountLate + model.amountUndertime + model.absentAmount), 2, MidpointRounding.AwayFromZero);

            model.amount = Math.Round(Convert.ToDouble(empInfo.render) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RegularDuty / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountOT = Math.Round(Convert.ToDouble(empInfo.renderOT) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RegularOT / 100.0), 2, MidpointRounding.AwayFromZero);
            model.reg_ot_al = Math.Round(Convert.ToDouble(empInfo.renderOT) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.RegularOT / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountNSD = Math.Round(Convert.ToDouble(empInfo.renderNSD) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RegularND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.reg_nd_al = Math.Round(Convert.ToDouble(empInfo.renderNSD) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.RegularND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountNSDOT = Math.Round(Convert.ToDouble(empInfo.renderNSDOT) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RegularOTND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.reg_ndot_al = Math.Round(Convert.ToDouble(empInfo.renderNSDOT) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.RegularOTND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountS = Math.Round(Convert.ToDouble(empInfo.renderS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.SH / 100.0), 2, MidpointRounding.AwayFromZero);
            model.sh_basic_al = Math.Round(Convert.ToDouble(empInfo.renderS) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.SH / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountOTS = Math.Round(Convert.ToDouble(empInfo.renderOTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.SHOT / 100.0), 2, MidpointRounding.AwayFromZero);
            model.sh_ot_al = Math.Round(Convert.ToDouble(empInfo.renderOTS) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.SHOT / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountNSDS = Math.Round(Convert.ToDouble(empInfo.renderNSDS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.SHND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.sh_nd_al = Math.Round(Convert.ToDouble(empInfo.renderNSDS) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.SHND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountNSDOTS = Math.Round(Convert.ToDouble(empInfo.renderNSDOTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.SHOTND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.sh_ndot_al = Math.Round(Convert.ToDouble(empInfo.renderNSDOTS) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.SHOTND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountL = Math.Round(Convert.ToDouble(empInfo.renderL) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RH / 100.0), 2, MidpointRounding.AwayFromZero);
            model.lh_basic_al = Math.Round(Convert.ToDouble(empInfo.renderL) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.RH / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountOTL = Math.Round(Convert.ToDouble(empInfo.renderOTL) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RHOT / 100.0), 2, MidpointRounding.AwayFromZero);
            model.lh_ot_al = Math.Round(Convert.ToDouble(empInfo.renderOTL) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.RHOT / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountNSDL = Math.Round(Convert.ToDouble(empInfo.renderNSDL) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RHND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.lh_nd_al = Math.Round(Convert.ToDouble(empInfo.renderNSDL) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.RHND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountNSDOTL = Math.Round(Convert.ToDouble(empInfo.renderNSDOTL) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RHOTND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountREST = Math.Round(Convert.ToDouble(empInfo.renderREST) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RD / 100.0), 2, MidpointRounding.AwayFromZero);
            model.rd_basic_al = Math.Round(Convert.ToDouble(empInfo.renderREST) * model.allowanceHourlyRate * Convert.ToDouble(rateInfo.RD / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountRESTOT = Math.Round(Convert.ToDouble(empInfo.renderRESTOT) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDOT / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountNSDREST = Math.Round(Convert.ToDouble(empInfo.renderNSDREST) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountNSDRESTOT = Math.Round(Convert.ToDouble(empInfo.renderNSDRESTOT) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDOTND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountRESTS = Math.Round(Convert.ToDouble(empInfo.renderRESTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDSH / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountRESTOTS = Math.Round(Convert.ToDouble(empInfo.renderRESTOTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDSHOT / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountNSDRESTS = Math.Round(Convert.ToDouble(empInfo.renderNSDRESTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDSHND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountNSDRESTOTS = Math.Round(Convert.ToDouble(empInfo.renderNSDRESTOTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDSHOTND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.amountRESTL = Math.Round(Convert.ToDouble(empInfo.renderRESTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDRH / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountRESTOTL = Math.Round(Convert.ToDouble(empInfo.renderRESTOTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDRHOT / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountNSDRESTL = Math.Round(Convert.ToDouble(empInfo.renderNSDRESTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDRHND / 100.0), 2, MidpointRounding.AwayFromZero);
            model.amountNSDRESTOTL = Math.Round(Convert.ToDouble(empInfo.renderNSDRESTOTS) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble(rateInfo.RDRHOTND / 100.0), 2, MidpointRounding.AwayFromZero);

            model.totalAllowance =
                model.totalAllowance
                + model.reg_ot_al
                + model.reg_nd_al
                + model.reg_ndot_al

                + model.rd_basic_al

                + model.lh_basic_al
                + model.lh_nd_al
                + model.lh_ot_al

                + model.sh_basic_al
                + model.sh_nd_al
                + model.sh_ndot_al
                + model.sh_ot_al;

            //model.otherEmployeeReceivable = model.otherEmployeeReceivable
            //    + model.absent_al
            //    + model.undertime_al
            //    + model.tardy_al;

            model.totalDeduction = model.totalDeduction
                + model.absent_al
                + model.undertime_al
                + model.tardy_al;

            if (empInfo.payrollBasis == "DAILY" || empInfo.payType == "dailyrated") // daily
            {
                model.basicPaySemi = Math.Round(Convert.ToDouble(Convert.ToDouble(model.dailyRate) * Convert.ToDouble(empInfo.presentCount)), 2, MidpointRounding.AwayFromZero);
                model.absentCount = 0;
                model.absentAmount = 0;
            }

            else // monthly
            {
                model.basicPaySemi = Math.Round(Convert.ToDouble(empInfo.basicPaySemi), 2, MidpointRounding.AwayFromZero);
                model.amountREST = Math.Round(Convert.ToDouble(empInfo.renderREST) * Convert.ToDouble(empInfo.hourlyRate) * Convert.ToDouble((rateInfo.RDMonthly / 100.0)), 2, MidpointRounding.AwayFromZero);
            }

            if (empInfo.isNoLate == 1)
            {
                model.amountLate = 0;
            }

            model.nonBasicPay = Math.Round(Convert.ToDouble(
                  model.amountOT
                + model.amountNSD
                + model.amountNSDOT
                + model.amountS
                + model.amountOTS
                + model.amountNSDS
                + model.amountNSDOTS
                + model.amountL
                + model.amountOTL
                + model.amountNSDL
                + model.amountNSDOTL
                + model.amountREST
                + model.amountRESTOT
                + model.amountNSDREST
                + model.amountNSDRESTOT
                + model.amountRESTS
                + model.amountRESTOTS
                + model.amountNSDRESTS
                + model.amountNSDRESTOTS
                + model.amountRESTL
                + model.amountRESTOTL
                + model.amountNSDRESTL
                + model.amountNSDRESTOTL), 2, MidpointRounding.AwayFromZero);

            #region Get Payroll Adjustments
            var getAdjustmentSql = @"
                    SELECT 
	                    cp.id AS payableID,
	                    s.isTaxable AS isTaxableAdj,
	                    cp.approvedAmount AS adjustmentAmount
	
                    FROM c_payable cp
                    JOIN s_adjustment s ON s.adjustmentCode = cp.adjustmentCode

                    WHERE cp.employeeNo = @employeeno
                    AND cp.dateToAdjustment <= DATE(@dateTo)
                    AND cp.statusName ='Approved'
                    AND cp.isActive = 1
                ";

            var adjustmentList = _db.Query<AdjustmentList>(getAdjustmentSql, new
            {
                employeeno = model.employeeNo,
                datemonth = model.dateMonth,
                dateyear = model.dateYear,
                cutofftype = model.cutOffType,
                dateto = model.dateTo
            }).ToList();

            // Loop through each adjustment
            foreach (var adj in adjustmentList)
            {
                //Other Deduction 
                if (adj.adjustmentAmount < 0)
                {
                    model.otherEmployeeReceivable += Math.Round(Convert.ToDouble(adj.adjustmentAmount), 2, MidpointRounding.AwayFromZero) * -1;
                }

                //Additional Adjustment
                else
                {
                    if (adj.isTaxableAdj == 1)
                    {
                        model.otherIncome += Math.Round(Convert.ToDouble(adj.adjustmentAmount), 2, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        model.otherEmployeePayable += Math.Round(Convert.ToDouble(adj.adjustmentAmount), 2, MidpointRounding.AwayFromZero);
                    }
                }

                // Update the adjustment record status to 'Closed'
                string updateSql = @"
                    UPDATE c_payable
                    SET dtLastModified = Now(), lastModifiedByUser = @lastModifiedByUser,
                    dateMonth = @datemonth,
                    dateYear  = @dateyear,
                    dateFrom = @datefrom,
                    dateTo = @dateto,
                    cutoffType=@cutoffType
                    WHERE id = @payableID
                ";

                _db.Execute(updateSql, new
                {
                    payableID = adj.payableID,
                    datemonth = model.dateMonth,
                    dateyear = model.dateYear,
                    cutofftype = model.cutOffType,
                    datefrom = model.dateFrom,
                    dateto = model.dateTo,
                    lastModifiedByUser = EmployeeNo
                });
            }

            #endregion

            model.totalDeduction += model.otherEmployeeReceivable;
            //#region Get Payroll Deduction
            //var getDeductionSql = @"
            //    UPDATE c_receivable SET statusname = 'Approved'
            //    WHERE isActive = 1 AND StatusName = 'Processed'
            //    AND employeeNo = @employeeno
            //    AND dateMonth = @datemonth
            //    AND dateYear = @dateyear
            //    AND cutOffType = @cutofftype;

            //    SELECT 
            //        cp.id AS receivableID,
            //        s.isTaxable AS isTaxableDed,
            //        cp.approvedAmount AS deductionAmount

            //    FROM c_receivable cp
            //    JOIN s_otherDeduction s ON s.otherDeductionCode = cp.otherDeductionCode

            //    WHERE cp.employeeNo = @employeeno
            //    AND cp.deductionDate <= DATE(@dateTo)
            //    AND cp.statusName IN ('Approved','Processed')
            //    AND cp.isActive = 1
            //";

            //var deductionList = _db.Query<DeductionList>(getDeductionSql, new
            //{
            //    employeeno = model.employeeNo,
            //    datemonth = model.dateMonth,
            //    dateyear = model.dateYear,
            //    cutofftype = model.cutOffType,
            //    dateto = model.dateTo
            //}).ToList();

            //// Loop through each deduction
            //foreach (var ded in deductionList)
            //{

            //    if (ded.isTaxableDed == 1)
            //    {
            //        model.otherIncome -= Math.Round(Convert.ToDouble(ded.deductionAmount), 2, MidpointRounding.AwayFromZero);
            //    }
            //    else
            //    {
            //        model.otherEmployeeReceivable += Math.Round(Convert.ToDouble(ded.deductionAmount), 2, MidpointRounding.AwayFromZero);
            //    }


            //    // Update the deduction record status to 'Closed'
            //    string updateSql = @"
            //        UPDATE c_receivable
            //        SET statusName = 'Processed', dtLastModified = Now(), lastModifiedByUser = 'SYSTEM'
            //        WHERE id = @receivableID
            //    ";

            //    _db.Execute(updateSql, new { receivableID = ded.receivableID });
            //}
            //#endregion

            #region Government Contribution

            double sssBasis = 0, phiBasis = 0, pifBasis = 0;
            double lastSSSEE = 0, lastSSSER = 0, lastWISPEE = 0, lastWISPER = 0, lastSSSECER = 0, lastPHIEE = 0, lastPHIER = 0, lastPIFEE = 0, lastPIFER = 0, lastTAX = 0, lastTaxableIncome = 0;

            sssBasis = Math.Round(Convert.ToDouble(
                model.basicPaySemi
                + model.nonBasicPay
                - model.amountLate
                - model.amountUndertime
                - model.absentAmount
                ), 2, MidpointRounding.AwayFromZero);

            phiBasis = Math.Round(Convert.ToDouble(
                empInfo.basicPay
                //model.basicPaySemi
                //- model.amountLate
                //- model.amountUndertime
                //- model.absentAmount
                ), 2, MidpointRounding.AwayFromZero);

            pifBasis = Math.Round(Convert.ToDouble(
                model.basicPaySemi
                - model.amountLate
                - model.amountUndertime
                - model.absentAmount
                ), 2, MidpointRounding.AwayFromZero);

            if (model.cutOffType == "2")
            {
                var getLastGovDeductions = @"
                        SELECT 
	                        IFNULL(p.deductionSSSemployee,0) AS lastSSSEE,
	                        IFNULL(p.deductionSSSemployer,0) AS lastSSSER,

	                        IFNULL(p.deductionWISPemployee,0) AS lastWISPEE,
	                        IFNULL(p.deductionWISPemployer,0) AS lastWISPER,

	                        IFNULL(p.deductionSSSec,0) AS lastSSSECER,
	                        IFNULL(p.deductionPHIemployee,0) AS lastPHIEE,
	                        IFNULL(p.deductionPHIemployer,0) AS lastPHIER,
	                        IFNULL(p.deductionPIFemployee,0) AS lastPIFEE,
	                        IFNULL(p.deductionPIFemployer,0) AS lastPIFER,
	                        IFNULL(p.withHeldTax,0) AS lastTAX,
	
	                        IFNULL(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200)),0) AS lastBasicPaySemi,
	                        IFNULL(p.totalDeductionLateUndertimeAbsent,0) AS lastTardiness,
	                        IFNULL(p.nonBasicPay,0) AS lastNonBasicPay,
	                        IFNULL(p.taxableIncome,0) AS lastTaxableIncome

                        FROM p_biometrics p
                        WHERE p.employeeNo = @employeeno
                        AND p.dateMonth = @datemonth
                        AND p.dateYear = @dateyear
                        AND p.cutOffType = 1
                        AND p.statusName = 'Posted'
                        AND p.isActive = 1
                    ";
                var lastGovDeductions = _db.QueryFirstOrDefault(getLastGovDeductions, new { employeeno = model.employeeNo, datemonth = model.dateMonth, dateyear = model.dateYear, cutofftype = model.cutOffType });
                Double lastBasicPaySemi = 0, lastNonBasicPay = 0, lastTardiness = 0;

                if (lastGovDeductions != null)
                {

                    lastBasicPaySemi = Convert.ToDouble(lastGovDeductions.lastBasicPaySemi);
                    lastNonBasicPay = Convert.ToDouble(lastGovDeductions.lastNonBasicPay);
                    lastTardiness = Convert.ToDouble(lastGovDeductions.lastTardiness);

                    sssBasis += Math.Round(Convert.ToDouble(
                        lastBasicPaySemi
                        + lastNonBasicPay
                        - lastTardiness
                    ), 2, MidpointRounding.AwayFromZero);

                    //phiBasis += Math.Round(Convert.ToDouble(
                    //    lastBasicPaySemi
                    //    - lastTardiness
                    //), 2, MidpointRounding.AwayFromZero);

                    pifBasis += Math.Round(Convert.ToDouble(
                        lastBasicPaySemi
                        - lastTardiness
                    ), 2, MidpointRounding.AwayFromZero);

                    lastSSSEE = Convert.ToDouble(lastGovDeductions.lastSSSEE);
                    lastSSSER = Convert.ToDouble(lastGovDeductions.lastSSSER);

                    lastWISPEE = Convert.ToDouble(lastGovDeductions.lastWISPEE);
                    lastWISPER = Convert.ToDouble(lastGovDeductions.lastWISPER);

                    lastSSSECER = Convert.ToDouble(lastGovDeductions.lastSSSECER);
                    lastPHIEE = Convert.ToDouble(lastGovDeductions.lastPHIEE);
                    lastPHIER = Convert.ToDouble(lastGovDeductions.lastPHIER);
                    lastPIFEE = Convert.ToDouble(lastGovDeductions.lastPIFEE);
                    lastPIFER = Convert.ToDouble(lastGovDeductions.lastPIFER);
                    lastTAX = Convert.ToDouble(lastGovDeductions.lastTAX);
                    lastTaxableIncome = Convert.ToDouble(lastGovDeductions.lastTaxableIncome);
                }
            }

            double pifEE = 0, pifER = 0, phiEE = 0, phiER = 0;

            if (empInfo.sss == 1)
            {
                var getSSS = @"
                    SELECT 
	                    p.socialSecurityER,
	                    p.socialSecurityEE,
	                    p.socialSecurityTotal,
	                    p.ecER,
	                    p.totalWispER,
	                    p.totalWispEE,
	                    p.totalWisp
	
                    FROM s_sss p
                    WHERE p.isActive = 1
                    AND @sssbasis BETWEEN p.compensationRange
                    AND CASE WHEN p.compensationRange2 = 0 THEN 9999999 ELSE p.compensationRange2 END
                    AND p.effectivityDate <= DATE(@dateto)
                    ORDER BY p.effectivityDate DESC LIMIT 1
                ";
                var sssContri = _db.QueryFirstOrDefault(getSSS, new { dateto = model.dateTo, sssbasis = sssBasis });

                if (sssContri != null)
                {
                    model.deductionSSSemployee = Math.Round(Convert.ToDouble(sssContri.socialSecurityEE), 2, MidpointRounding.AwayFromZero);
                    model.deductionSSSemployer = Math.Round(Convert.ToDouble(sssContri.socialSecurityER), 2, MidpointRounding.AwayFromZero);

                    model.deductionWISPemployee = Math.Round(Convert.ToDouble(sssContri.totalWispEE), 2, MidpointRounding.AwayFromZero);
                    model.deductionWISPemployer = Math.Round(Convert.ToDouble(sssContri.totalWispER), 2, MidpointRounding.AwayFromZero);

                    model.deductionSSSec = Math.Round(Convert.ToDouble(sssContri.ecER), 2, MidpointRounding.AwayFromZero);
                }

            }

            if (empInfo.philhealth == 1)
            {
                var getPHI = @"
                    SELECT 
	                    p.personalShare,
	                    p.employerShare,
	                    p.percentMode
	
                    FROM s_philhealth p
                    WHERE p.isActive = 1
                    AND @phibasis BETWEEN p.basicSalaryMin 
                    AND CASE WHEN p.basicSalaryMax = 0 THEN 9999999 ELSE p.basicSalaryMax END
                    AND p.effectivityDate <= DATE(@dateto)
                    ORDER BY p.effectivityDate DESC LIMIT 1
                ";
                var phiContri = _db.QueryFirstOrDefault(getPHI, new { dateto = model.dateTo, phibasis = phiBasis });

                if (phiContri != null)
                {
                    if (phiContri.percentMode == 1)
                    {
                        phiEE = Convert.ToDouble((phiBasis * phiContri.personalShare / 100.0) / 2);
                        phiER = Convert.ToDouble((phiBasis * phiContri.employerShare / 100.0) / 2);
                    }
                    else
                    {
                        phiEE = Convert.ToDouble(phiContri.personalShare);
                        phiER = Convert.ToDouble(phiContri.employerShare);
                    }
                }

            }

            if (empInfo.pagibig == 1)
            {
                var getPIF = @"
                    SELECT 
	                    p.employeeShare,
	                    p.employerShare
	
                    FROM s_pagibig p
                    WHERE p.isActive = 1
                    AND @pifbasis BETWEEN p.monthlyCompensationMin 
                    AND CASE WHEN p.monthlyCompensationMax = 0 THEN 9999999 ELSE p.monthlyCompensationMax END 
                    AND p.effectivityDate <= DATE(@dateto)
                    ORDER BY p.effectivityDate DESC LIMIT 1
                ";
                var PIFContri = _db.QueryFirstOrDefault(getPIF, new { dateto = model.dateTo, pifbasis = pifBasis });

                if (PIFContri != null)
                {
                    pifEE = Math.Round(Convert.ToDouble((PIFContri.employeeShare)), 2, MidpointRounding.AwayFromZero);
                    pifER = Math.Round(Convert.ToDouble((PIFContri.employerShare)), 2, MidpointRounding.AwayFromZero);
                }

            }


            if (model.cutOffType == "1")
            {
                model.deductionPIFemployee = Math.Round(pifEE, 2, MidpointRounding.AwayFromZero);
                model.deductionPIFemployer = Math.Round(pifER, 2, MidpointRounding.AwayFromZero);
                model.deductionPHIemployee = Math.Round(phiEE, 2, MidpointRounding.AwayFromZero);
                model.deductionPHIemployer = Math.Round(phiER, 2, MidpointRounding.AwayFromZero);
            }
            else // 2nd cutoff
            {

                model.deductionSSSemployee = Math.Round(Convert.ToDouble(model.deductionSSSemployee) - lastSSSEE, 2, MidpointRounding.AwayFromZero);
                model.deductionSSSemployer = Math.Round(Convert.ToDouble(model.deductionSSSemployer) - lastSSSER, 2, MidpointRounding.AwayFromZero);

                model.deductionWISPemployee = Math.Round(Convert.ToDouble(model.deductionWISPemployee) - lastWISPEE, 2, MidpointRounding.AwayFromZero);
                model.deductionWISPemployer = Math.Round(Convert.ToDouble(model.deductionWISPemployer) - lastWISPER, 2, MidpointRounding.AwayFromZero);

                model.deductionSSSec = Math.Round(Convert.ToDouble(model.deductionSSSec) - lastSSSECER, 2, MidpointRounding.AwayFromZero);

                model.deductionPIFemployee = Math.Round(pifEE - lastPIFEE, 2, MidpointRounding.AwayFromZero);
                model.deductionPIFemployer = Math.Round(pifER - lastPIFER, 2, MidpointRounding.AwayFromZero);

                model.deductionPHIemployee = Math.Round(phiEE - lastPHIEE, 2, MidpointRounding.AwayFromZero);
                model.deductionPHIemployer = Math.Round(phiER - lastPHIER, 2, MidpointRounding.AwayFromZero);
            }

            model.totalMandatory = Math.Round(Convert.ToDouble(
                model.deductionSSSemployee
                + model.deductionWISPemployee
                + model.deductionPHIemployee
                + model.deductionPIFemployee), 2, MidpointRounding.AwayFromZero);

            model.taxableIncome = Math.Round(Convert.ToDouble(
                model.basicPaySemi
                + model.nonBasicPay
                + model.otherIncome
                - model.amountLate
                - model.amountUndertime
                - model.absentAmount
                - model.totalMandatory
                + lastTaxableIncome), 2, MidpointRounding.AwayFromZero);


            double taxBasis = Math.Round(Convert.ToDouble(model.taxableIncome), 2, MidpointRounding.AwayFromZero); // Math.Round(Convert.ToDouble(model.taxableIncome) + lastTaxableIncome,2,MidpointRounding.AwayFromZero);

            string taxType = "";

            if (model.cutOffType == "1")
            {
                taxType = "Semi-Monthly";
            }
            else
            {
                taxType = "Monthly";
            }

            if (empInfo.tax == 1 && empInfo.isMinimumWageEarner == 0)
            {
                var getTAX = @"
                    SELECT 
	                    p.taxCompensationRangeMin as min,
	                    p.taxPercent as percent,
                        p.taxPWT as pwt
	
                    FROM s_taxtable p
                    WHERE p.isActive = 1
                    AND @taxbasis BETWEEN p.taxCompensationRangeMin AND CASE WHEN p.taxCompensationRangeMax = 0 THEN 9999999 ELSE p.taxCompensationRangeMax END 
                    AND p.effectivityDate <= DATE(@dateto)
                    AND p.taxtype = @taxtype
                    ORDER BY p.effectivityDate DESC LIMIT 1
                ";
                var tax = _db.QueryFirstOrDefault(getTAX, new { dateto = model.dateTo, taxbasis = taxBasis, taxtype = taxType });

                if (tax != null)
                {
                    double min = Convert.ToDouble(tax.min);
                    double percent = Convert.ToDouble(tax.percent / 100.0);
                    double pwt = Convert.ToDouble(tax.pwt);

                    double taxAmount = ((taxBasis - min) * percent) + pwt;
                    if (model.cutOffType == "2")
                    {
                        taxAmount = taxAmount - lastTAX;
                    }

                    model.withHeldTax = Math.Round(taxAmount, 2, MidpointRounding.AwayFromZero);

                }

            }
            #endregion region


            // FOR MARCH 1ST CUTOFF 2026 ONLY AS PER MS. AILEEN //
            if (model.dateMonth == "March" && model.cutOffType == "1" && model.dateYear == "2026")
            {
                var getContriTemp = @"
                    SELECT 
	                    p.sss_ee,
                        p.hdmf_ee,
                        p.phic_ee,
                        p.wtax
                    FROM tbl_temp_contri p
                    WHERE p.employeeNo=@employeeno
                ";
                var contriTemp = _db.QueryFirstOrDefault(getContriTemp, new { employeeno = model.employeeNo });

                if (contriTemp != null)
                {
                    model.deductionSSSemployee = contriTemp.sss_ee;
                    model.deductionWISPemployee = 0;
                    model.deductionPIFemployee = contriTemp.hdmf_ee;
                    model.deductionPHIemployee = contriTemp.phic_ee;
                    model.withHeldTax = contriTemp.wtax;

                    model.totalMandatory =
                        model.deductionSSSemployee
                        + model.deductionPIFemployee
                        + model.deductionPHIemployee;
                }
            }
            //-- END -- //

            #region Get Loans
            var getLoanSql = @"
                    SELECT * FROM 
                    (
                    SELECT 							
	                    tbl1.totalLoanAmount - tbl1.loanPayments AS loanBalance,
	
	                    CASE WHEN (tbl1.totalLoanAmount - tbl1.loanPayments) <= 0 OR tbl1.statusName='Completed'
		                    THEN 'Completed' ELSE 'Ongoing' 
	                    END AS loanStatus,
	
	                    tbl1.*

                    FROM
                    (	
		                  SELECT r.employeeNo, 
		                  r.id AS loanID,
                          IFNULL(r.isActive,0) AS loanIsActive,
                          r.loanCode,

                          CASE WHEN r.deductionSchedule = '1st CutOff' THEN 1 
                          WHEN r.deductionSchedule = '2nd CutOff' THEN 2 
                          ELSE r.deductionSchedule END AS deductionSchedule,

                          r.statusName,
                          CAST(IFNULL(r.principalAmount,0) AS DECIMAL(10,2)) AS principalAmount,
                          CAST(IFNULL(r.interestAmount,0) AS DECIMAL(10,2)) AS interestAmount,
                          CAST(IFNULL(r.totalLoanAmount,0) AS DECIMAL(10,2)) AS totalLoanAmount,
                          CAST(IFNULL(r.amortizationAmount,0) AS DECIMAL(10,2)) AS amortizationAmount,     
				     
                          CAST(IFNULL((SELECT SUM(credit) FROM m_loan m WHERE m.e_loanID = r.id AND m.isActive = 1 AND m.dateTo <> @dateTo),0) AS DECIMAL(10,2)) AS loanPayments,
                          DATE_FORMAT(r.dateGranted,'%Y-%m-%d') AS dateGranted,
                          DATE_FORMAT(r.deductionStartDate,'%Y-%m-%d') AS deductionStartDate 

                          FROM e_loan r
                          LEFT JOIN s_user s ON s.userCode = r.addedByUser
                          LEFT JOIN s_user ss ON ss.userCode = r.statusByUser
                          LEFT JOIN s_loan sa on sa.loanCode = r.loanCode AND sa.isActive = 1

                          WHERE r.employeeNo = @employeeno
                     ) tbl1
                     ) tbl2 
                     WHERE loanStatus IN ('Ongoing','For Completion') AND loanIsActive = 1
                     AND deductionStartDate <= DATE(@dateTo)
                ";

            var loanList = _db.Query<LoanList>(getLoanSql, new
            {
                employeeno = model.employeeNo,
                dateto = model.dateTo
            }).ToList();

            // Loop through each loan
            foreach (var loans in loanList)
            {
                double LoanAmount = 0;
                if (loans.deductionSchedule == "Per Cutoff")
                {
                    LoanAmount = Convert.ToDouble(loans.amortizationAmount / 2);
                }
                else if (loans.deductionSchedule == model.cutOffType)
                {
                    LoanAmount = Convert.ToDouble(loans.amortizationAmount);
                }

                if (LoanAmount > 0)
                {
                    if (loans.loanBalance <= LoanAmount)
                    {
                        LoanAmount = Convert.ToDouble(loans.loanBalance);
                    }

                    if (loans.loanCode == "HDMF SALARY LOAN")
                    {
                        model.hdmfLoan += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "HDMF CALAMITY LOAN")
                    {
                        model.hdmfCalamity += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "SSS SALARY LOAN")
                    {
                        model.sssLoan += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "SSS CALAMITY LOAN")
                    {
                        model.sssCalamity += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "CASH ADVANCE")
                    {
                        model.cashadvance += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "HMO Dependent")
                    {
                        model.hmoLoan += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "EMPLOYEE LEDGER")
                    {
                        model.employeeLedger += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "China Bank Savings Loan")
                    {
                        model.csbLoan += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "Other Loan1")
                    {
                        model.otherLoan1 += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "Other Loan2")
                    {
                        model.otherLoan2 += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else if (loans.loanCode == "Other Loan3")
                    {
                        model.otherLoan3 += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                    else // Other Loan4
                    {
                        model.otherLoan4 += Math.Round(LoanAmount, 2, MidpointRounding.AwayFromZero);
                    }
                }

                model.amountLoan = Math.Round(Convert.ToDouble(model.amountLoan) + LoanAmount, 2, MidpointRounding.AwayFromZero); //total loan amount

                double newLoanBalance = Math.Round(Convert.ToDouble(loans.loanBalance) - LoanAmount, 2, MidpointRounding.AwayFromZero);
                // Insert loan payment details
                string sql = @"
                    DELETE FROM m_loan 
                    WHERE employeeNo = @employeeNo
                    AND e_loanID = @e_loanID
                    AND dateMonth = @dateMonth
                    AND dateYear = @dateYear
                    AND cutofftype = @cutOffType;

                    INSERT INTO m_loan(
                    employeeNo,
                    loanCode,
                    loanAmountDeducted,
                    loanDateDeducted,
                    cutOff,
                    statusName,
                    isActive,
                    dtAdded,
                    addedByUser,
                    loanAmortizationAmount,
                    loanDate,
                    loanDateStart,
                    methodType,
                    cutOffType,
                    dateMonth,
                    dateFrom,
                    dateTo,
                    dateYear,
                    loanAmount,
                    e_loanID,
                    credit,
                    loanBalance,
                    details
                )
                VALUES (
                    @employeeNo,
                    @loanCode,
                    @loanAmountDeducted,
                    @loanDateDeducted,
                    @cutOff,
                    @statusName,
                    @isActive,
                    @dtAdded,
                    @addedByUser,
                    @loanAmortizationAmount,
                    @loanDate,
                    @loanDateStart,
                    @methodType,
                    @cutOffType,
                    @dateMonth,
                    @dateFrom,
                    @dateTo,
                    @dateYear,
                    @loanAmount,
                    @e_loanID,
                    @credit,
                    @loanBalance,
                    @details
                )

                ";

                _db.Execute(sql, new
                {
                    employeeNo = model.employeeNo,
                    loanCode = loans.loanCode,
                    loanAmountDeducted = LoanAmount,
                    loanDateDeducted = DateTime.Now,
                    cutOff = model.cutOffType,
                    statusName = "Added",
                    isActive = 1,
                    dtAdded = DateTime.Now,
                    addedByUser = userCode,
                    loanAmortizationAmount = loans.amortizationAmount,
                    loanDate = loans.dateGranted,
                    loanDateStart = loans.deductionStartDate,
                    methodType = 1,
                    cutOffType = model.cutOffType,
                    dateMonth = model.dateMonth,
                    dateFrom = model.dateFrom,
                    dateTo = model.dateTo,
                    dateYear = model.dateYear,
                    loanAmount = loans.principalAmount,
                    e_loanID = loans.loanID,
                    credit = LoanAmount,
                    loanBalance = newLoanBalance,
                    details = "PAYROLL " + model.dateFrom + " to " + model.dateTo
                });

                // Auto-complete loan if balance has reached zero
                if (newLoanBalance <= 0)
                {
                    string completeLoanSql = @"
                        UPDATE e_loan 
                        SET statusName = 'For Completion',
                            dtStatus = NOW(),
                            statusByUser = @statusByUser,
                            dtLastModified = NOW(),
                            lastModifiedByUser = @lastModifiedByUser
                        WHERE id = @loanID";

                    _db.Execute(completeLoanSql, new
                    {
                        loanID = loans.loanID,
                        statusByUser = userCode,
                        lastModifiedByUser = userCode
                    });
                }
            }
            #endregion

            model.totalDeduction = Math.Round(Convert.ToDouble(
                model.totalDeduction
                + model.amountLate
                + model.amountUndertime
                + model.absentAmount
                + model.totalMandatory
                + model.withHeldTax
            //    + model.otherEmployeeReceivable
                + model.amountLoan), 2, MidpointRounding.AwayFromZero);

            model.totalGrossPay = Math.Round(Convert.ToDouble(
                model.basicPaySemi
                + model.nonBasicPay
                - model.amountLate
                - model.amountUndertime
                - model.absentAmount), 2, MidpointRounding.AwayFromZero);

            model.grossIncome = Math.Round(Convert.ToDouble(
                model.basicPaySemi
                + model.nonBasicPay
                + model.totalAllowance
                + model.otherIncome
                + model.otherEmployeePayable), 2, MidpointRounding.AwayFromZero);

            model.totalNetPay = Math.Round(Convert.ToDouble(
                model.grossIncome
                - model.totalDeduction), 2, MidpointRounding.AwayFromZero);



            //#region Fixed Deductions
            //var getFixedDeduction = @"
            //        SELECT 
            //            p.fixedDeductionCode,
            //         p.fixedDeductionAmount,
            //         p.deductionSchedule

            //        FROM e_fixeddeduction p
            //        WHERE p.isActive = 1
            //        AND p.employeeNo = @employeeno
            //        AND p.fixedDeductionDateStart <= DATE(NOW())
            //        AND p.deductionSchedule <> 'InActive'
            //        AND CASE WHEN @cutofftype=1 THEN p.deductionSchedule IN ('1st Cutoff','Per Cutoff')
            //                 ELSE p.deductionSchedule IN ('2nd Cutoff','Per Cutoff') END

            //    ";
            //var fixedDeductionList = _db.Query<FixedDeductionList>(getFixedDeduction, new { employeeno = model.employeeNo, cutofftype = model.cutOffType }).ToList();

            //// Loop through each fixedDeduction
            //foreach (var deduction in fixedDeductionList)
            //{
            //   if (deduction.fixedDeductionCode == "healthCard")
            //   {
            //        model.healthcard = Math.Round(Convert.ToDouble(model.healthcard) + Convert.ToDouble(deduction.fixedDeductionAmount), 2, MidpointRounding.AwayFromZero);
            //   }
            //   else if (deduction.fixedDeductionCode == "parking")
            //   {
            //        model.parking = Math.Round(Convert.ToDouble(model.parking) + Convert.ToDouble(deduction.fixedDeductionAmount), 2, MidpointRounding.AwayFromZero);
            //   }
            //   else if (deduction.fixedDeductionCode == "meals")
            //   {
            //        model.meals = Math.Round(Convert.ToDouble(model.meals) + Convert.ToDouble(deduction.fixedDeductionAmount), 2, MidpointRounding.AwayFromZero);
            //   }
            //   else
            //   {
            //        model.fixedOthers = Math.Round(Convert.ToDouble(model.fixedOthers) + Convert.ToDouble(deduction.fixedDeductionAmount), 2, MidpointRounding.AwayFromZero);
            //   }

            //    model.totalFixedDeduction = Math.Round(Convert.ToDouble(model.totalFixedDeduction) + Convert.ToDouble(deduction.fixedDeductionAmount), 2, MidpointRounding.AwayFromZero);
            //}
            //#endregion

            // MBOS AMOUNT
            model.totalMBOS = Math.Round(Convert.ToDouble(model.totalNetPay - model.totalFixedDeduction - model.otherEmployeeReceivable), 2, MidpointRounding.AwayFromZero);

            if (model.employeeNo == "NL-01-0001")
            {
                model.additionalMbos = Math.Round(Convert.ToDouble(
                80000
                - model.totalNetPay), 2, MidpointRounding.AwayFromZero);

            }
            else if (model.employeeNo == "NL-04-0009")
            {
                model.additionalMbos = Math.Round(Convert.ToDouble(
                50000
                - model.totalNetPay), 2, MidpointRounding.AwayFromZero);
            }




            try
            {

                string sql = @"
                    DELETE FROM p_biometrics 
                    WHERE employeeNo = @employeeNo
                    AND dateMonth=@dateMonth
                    AND dateYear = @dateYear
                    AND cutOffType = @cutofftype;

                    INSERT INTO p_biometrics (
                        methodType,
                        cutOffType,
                        dateMonth,
                        dateYear,
                        dateFrom,
                        dateTo,
                        branchCode,
                        departmentCode,
                        employmentStatus,
                        positionCode,
                        rankCode,
                        activeStatus,
                        employeeNo,
                        dailyRate,
                        basicPay,
                        basicPaySemi,
                        longPay,
                        render,
                        renderOT,
                        renderREST,
                        renderRESTOT,
                        renderNSD,
                        renderNSDOT,
                        renderNSDREST,
                        renderNSDRESTOT,
                        amount,
                        amountOT,
                        amountREST,
                        amountRESTOT,
                        amountNSD,
                        amountNSDOT,
                        amountNSDREST,
                        amountNSDRESTOT,
                        renderL,
                        renderOTL,
                        renderRESTL,
                        renderRESTOTL,
                        renderNSDL,
                        renderNSDOTL,
                        renderNSDRESTL,
                        renderNSDRESTOTL,
                        amountL,
                        amountOTL,
                        amountRESTL,
                        amountRESTOTL,
                        amountNSDL,
                        amountNSDOTL,
                        amountNSDRESTL,
                        amountNSDRESTOTL,
                        renderS,
                        renderOTS,
                        renderRESTS,
                        renderRESTOTS,
                        renderNSDS,
                        renderNSDOTS,
                        renderNSDRESTS,
                        renderNSDRESTOTS,
                        amountS,
                        amountOTS,
                        amountRESTS,
                        amountRESTOTS,
                        amountNSDS,
                        amountNSDOTS,
                        amountNSDRESTS,
                        amountNSDRESTOTS,
                        nonBasicPay,
                        allowanceDeductionAbsent,
                        absentCount,
                        presentCount,
                        lessDayCount,
                        workOnOffPresentCount,
                        legalPresentCount,
                        specialPresentCount,
                        absentAmount,
                        presentAmount,
                        lessDayAmount,
                        workOnOffPresentAmount,
                        legalPresentAmount,
                        specialPresentAmount,
                        totalRenderEarly,
                        totalRenderLate,
                        totalRenderUndertime,
                        totalRenderOvertime,
                        totalAmountEarly,
                        totalAmountLate,
                        totalAmountUndertime,
                        totalAmountOvertime,
                        totalDeductionLateUndertimeAbsent,
                        totalGrossPay,
                        totalGrossPayMandatory,
                        grossIncome,
                        rataAmount,
                        allowanceDeductionLate,
                        riceAllowanceAmount,
                        communicationAllowance,
                        travelAllowance,
                        otherAllowance,
                        totalAllowance,
                        allowanceNonTaxable,
                        allowanceTaxable,
                        deductionSSSemployee,
                        deductionSSSemployer,
                        deductionWISPemployee,
                        deductionWISPemployer,
                        deductionSSSec,
                        deductionPHIemployee,
                        deductionPHIemployer,
                        deductionPIFemployee,
                        deductionPIFemployer,
                        deductionPFemployee,
                        deductionPFemployer,
                        totalMandatory,
                        otherIncome,
                        taxableIncome,
                        withHeldTax,
                        amountLoan,
                        sssLoan,
                        hdmfLoan,
                        cashadvance,
                        acdiLoan,
                        prulife,
                        telephone,
                        sssCalamity,
                        hdmfCalamity,
                        otherLoan1,
                        otherLoan2,
                        otherLoan3,
                        otherLoan4,
                        csbLoan,
                        sbLoan,
                        otherEmployeeReceivable,
                        otherEmployeePayable,
                        otherEmployeeAdjustment,
                        totalDeduction,
                        totalNetPay,
                        statusName,
                        dtStatus,
                        statusByUser,
                        payrollBy,
                        isActive,
                        dtAdded,
                        addedByUser,
                        v13thMonth,
                        v13thMonthAndNonTaxableAllowance,
                        v14thMonth,
                        v14thMonthAndNonTaxable,
                        leaveCount,
                        leaveAmount,
                        payrollType,
                        bankCode,
                        accountNo,
                        cateringOT,
                        healthcard,
                        parking,
                        meals,
                        fixedOthers,
                        totalFixedDeduction,
                        totalMBOS,
                        additionalMbos,
                        
                        reg_basic_al,
                        tardy_al,
                        undertime_al,
                        absent_al,
                        salary_adjustment_al,

                        lh_basic_al,
                        lh_nd_al,
                        lh_ot_al,

                        rd_basic_al,

                        reg_nd_al,
                        reg_ndot_al,
                        reg_ot_al,

                        sh_basic_al,
                        sh_nd_al,
                        sh_ndot_al,
                        sh_ot_al,
                        employeeLedger,
                        hmoLoan
                    ) 
                    VALUES (
                        @methodType,
                        @cutOffType,
                        @dateMonth,
                        @dateYear,
                        @dateFrom,
                        @dateTo,
                        @branchCode,
                        @departmentCode,
                        @employmentStatus,
                        @positionCode,
                        @rankCode,
                        @activeStatus,
                        @employeeNo,
                        AES_ENCRYPT(@dailyRate, 'portalkeisan'),
                        AES_ENCRYPT(@basicPay, 'portalkeisan'),
                        AES_ENCRYPT(@basicPaySemi, 'portalkeisan'),
                        @longPay,
                        @render,
                        @renderOT,
                        @renderREST,
                        @renderRESTOT,
                        @renderNSD,
                        @renderNSDOT,
                        @renderNSDREST,
                        @renderNSDRESTOT,
                        @amount,
                        @amountOT,
                        @amountREST,
                        @amountRESTOT,
                        @amountNSD,
                        @amountNSDOT,
                        @amountNSDREST,
                        @amountNSDRESTOT,
                        @renderL,
                        @renderOTL,
                        @renderRESTL,
                        @renderRESTOTL,
                        @renderNSDL,
                        @renderNSDOTL,
                        @renderNSDRESTL,
                        @renderNSDRESTOTL,
                        @amountL,
                        @amountOTL,
                        @amountRESTL,
                        @amountRESTOTL,
                        @amountNSDL,
                        @amountNSDOTL,
                        @amountNSDRESTL,
                        @amountNSDRESTOTL,
                        @renderS,
                        @renderOTS,
                        @renderRESTS,
                        @renderRESTOTS,
                        @renderNSDS,
                        @renderNSDOTS,
                        @renderNSDRESTS,
                        @renderNSDRESTOTS,
                        @amountS,
                        @amountOTS,
                        @amountRESTS,
                        @amountRESTOTS,
                        @amountNSDS,
                        @amountNSDOTS,
                        @amountNSDRESTS,
                        @amountNSDRESTOTS,
                        @nonBasicPay,
                        @allowanceDeductionAbsent,
                        @absentCount,
                        @presentCount,
                        @lessDayCount,
                        @workOnOffPresentCount,
                        @legalPresentCount,
                        @specialPresentCount,
                        @absentAmount,
                        @presentAmount,
                        @lessDayAmount,
                        @workOnOffPresentAmount,
                        @legalPresentAmount,
                        @specialPresentAmount,
                        @totalRenderEarly,
                        @totalRenderLate,
                        @totalRenderUndertime,
                        @totalRenderOvertime,
                        @totalAmountEarly,
                        @totalAmountLate,
                        @totalAmountUndertime,
                        @totalAmountOvertime,
                        @totalDeductionLateUndertimeAbsent,
                        @totalGrossPay,
                        @totalGrossPayMandatory,
                        AES_ENCRYPT(@grossIncome, 'portalkeisan'),
                        @rataAmount,
                        @allowanceDeductionLate,
                        @riceAllowanceAmount,
                        @communicationAllowance,
                        @travelAllowance,
                        @otherAllowance,
                        @totalAllowance,
                        @allowanceNonTaxable,
                        @allowanceTaxable,
                        @deductionSSSemployee,
                        @deductionSSSemployer,
                        @deductionWISPemployee,
                        @deductionWISPemployer,
                        @deductionSSSec,
                        @deductionPHIemployee,
                        @deductionPHIemployer,
                        @deductionPIFemployee,
                        @deductionPIFemployer,
                        @deductionPFemployee,
                        @deductionPFemployer,
                        @totalMandatory,
                        @otherIncome,
                        @taxableIncome,
                        @withHeldTax,
                        @amountLoan,
                        @sssLoan,
                        @hdmfLoan,
                        @cashadvance,
                        @acdiLoan,
                        @prulife,
                        @telephone,
                        @sssCalamity,
                        @hdmfCalamity,
                        @otherLoan1,
                        @otherLoan2,
                        @otherLoan3,
                        @otherLoan4,
                        @csbLoan,
                        @sbLoan,
                        @otherEmployeeReceivable,
                        @otherEmployeePayable,
                        @otherEmployeeAdjustment,
                        @totalDeduction,
                        AES_ENCRYPT(@totalNetPay, 'portalkeisan'),
                        @statusName,
                        NOW(),
                        @statusByUser,
                        @payrollBy,
                        @isActive,
                        NOW(),
                        @addedByUser,
                        @v13thMonth,
                        @v13thMonthAndNonTaxableAllowance,
                        @v14thMonth,
                        @v14thMonthAndNonTaxable,
                        @leaveCount,
                        @leaveAmount,
                        @payrollType,
                        @bankCode,
                        @accountNo,
                        @cateringOT,
                        @healthcard,
                        @parking,
                        @meals,
                        @fixedOthers,
                        @totalFixedDeduction,
                        @totalMBOS,
                        @additionalMbos,
                        @reg_basic_al,
                        @tardy_al,
                        @undertime_al,
                        @absent_al,
                        @salary_adjustment_al,

                        @lh_basic_al,
                        @lh_nd_al,
                        @lh_ot_al,

                        @rd_basic_al,

                        @reg_nd_al,
                        @reg_ndot_al,
                        @reg_ot_al,

                        @sh_basic_al,
                        @sh_nd_al,
                        @sh_ndot_al,
                        @sh_ot_al,
                        @employeeLedger,
                        @healthcard
                    );";

                _db.Execute(sql, new
                {
                    // Map all the properties from your model to the table
                    methodType = 1,
                    cutOffType = model.cutOffType,
                    dateMonth = model.dateMonth,
                    dateYear = model.dateYear,
                    dateFrom = model.dateFrom,
                    dateTo = model.dateTo,
                    branchCode = empInfo.branchCode,
                    departmentCode = empInfo.departmentCode,
                    employmentStatus = empInfo.employmentStatus,
                    positionCode = empInfo.positionCode,
                    rankCode = empInfo.rankCode,
                    activeStatus = "Active",
                    employeeNo = model.employeeNo,
                    dailyRate = model.dailyRate,
                    basicPay = empInfo.basicPay,
                    basicPaySemi = model.basicPaySemi,
                    longPay = 0,
                    render = empInfo.render,
                    renderOT = empInfo.renderOT,
                    renderREST = empInfo.renderREST,
                    renderRESTOT = empInfo.renderRESTOT,
                    renderNSD = empInfo.renderNSD,
                    renderNSDOT = empInfo.renderNSDOT,
                    renderNSDREST = empInfo.renderNSDREST,
                    renderNSDRESTOT = empInfo.renderNSDRESTOT,
                    amount = model.amount,
                    amountOT = model.amountOT,
                    amountREST = model.amountREST,
                    amountRESTOT = model.amountRESTOT,
                    amountNSD = model.amountNSD,
                    amountNSDOT = model.amountNSDOT,
                    amountNSDREST = model.amountNSDREST,
                    amountNSDRESTOT = model.amountNSDRESTOT,
                    renderL = empInfo.renderL,
                    renderOTL = empInfo.renderOTL,
                    renderRESTL = empInfo.renderRESTL,
                    renderRESTOTL = empInfo.renderRESTOTL,
                    renderNSDL = empInfo.renderNSDL,
                    renderNSDOTL = empInfo.renderNSDOTL,
                    renderNSDRESTL = empInfo.renderNSDRESTL,
                    renderNSDRESTOTL = empInfo.renderNSDRESTOTL,
                    amountL = model.amountL,
                    amountOTL = model.amountOTL,
                    amountRESTL = model.amountRESTL,
                    amountRESTOTL = model.amountRESTOTL,
                    amountNSDL = model.amountNSDL,
                    amountNSDOTL = model.amountNSDOTL,
                    amountNSDRESTL = model.amountNSDRESTL,
                    amountNSDRESTOTL = model.amountNSDRESTOTL,
                    renderS = empInfo.renderS,
                    renderOTS = empInfo.renderOTS,
                    renderRESTS = empInfo.renderRESTS,
                    renderRESTOTS = empInfo.renderRESTOTS,
                    renderNSDS = empInfo.renderNSDS,
                    renderNSDOTS = empInfo.renderNSDOTS,
                    renderNSDRESTS = empInfo.renderNSDRESTS,
                    renderNSDRESTOTS = empInfo.renderNSDRESTOTS,
                    amountS = model.amountS,
                    amountOTS = model.amountOTS,
                    amountRESTS = model.amountRESTS,
                    amountRESTOTS = model.amountRESTOTS,
                    amountNSDS = model.amountNSDS,
                    amountNSDOTS = model.amountNSDOTS,
                    amountNSDRESTS = model.amountNSDRESTS,
                    amountNSDRESTOTS = model.amountNSDRESTOTS,
                    nonBasicPay = model.nonBasicPay,
                    allowanceDeductionAbsent = 0,
                    absentCount = empInfo.absentCount,
                    presentCount = empInfo.presentCount,
                    lessDayCount = 0,
                    workOnOffPresentCount = 0,
                    legalPresentCount = 0,
                    specialPresentCount = 0,
                    absentAmount = model.absentAmount,
                    presentAmount = model.presentAmount,
                    lessDayAmount = 0,
                    workOnOffPresentAmount = 0,
                    legalPresentAmount = model.amountL,
                    specialPresentAmount = model.amountS,
                    totalRenderEarly = 0,
                    totalRenderLate = empInfo.renderLate,
                    totalRenderUndertime = empInfo.renderUndertime,
                    totalRenderOvertime = empInfo.renderOvertime,
                    totalAmountEarly = 0,
                    totalAmountLate = model.amountLate,
                    totalAmountUndertime = model.amountUndertime,
                    totalAmountOvertime = model.nonBasicPay,
                    totalDeductionLateUndertimeAbsent = model.totalDeductionLateUndertimeAbsent,
                    totalGrossPay = model.totalGrossPay,
                    totalGrossPayMandatory = 0,
                    grossIncome = model.grossIncome,
                    rataAmount = model.rataAmount,
                    allowanceDeductionLate = 0,
                    riceAllowanceAmount = model.riceAllowanceAmount,
                    communicationAllowance = model.communicationAllowance,
                    travelAllowance = model.travelAllowance,
                    otherAllowance = model.otherAllowance,
                    totalAllowance = model.totalAllowance,
                    allowanceNonTaxable = model.allowanceNonTaxable,
                    allowanceTaxable = model.allowanceTaxable,
                    deductionSSSemployee = model.deductionSSSemployee,
                    deductionSSSemployer = model.deductionSSSemployer,
                    deductionWISPemployee = model.deductionWISPemployee,
                    deductionWISPemployer = model.deductionWISPemployer,
                    deductionSSSec = model.deductionSSSec,
                    deductionPHIemployee = model.deductionPHIemployee,
                    deductionPHIemployer = model.deductionPHIemployer,
                    deductionPIFemployee = model.deductionPIFemployee,
                    deductionPIFemployer = model.deductionPIFemployer,
                    deductionPFemployee = model.deductionPFemployee,
                    deductionPFemployer = model.deductionPFemployer,
                    totalMandatory = model.totalMandatory,
                    otherIncome = model.otherIncome,
                    taxableIncome = model.taxableIncome,
                    withHeldTax = Math.Round(Convert.ToDouble(model.withHeldTax), 2, MidpointRounding.AwayFromZero),
                    amountLoan = model.amountLoan,
                    sssLoan = model.sssLoan,
                    hdmfLoan = model.hdmfLoan,
                    cashadvance = model.cashadvance,
                    acdiLoan = model.acdiLoan,
                    prulife = model.prulife,
                    telephone = model.telephone,
                    sssCalamity = model.sssCalamity,
                    hdmfCalamity = model.hdmfCalamity,
                    otherLoan1 = model.otherLoan1,
                    otherLoan2 = model.otherLoan2,
                    otherLoan3 = model.otherLoan3,
                    otherLoan4 = model.otherLoan4,
                    csbLoan = model.csbLoan,
                    sbLoan = model.sbLoan,
                    otherEmployeeReceivable = model.otherEmployeeReceivable,
                    otherEmployeePayable = model.otherEmployeePayable,
                    otherEmployeeAdjustment = model.otherEmployeePayable,
                    totalDeduction = model.totalDeduction,
                    totalNetPay = model.totalNetPay,
                    healthcard = model.hmoLoan,
                    employeeLedger = model.employeeLedger,
                    parking = model.parking,
                    meals = model.meals,
                    fixedOthers = model.fixedOthers,
                    totalFixedDeduction = model.totalFixedDeduction,
                    totalMBOS = model.totalMBOS,
                    additionalMbos = model.additionalMbos,
                    statusName = "Open",
                    statusByUser = User.Identity?.Name ?? "System",
                    payrollBy = User.Identity?.Name ?? "System",
                    isActive = 1,
                    addedByUser = User.Identity?.Name ?? "System",
                    v13thMonth = 0,
                    v13thMonthAndNonTaxableAllowance = 0,
                    v14thMonth = 0,
                    v14thMonthAndNonTaxable = 0,
                    leaveCount = 0,
                    leaveAmount = 0,
                    payrollType = empInfo.payrollType,
                    bankCode = empInfo.bankCode,
                    accountNo = empInfo.accountNo,
                    cateringOT = 0,
                    reg_basic_al = model.reg_basic_al,
                    tardy_al = model.tardy_al,
                    undertime_al = model.undertime_al,
                    absent_al = model.absent_al,
                    salary_adjustment_al = model.salary_adjustment_al,

                    lh_basic_al = model.lh_basic_al,
                    lh_nd_al = model.lh_nd_al,
                    lh_ot_al = model.lh_ot_al,

                    rd_basic_al = model.rd_basic_al,

                    reg_nd_al = model.reg_nd_al,
                    reg_ndot_al = model.reg_nd_al,
                    reg_ot_al = model.reg_ot_al,

                    sh_basic_al = model.sh_basic_al,
                    sh_nd_al = model.sh_nd_al,
                    sh_ndot_al = model.sh_ndot_al,
                    sh_ot_al = model.sh_ot_al
                });


                return Json(new { success = true, message = "Completed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error in process payroll: {ex.Message}" });
            }
        }

    }
}