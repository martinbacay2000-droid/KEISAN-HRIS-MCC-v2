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
    [ModuleAuthorize("TprocessLastPayM")]
    public class ProcessLastPayController : BaseController
    {
        private readonly IDbConnection _db;

        public ProcessLastPayController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payroll/ProcessLastPay.cshtml");

        }

        [HttpGet]
        public JsonResult GetEmployeeList(string search)
        {
            try
            {
                var sql = @"
                SELECT 
                    b.employeeNo, 
                    CONCAT(
                        IFNULL(firstName, ''), ' ',
                        IFNULL(CONCAT(middleName, ' '), ''),
                        IFNULL(lastName, '')
                    ) as employeeName,
                    DATE_FORMAT(b.dateHired,'%m/%d/%Y') as dateHired,
                    DATE_FORMAT(dateOfEmpTermInitial,'%m/%d/%Y') as dateResigned,
                    b.employmentStatus as empStatus,

                    IFNULL(r.amount_lastcutoff,0) as amount_lastcutoff,
                    IFNULL(r.amount_adjustment,0) as amount_adjustment,
                    IFNULL(r.amount_13thmonth,0) as amount_13thmonth,
                    IFNULL(r.amount_taxRefund,0) as amount_taxRefund,
                    IFNULL(r.amount_vl,0) as amount_vl,
                    IFNULL(r.amount_sl,0) as amount_sl,

                    IFNULL(r.lastpayAmount,0) as lastPayAmount,
                    IFNULL(r.statusName,'Open') as lastPayStatus

                FROM e_basicinfo b
                LEFT JOIN rq_lastpay r ON r.employeeNo = b.employeeNo 
                WHERE b.isActive = 0
                    AND (
                        @search IS NULL
                        OR b.employeeNo LIKE CONCAT('%', @search, '%')
                        OR CONCAT(
                            IFNULL(firstName, ''), ' ',
                            IFNULL(CONCAT(middleName, ' '), ''),
                            IFNULL(lastName, '')
                        ) LIKE CONCAT('%', @search, '%')
                    )
                ORDER BY firstName, lastName";

                return Json(_db.Query(sql, new { search }).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // CRUD Operations
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

        [HttpGet]
        public JsonResult GetLastCutoff(string employeeNo)
        {
            var query = @"
                SELECT 
                    e.employeeNo,
                    CONCAT(DATE_FORMAT(e.dateFrom,'%m/%d/%Y'), ' - ', DATE_FORMAT(e.dateTo,'%m/%d/%Y')) AS cutOffType,
                    CAST(CAST(AES_DECRYPT(e.totalNetPay,'portalkeisan') AS CHAR(200)) AS DECIMAL(10,2)) AS amount_netpay,
                    e.cutOffType AS cutOffTypeCode,
                    e.dateMonth,
                    e.dateYear,
                    e.statusName

                FROM p_biometrics e

                WHERE e.employeeNo = @employeeNo
                AND e.isActive = 1
                AND e.statusName IN ('Posted', 'POSTED')

                ORDER BY e.id DESC
                LIMIT 1
            ";

            var requests = _db.Query<LastPayModel>(query, new { employeeNo }).ToList();
            return Json(new { data = requests });
        }

        [HttpGet]
        public JsonResult GetLastPayAdjustment(string employeeNo)
        {
            var query = @"
                SELECT 

                    CAST(SUM(e.approvedAmount) AS DECIMAL(10,2)) AS otherEmployeePayable

                FROM c_payable e

                WHERE e.employeeNo = @employeeNo
                AND e.isActive = 1
                AND e.statusName = 'Approved'
                AND LEFT(e.adjustmentCode,7) = 'LASTPAY'
            ";

            var requests = _db.Query<LastPayModel>(query, new { employeeNo }).ToList();
            return Json(new { data = requests });
        }

        [HttpGet]
        public JsonResult GetLastPayDeduction(string employeeNo)
        {
            var query = @"
                SELECT 

                    CAST(SUM(e.approvedAmount) AS DECIMAL(10,2)) AS otherEmployeeReceivable

                FROM c_receivable e

                WHERE e.employeeNo = @employeeNo
                AND e.isActive = 1
                AND e.statusName = 'Approved'
                AND LEFT(e.otherDeductionCode,7) = 'LASTPAY'
            ";

            var requests = _db.Query<LastPayModel>(query, new { employeeNo }).ToList();
            return Json(new { data = requests });
        }

        [HttpGet]
        public JsonResult GetTaxRefund(string employeeNo)
        {
            var query = @"
                SELECT 
	                taxable,
	                wtax,
	                wtax - taxDue AS amount_taxRefund
                FROM 
                (
	                SELECT
		                wtax,
		                taxable,
		                CAST((SELECT 
					                (taxable - taxCompensationRangeMin) * (taxPercent/100) + taxPWT
				                FROM s_taxtable t  
				                WHERE taxCompensationRangeMin <=  CAST(taxable AS DECIMAL(10,2))
				                AND CASE WHEN taxCompensationRangeMax = 0 
				                THEN taxCompensationRangeMin <=  CAST(taxable AS DECIMAL(10,2))
				                ELSE taxCompensationRangeMax >= CAST(taxable AS DECIMAL(10,2)) END  
				                AND taxType = 'Annual' 
				                AND t.effectivityDate <= NOW()
				                ) 
		                AS DECIMAL(10,2)) AS taxDue
		
	                FROM 
	
	                (	SELECT 		
		                   SUM(p.withHeldTax) as wtax,   
		   
		                   CAST(SUM(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200))) 
			                - SUM(p.absentAmount)
		                   - SUM(p.totalAmountLate)
		                   - SUM(p.totalAmountUndertime)	
		                   - SUM(p.deductionSSSemployee)	
		                   - SUM(p.deductionWISPemployee)	
		                   - SUM(p.deductionPHIemployee)	
		                   - SUM(p.deductionPIFemployee)	
			                AS DECIMAL(10,2)) as taxable
		
		                FROM p_biometrics p
		                LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
		                WHERE p.employeeNo = @employeeNo
		                AND p.isActive = 1
		                AND p.statusName = 'POSTED'
		                AND p.dateYear = YEAR(b.dateOfEmpTermInitial)
	                ) t1
                ) t2
            ";

            var requests = _db.Query<LastPayModel>(query, new { employeeNo }).ToList();
            return Json(new { data = requests });
        }

        [HttpGet]
        public JsonResult Get13thMonthPay(string employeeNo)
        {
            var query = @"
                SELECT 
                    CAST((basicPaySemi 
                    + adjustment    
                    - totalAmountLate 
                    - totalAmountUndertime 
                    - absentAmount
	 
	                 + allow_basic
	                 + allow_adjustment
	                 - allow_tardy
	                 - allow_undertime
	                 - allow_absent	 
	                 )/12 AS DECIMAL(10,2))
                    AS amount_13thmonth
                FROM
                (
                SELECT 
                    p.dateYear,
                    p.dateMonth,
                    case when p.cutOffType = 1 THEN '1st' ELSE '2nd' END AS cutoffType,
                    p.employeeNo,
                    CAST(SUM(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200))) AS DECIMAL(10,2)) as basicPaySemi, 
                    SUM(p.absentAmount) AS absentAmount,
                    SUM(p.totalAmountLate) AS totalAmountLate,
                    SUM(p.totalAmountUndertime) AS totalAmountUndertime,
    
                    SUM(p.reg_basic_al) AS allow_basic,
                    SUM(p.tardy_al) AS allow_tardy,
                    SUM(p.undertime_al) AS allow_undertime,    
                    SUM(p.absent_al) AS allow_absent, 
                    SUM(p.salary_adjustment_al) AS allow_adjustment,
    
                    IFNULL(t_13.adj_13th,0) AS adjustment
	
                FROM p_biometrics p

                LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                LEFT JOIN rq_13thmonth rq ON p.employeeNo = p.employeeNo
                AND rq.isActive = 1
                AND rq.statusName = 'Approved'
                AND rq.dateYear = YEAR(b.dateOfEmpTermInitial)

                LEFT JOIN 
                    (SELECT 
                        cp.employeeNo, 
                        SUM(cp.approvedAmount) AS adj_13th		
                    FROM c_payable cp 
                    LEFT JOIN e_basicinfo ba ON ba.employeeNo = cp.employeeNo
                    WHERE cp.adjustmentCode IN ('TK ADJ','TARDY', 'INCLOGS')
                    AND cp.statusName IN ('Approved','Processed') AND cp.isActive=1
	                 AND cp.dateToAdjustment BETWEEN DATE(CONCAT(YEAR(ba.dateOfEmpTermInitial)-1,'-12-26')) AND DATE(CONCAT(YEAR(ba.dateOfEmpTermInitial),'-12-25'))  ) 
                    AS t_13 ON t_13.employeeNo = p.employeeNo
		
                WHERE p.employeeNo = @employeeNo
                AND p.isActive = 1
                AND p.statusName = 'POSTED'

                -- CUTOFF DECEMBER TO NOVEMBER --
                AND	((  p.dateYear   = YEAR(b.dateOfEmpTermInitial) 		AND p.dateMonth <> 'December')
                        OR (p.dateYear = YEAR(b.dateOfEmpTermInitial) - 1 	AND p.dateMonth = 'December')) 

                GROUP BY p.employeeNo -- , p.dateFrom
                ORDER BY p.dateFrom) tbl;
            ";

            var requests = _db.Query<LastPayModel>(query, new { employeeNo }).ToList();
            return Json(new { data = requests });
        }

        [HttpGet]
        public JsonResult GetSLConversion(string employeeNo)
        {
            var query = @"
                SELECT 
	                m.availableBalance,
	                CAST(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan') AS CHAR(200)) AS DECIMAL(10,2)) AS dailyRate,	
	                CAST(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan') AS CHAR(200)) * m.availableBalance AS DECIMAL(10,2)) AS amount_SL
                FROM m_leave m
                LEFT JOIN e_payrolldetails p ON p.employeeNo = m.employeeNo
                WHERE m.leaveCode = 'SL'
                AND m.employeeNo = @employeeno
                ORDER BY m.id DESC LIMIT 1
            ";

            var requests = _db.Query<LastPayModel>(query, new { employeeNo }).ToList();
            return Json(new { data = requests });
        }

        [HttpGet]
        public JsonResult GetVLConversion(string employeeNo)
        {
            var query = @"
                SELECT 
	                m.availableBalance,
	                CAST(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan') AS CHAR(200)) AS DECIMAL(10,2)) AS dailyRate,	
	                CAST(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan') AS CHAR(200)) * m.availableBalance AS DECIMAL(10,2)) AS amount_VL
                FROM m_leave m
                LEFT JOIN e_payrolldetails p ON p.employeeNo = m.employeeNo
                WHERE m.leaveCode = 'VL'
                AND m.employeeNo = @employeeno
                ORDER BY m.id DESC LIMIT 1
            ";

            var requests = _db.Query<LastPayModel>(query, new { employeeNo }).ToList();
            return Json(new { data = requests });
        }

        [HttpPost]
        public JsonResult ProcessLastPayAmount([FromBody] LastPayModel model)
        {
            var userCode = HttpContext.Session.GetString("employeeNo");
            if (model == null)
            {
                return Json(new { success = false, message = "Model is null" });
            }

            try
            {
                string sql = @"
                    DELETE FROM rq_lastpay 
                    WHERE employeeNo = @employeeNo
                    AND statusName = 'Open'
                    AND isActive = 1;

                    INSERT INTO rq_lastpay (
                        employeeNo,
                        employmentStatus,
                        dateHired,
                        dateResigned,
                       -- lastCufoffDateFrom,
                       -- lastCutOffDateTo,
                        amount_lastcutoff,
                        amount_adjustment,
                        amount_13thmonth,
                        amount_taxRefund,
                        amount_sl,
                        amount_vl,
                        lastPayAmount,
                        remarks,
                        statusName,
                        dtStatus,
                        statusByUser,
                        dtAdded,
                        addedByUser,
                        isActive
                    )

                    VALUES (
                        @employeeNo,
                        @employmentStatus,
                        @dateHired,
                        @dateResigned,
                       -- @lastCufoffDateFrom,
                       --  @lastCutOffDateTo,
                        @amount_netpay,
                        @amount_adjustment,
                        @amount_13thmonth,
                        @amount_taxRefund,
                        @amount_sl,
                        @amount_vl,
                        @lastPayAmount,
                        @remarks,                        
                        @statusName,
                        @dtStatus,
                        @statusByUser,
                        @dtAdded,
                        @addedByUser,
                        1
                    )
                ";

                _db.Execute(sql, new
                {
                    employeeNo = model.employeeNo,
                    employmentStatus = model.employmentStatus,
                    dateHired = model.dateHired,
                    dateResigned = model.dateResigned,
                    amount_netpay = model.amount_netpay,
                    amount_adjustment = model.amount_adjustment,
                    amount_13thmonth = model.amount_13thmonth,
                    amount_taxRefund = model.amount_taxRefund,
                    amount_sl = model.amount_sl,
                    amount_vl = model.amount_vl,
                    lastPayAmount = Math.Round(Convert.ToDouble(model.lastPayAmount), 2, MidpointRounding.AwayFromZero),
                    remarks = model.remarks,
                    statusName = "Open",
                    dtStatus = DateTime.Now,
                    statusByUser = userCode,
                    dtAdded = DateTime.Now,
                    addedByUser = userCode

                });

                return Json(new { success = true, message = "Process last pay completed." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error in processing last pay: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult PostLastPay([FromBody] LastPayModel model)
        {
            var userCode = HttpContext.Session.GetString("employeeNo");
            if (model == null)
            {
                return Json(new { success = false, message = "Model is null" });
            }

            try
            {
                string sql = @"
                    UPDATE rq_lastpay 

                    SET statusName = 'Posted', dtStatus = @dtStatus, statusByUser = @statusByUser

                    WHERE employeeNo = @employeeNo
                    AND statusName = 'Open'
                    AND isActive = 1;
                ";

                _db.Execute(sql, new
                {
                    employeeNo = model.employeeNo,
                    statusName = "Posted",
                    dtStatus = DateTime.Now,
                    statusByUser = userCode

                });

                return Json(new { success = true, message = "Last Pay Posted." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error in posting last pay: {ex.Message}" });
            }
        }
    }
}