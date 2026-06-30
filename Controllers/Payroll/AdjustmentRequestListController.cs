using Dapper;
using KEISAN_HRIS_v2.Models.Payroll;
using Microsoft.AspNetCore.Mvc;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Helpers;
using System.Data;
using System.Text;
using KEISAN_HRIS_v2.Services.OtherServices;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    [ModuleAuthorize("RadjustmentM")]
    public class AdjustmentRequestListController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;
        private const string ADMIN_ROLE = "RL-000000";

        public AdjustmentRequestListController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        private string CurrentRoleCode => HttpContext.Session.GetString("roleCode");
        private bool IsAdmin => CurrentRoleCode == ADMIN_ROLE;

        // READWRITE or FULL: can Add and Edit adjustment requests
        private bool CanWrite => IsAdmin || AccessHelper.CanCreate(HttpContext, "RadjustmentM");
        // READWRITE or FULL: can see Daily Rate panel and trigger auto-calculation
        private bool CanViewRates => IsAdmin || AccessHelper.CanCreate(HttpContext, "RadjustmentM");
        // FULL only: can see the Daily Allowance row inside the rate panel
        private bool CanViewAllowance => IsAdmin || AccessHelper.CanDelete(HttpContext, "RadjustmentM");
        // FULL only: can Approve or Decline requests
        private bool CanApprove => IsAdmin || AccessHelper.CanDelete(HttpContext, "RadjustmentM");

        public IActionResult Index()
        {
            ViewBag.CanWrite = CanWrite;
            ViewBag.CanViewRates = CanViewRates;
            ViewBag.CanViewAllowance = CanViewAllowance;
            ViewBag.CanApprove = CanApprove;
            return View("~/Views/Payroll/AdjustmentRequestList.cshtml");
        }

        // Get all active Adjustment Requests with filters
        [HttpGet]
        public JsonResult GetAdjustmentRequestList(string status, string adjustmentType, string dateFrom, string dateTo)
        {
            var query = new StringBuilder(@"
                SELECT 
                    ap.id, ap.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS employeeName,
                    ap.adjustmentCode,
                    adj.adjustmentName AS adjustmentType,
                    ap.amount AS requestedAmount,
                    DATE_FORMAT(ap.dateToAdjustment, '%m/%d/%Y') as effectivityDate,
                    ap.reason AS remarks,
                    ap.statusName,
                    ap.DayType,
                    ap.Value,
                    ap.Units,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', LEFT(IFNULL(req.middleName,''), 1), '.') AS requestedByUser,
                    ap.dtAdded
                FROM c_payable ap
                JOIN e_basicinfo b ON b.employeeNo = ap.employeeNo
                JOIN s_adjustment adj ON adj.adjustmentCode = ap.adjustmentCode
                LEFT JOIN e_basicinfo req ON req.employeeNo = ap.requestedByUser
                WHERE ap.isActive = 1");

            var parameters = new DynamicParameters();

            // Apply status filter - default to Pending
            if (string.IsNullOrWhiteSpace(status) || status.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND ap.statusName = @status");
                parameters.Add("@status", "Pending");
            }
            else if (!status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND ap.statusName = @status");
                parameters.Add("@status", status);
            }

            // Apply adjustment type filter
            if (!string.IsNullOrWhiteSpace(adjustmentType) && !adjustmentType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND ap.adjustmentCode = @adjustmentType");
                parameters.Add("@adjustmentType", adjustmentType);
            }

            // Apply date range filter 
            if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
            {
                query.Append(" AND ap.dateToAdjustment BETWEEN STR_TO_DATE(@dateFrom, '%m/%d/%Y') AND STR_TO_DATE(@dateTo, '%m/%d/%Y')");
                parameters.Add("@dateFrom", dateFrom);
                parameters.Add("@dateTo", dateTo);
            }

            query.Append(" ORDER BY ap.id DESC");

            var requests = _db.Query<dynamic>(query.ToString(), parameters).ToList();
            return Json(new { data = requests });
        }

        // Get single Adjustment Request by ID
        [HttpGet]
        public JsonResult GetAdjustmentRequest(int id)
        {
            try
            {
                var sql = @"
                    SELECT 
                        ap.id, ap.employeeNo, ap.adjustmentCode,
                        ap.amount AS requestedAmount,
                        DATE_FORMAT(ap.dateToAdjustment, '%Y-%m-%d') as effectivityDate,
                        ap.reason AS remarks, ap.statusName,
                        ap.DayType, ap.Value, ap.Units,
                        CONCAT(IFNULL(e.firstName, ''), ' ', IFNULL(CONCAT(e.middleName, ' '), ''), IFNULL(e.lastName, '')) as employeeName,
                        adj.adjustmentName AS adjustmentType,
                        CONCAT(IFNULL(req.firstName, ''), ' ', IFNULL(CONCAT(req.middleName, ' '), ''), IFNULL(req.lastName, '')) as requestedByUser
                    FROM c_payable ap
                    LEFT JOIN e_basicinfo e ON ap.employeeNo = e.employeeNo
                    LEFT JOIN s_adjustment adj ON adj.adjustmentCode = ap.adjustmentCode
                    LEFT JOIN e_basicinfo req ON req.employeeNo = ap.requestedByUser
                    WHERE ap.id = @Id AND ap.isActive = 1";

                return Json(_db.QueryFirstOrDefault<dynamic>(sql, new { Id = id }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAdjustmentRequest: {ex.Message}");
                return Json(null);
            }
        }

        // Get list of active employees for dropdown
        [HttpGet]
        public JsonResult GetEmployeeList()
        {
            try
            {
                var sql = @"
                    SELECT 
                        employeeNo, 
                        CONCAT(IFNULL(firstName, ''), ' ', IFNULL(CONCAT(middleName, ' '), ''), IFNULL(lastName, '')) as employeeName
                    FROM e_basicinfo 
                    WHERE isActive = 1 
                    ORDER BY firstName, lastName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // Get active adjustment types for dropdown
        [HttpGet]
        public JsonResult GetAdjustmentTypes()
        {
            try
            {
                var adjustmentSql = @"
                    SELECT adjustmentCode AS value, adjustmentName AS text
                    FROM s_adjustment 
                    WHERE isActive = 1 
                    ORDER BY adjustmentName";

                var types = _db.Query<dynamic>(adjustmentSql).ToList();

                return Json(types);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAdjustmentTypes: {ex.Message}");
                return Json(new List<dynamic>());
            }
        }

        // Get all dropdown types for Time Keeping Adjustment
        [HttpGet]
        public JsonResult GetTimeKeepingDropdowns()
        {
            try
            {
                var dropdowns = new
                {
                    dayTypes = new List<object>
                    {
                        // Regular Day
                        new { value = "Regular Day - Duty", text = "Regular Day - Duty" },
                        new { value = "Regular Day - Late", text = "Regular Day - Late" },
                        new { value = "Regular Day - Undertime", text = "Regular Day - Undertime" },
                        new { value = "Regular Day - Absent", text = "Regular Day - Absent" },
                        new { value = "Regular Day - OT", text = "Regular Day - OT (Overtime)" },
                        new { value = "Regular Day - ND", text = "Regular Day - ND (Night Differential)" },
                        new { value = "Regular Day - OTND", text = "Regular Day - OTND (OT + Night Differential)" },
                        
                        // Rest Day
                        new { value = "Rest Day - Duty", text = "Rest Day - Duty" },
                        new { value = "Rest Day - Duty Monthly", text = "Rest Day - Duty (Monthly)" },
                        new { value = "Rest Day - OT", text = "Rest Day - OT" },
                        new { value = "Rest Day - ND", text = "Rest Day - ND" },
                        new { value = "Rest Day - OTND", text = "Rest Day - OTND" },
                        
                        // Special Holiday
                        new { value = "Special Holiday - Duty", text = "Special Holiday - Duty" },
                        new { value = "Special Holiday - Duty Monthly", text = "Special Holiday - Duty (Monthly)" },
                        new { value = "Special Holiday - OT", text = "Special Holiday - OT" },
                        new { value = "Special Holiday - ND", text = "Special Holiday - ND" },
                        new { value = "Special Holiday - OTND", text = "Special Holiday - OTND" },
                        
                        // Rest Day + Special Holiday
                        new { value = "Rest Day Special Holiday - Duty", text = "Rest Day + Special Holiday - Duty" },
                        new { value = "Rest Day Special Holiday - OT", text = "Rest Day + Special Holiday - OT" },
                        new { value = "Rest Day Special Holiday - ND", text = "Rest Day + Special Holiday - ND" },
                        new { value = "Rest Day Special Holiday - OTND", text = "Rest Day + Special Holiday - OTND" },
                        
                        // Regular/Legal Holiday
                        new { value = "Regular Holiday - Duty", text = "Regular Holiday - Duty" },
                        new { value = "Regular Holiday - OT", text = "Regular Holiday - OT" },
                        new { value = "Regular Holiday - ND", text = "Regular Holiday - ND" },
                        new { value = "Regular Holiday - OTND", text = "Regular Holiday - OTND" },
                        
                        // Rest Day + Regular Holiday
                        new { value = "Rest Day Regular Holiday - Duty", text = "Rest Day + Regular Holiday - Duty" },
                        new { value = "Rest Day Regular Holiday - OT", text = "Rest Day + Regular Holiday - OT" },
                        new { value = "Rest Day Regular Holiday - ND", text = "Rest Day + Regular Holiday - ND" },
                        new { value = "Rest Day Regular Holiday - OTND", text = "Rest Day + Regular Holiday - OTND" }
                    },
                    unitTypes = new List<object>
                    {
                        new { value = "Hour", text = "Hour" },
                        new { value = "Minute", text = "Minute" }
                    }
                };

                return Json(dropdowns);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTimeKeepingDropdowns: {ex.Message}");
                return Json(new
                {
                    dayTypes = new List<object>(),
                    unitTypes = new List<object>()
                });
            }
        }

        // Get employee daily rate - UPDATED WITH DECRYPTION AND TYPE CONVERSION
        [HttpGet]
        public JsonResult GetEmployeeDailyRate(string employeeNo)
        {
            try
            {
                // Only FULL access or Admin can retrieve salary data
                if (!CanViewRates)
                    return Json(new { success = false, message = "Unauthorized: You do not have permission to view salary information." });

                var sql = @"
                    SELECT
                        CAST(IFNULL(CAST(AES_DECRYPT(dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS dailyRate,
                        CAST(IFNULL(CAST(AES_DECRYPT(hourlyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS hourlyRate,
                        CAST(IFNULL(CAST(AES_DECRYPT(basicSalary,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS basicSalary,
                        CAST(IFNULL(CAST(AES_DECRYPT(basicMonthlyPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS basicMonthlyPay
                    FROM e_payrolldetails 
                    WHERE employeeNo = @employeeNo AND isActive = 1
                    ORDER BY effectivityDate DESC
                    LIMIT 1";

                var result = _db.QueryFirstOrDefault<dynamic>(sql, new { employeeNo });

                if (result != null)
                {
                    var allowanceSql = @"
                        SELECT CAST(ea.allowanceAmount AS DECIMAL(10,2)) AS allowanceAmount
                        FROM e_allowance ea
                        WHERE ea.employeeNo = @employeeNo
                          AND ea.allowanceCode = 'Basic Allowance'
                          AND ea.isActive = 1
                          AND (ea.dtDeleted IS NULL OR ea.dtDeleted = '0000-00-00 00:00:00')
                        ORDER BY ea.id DESC
                        LIMIT 1";

                    var allowanceResult = _db.QueryFirstOrDefault<dynamic>(allowanceSql, new { employeeNo });
                    double allowanceDailyRate = allowanceResult != null
                        ? Math.Round(Convert.ToDouble(allowanceResult.allowanceAmount) / 313 * 12, 2)
                        : 0;

                    double dailyRate = Convert.ToDouble(result.dailyRate);
                    double hourlyRate = Convert.ToDouble(result.hourlyRate);
                    double basicSalary = Convert.ToDouble(result.basicSalary);
                    double basicMonthly = Convert.ToDouble(result.basicMonthlyPay);

                    // FULL: return allowance value and tell JS to show the allowance row
                    if (CanViewAllowance)
                    {
                        return Json(new
                        {
                            success = true,
                            dailyRate,
                            hourlyRate,
                            basicSalary,
                            basicMonthlyPay = basicMonthly,
                            allowanceDailyRate,
                            showAllowance = true
                        });
                    }

                    // READWRITE: return rate data only, allowance row stays hidden in UI
                    return Json(new
                    {
                        success = true,
                        dailyRate,
                        hourlyRate,
                        basicSalary,
                        basicMonthlyPay = basicMonthly,
                        showAllowance = false
                    });
                }

                return Json(new { success = false, message = "Employee payroll details not found" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployeeDailyRate: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get salary rate multipliers from s_salaryrates
        [HttpGet]
        public JsonResult GetSalaryRates()
        {
            try
            {
                var sql = @"
                    SELECT 
                        RegularDuty, RegularOT, RegularND, RegularOTND,
                        RD, RDMonthly, RDOT, RDND, RDOTND,
                        SH, SHMonthly, SHOT, SHND, SHOTND,
                        RDSH, RDSHOT, RDSHND, RDSHOTND,
                        RH, RHOT, RHND, RHOTND,
                        RDRH, RDRHOT, RDRHND, RDRHOTND
                    FROM s_salaryrates 
                    WHERE isActive = 1 
                    ORDER BY effectivityDate DESC 
                    LIMIT 1";

                var rates = _db.QueryFirstOrDefault<dynamic>(sql);

                if (rates != null)
                {
                    return Json(new { success = true, rates = rates });
                }

                return Json(new { success = false, message = "Salary rates not found" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetSalaryRates: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Calculate adjustment amount - WITH DECRYPTION AND PROPER TYPE CONVERSION
        [HttpPost]
        public JsonResult CalculateAdjustmentAmount(string employeeNo, string dayType, double value, string unit)
        {
            try
            {
                // READWRITE and FULL can calculate; allowance is always used in math server-side
                if (!CanViewRates)
                {
                    return Json(new { success = false, message = "Unauthorized: Only users with Read/Write or Full Access can calculate salary-based amounts." });
                }

                var employeeRateSql = @"
                    SELECT
                        CAST(IFNULL(CAST(AES_DECRYPT(dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS dailyRate,
                        CAST(IFNULL(CAST(AES_DECRYPT(hourlyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2)) AS hourlyRate
                    FROM e_payrolldetails 
                    WHERE employeeNo = @employeeNo AND isActive = 1
                    ORDER BY effectivityDate DESC
                    LIMIT 1";

                var employeeRate = _db.QueryFirstOrDefault<dynamic>(employeeRateSql, new { employeeNo });

                if (employeeRate == null)
                    return Json(new { success = false, message = "Employee payroll details not found" });

                // Convert decimal to double explicitly
                double dailyRate = Convert.ToDouble(employeeRate.dailyRate);

                // Fetch Basic Allowance and compute daily allowance rate
                var allowanceSql = @"
                    SELECT CAST(ea.allowanceAmount AS DECIMAL(10,2)) AS allowanceAmount
                    FROM e_allowance ea
                    WHERE ea.employeeNo = @employeeNo
                      AND ea.allowanceCode = 'Basic Allowance'
                      AND ea.isActive = 1
                      AND (ea.dtDeleted IS NULL OR ea.dtDeleted = '0000-00-00 00:00:00')
                    ORDER BY ea.id DESC
                    LIMIT 1";

                var allowanceResult = _db.QueryFirstOrDefault<dynamic>(allowanceSql, new { employeeNo });
                double allowanceDailyRate = allowanceResult != null ? Math.Round(Convert.ToDouble(allowanceResult.allowanceAmount) / 313 * 12, 2) : 0;
                double combinedDailyRate = dailyRate + allowanceDailyRate;

                // Get salary rates
                var salaryRatesSql = @"
                    SELECT *
                    FROM s_salaryrates 
                    WHERE isActive = 1 
                    ORDER BY effectivityDate DESC 
                    LIMIT 1";

                var salaryRates = _db.QueryFirstOrDefault<dynamic>(salaryRatesSql);

                if (salaryRates == null)
                    return Json(new { success = false, message = "Salary rates not found" });

                // Map day type to field name
                var dayTypeFieldMap = new Dictionary<string, string>
                {
                    // Regular Day
                    { "Regular Day - Duty", "RegularDuty" },
                    { "Regular Day - Late", "RegularDuty" },
                    { "Regular Day - Undertime", "RegularDuty" },
                    { "Regular Day - Absent", "RegularDuty" },
                    { "Regular Day - OT", "RegularOT" },
                    { "Regular Day - ND", "RegularND" },
                    { "Regular Day - OTND", "RegularOTND" },
                    
                    // Rest Day
                    { "Rest Day - Duty", "RD" },
                    { "Rest Day - Duty Monthly", "RDMonthly" },
                    { "Rest Day - OT", "RDOT" },
                    { "Rest Day - ND", "RDND" },
                    { "Rest Day - OTND", "RDOTND" },
                    
                    // Special Holiday
                    { "Special Holiday - Duty", "SH" },
                    { "Special Holiday - Duty Monthly", "RDMonthly" },
                    { "Special Holiday - OT", "SHOT" },
                    { "Special Holiday - ND", "SHND" },
                    { "Special Holiday - OTND", "SHOTND" },
                    
                    // Rest Day + Special Holiday
                    { "Rest Day Special Holiday - Duty", "RDSH" },
                    { "Rest Day Special Holiday - OT", "RDSHOT" },
                    { "Rest Day Special Holiday - ND", "RDSHND" },
                    { "Rest Day Special Holiday - OTND", "RDSHOTND" },
                    
                    // Regular/Legal Holiday
                    { "Regular Holiday - Duty", "RH" },
                    { "Regular Holiday - OT", "RHOT" },
                    { "Regular Holiday - ND", "RHND" },
                    { "Regular Holiday - OTND", "RHOTND" },
                    
                    // Rest Day + Regular Holiday
                    { "Rest Day Regular Holiday - Duty", "RDRH" },
                    { "Rest Day Regular Holiday - OT", "RDRHOT" },
                    { "Rest Day Regular Holiday - ND", "RDRHND" },
                    { "Rest Day Regular Holiday - OTND", "RDRHOTND" }
                };

                if (!dayTypeFieldMap.ContainsKey(dayType))
                    return Json(new { success = false, message = "Invalid day type" });

                string fieldName = dayTypeFieldMap[dayType];

                // Get the rate percentage (convert from field to double)
                var rateValue = Convert.ToDouble(((IDictionary<string, object>)salaryRates)[fieldName]);

                // Convert value based on unit (Hour or Minute)
                double hours = unit == "Minute" ? value / 60.0 : value;

                // Calculate: combinedDailyRate / 8 * ratePercentage/100 * hours
                double requestedAmount = (combinedDailyRate / 8.0) * (rateValue / 100.0) * hours;

                // Late, Undertime, Absent are deductions — return as negative
                bool isDeduction = dayType == "Regular Day - Late" || dayType == "Regular Day - Undertime" || dayType == "Regular Day - Absent";
                if (isDeduction) requestedAmount = -Math.Abs(requestedAmount);

                if (CanViewAllowance)
                {
                    return Json(new
                    {
                        success = true,
                        requestedAmount = Math.Round(requestedAmount, 2),
                        dailyRate,
                        allowanceDailyRate,
                        combinedDailyRate,
                        ratePercentage = rateValue,
                        showAllowance = true
                    });
                }

                // READWRITE: correct amount returned but allowance breakdown omitted
                return Json(new
                {
                    success = true,
                    requestedAmount = Math.Round(requestedAmount, 2),
                    dailyRate,
                    ratePercentage = rateValue,
                    showAllowance = false
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CalculateAdjustmentAmount: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Add new Adjustment Request
        [HttpPost]
        public JsonResult AddAdjustmentRequest(AdjustmentPayrollModel model)
        {
            if (!CanWrite)
                return Json(new { success = false, message = "Unauthorized: You do not have permission to add adjustment requests." });

            try
            {
                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (!RecordExists("s_adjustment", "adjustmentCode", model.adjustmentCode))
                    return Json(new { success = false, message = "Adjustment type not found!" });

                if (model.dateToAdjustment == null)
                    return Json(new { success = false, message = "Effectivity date is required!" });

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
                    model.employeeNo,
                    model.adjustmentCode,
                    model.amount,
                    model.dateToAdjustment,
                    reason = model.reason ?? "",
                    DayType = model.DayType ?? "",
                    Value = model.Value,
                    Units = model.Units ?? "",
                    addedByUser = EmployeeNo,
                    requestedByUser = EmployeeNo
                });

                _auditTrail.Log("c_payable", newId, "CREATED",
                    $"Added adjustment request for {model.employeeNo}: {model.adjustmentCode}, Amount: {model.amount:N2}");

                return Json(new { success = true, message = "Adjustment Request added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddAdjustmentRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error adding Adjustment Request: {ex.Message}" });
            }
        }

        // Update existing Adjustment Request (Pending and Declined only)
        [HttpPost]
        public JsonResult UpdateAdjustmentRequest(AdjustmentPayrollModel model)
        {
            if (!CanWrite)
                return Json(new { success = false, message = "Unauthorized: You do not have permission to update adjustment requests." });

            try
            {
                var currentStatus = _db.QueryFirstOrDefault<string>(
                    "SELECT statusName FROM c_payable WHERE id = @id AND isActive = 1",
                    new { model.id });

                if (string.IsNullOrEmpty(currentStatus))
                    return Json(new { success = false, message = "Adjustment Request not found!" });

                if (currentStatus != "Pending" && currentStatus != "Declined")
                    return Json(new { success = false, message = "Only pending or declined requests can be edited!" });

                if (!RecordExists("e_basicinfo", "employeeNo", model.employeeNo))
                    return Json(new { success = false, message = "Employee not found!" });

                if (!RecordExists("s_adjustment", "adjustmentCode", model.adjustmentCode))
                    return Json(new { success = false, message = "Adjustment type not found!" });

                var newStatus = currentStatus == "Declined" ? "Pending" : currentStatus;

                var sql = @"
                    UPDATE c_payable 
                    SET employeeNo = @employeeNo, adjustmentCode = @adjustmentCode,
                        amount = @amount, dateToAdjustment = @dateToAdjustment,
                        reason = @reason, statusName = @statusName,
                        DayType = @DayType, Value = @Value, Units = @Units,
                        dtLastModified = NOW(), lastModifiedByUser = @lastModifiedByUser
                    WHERE id = @id";

                _db.Execute(sql, new
                {
                    model.id,
                    model.employeeNo,
                    model.adjustmentCode,
                    model.amount,
                    model.dateToAdjustment,
                    reason = model.reason ?? "",
                    DayType = model.DayType ?? "",
                    Value = model.Value,
                    Units = model.Units ?? "",
                    statusName = newStatus,
                    lastModifiedByUser = EmployeeNo
                });

                _auditTrail.Log("c_payable", model.id, "UPDATED",
                    $"Updated adjustment request for {model.employeeNo}: {model.adjustmentCode}, Amount: {model.amount:N2}");

                var message = currentStatus == "Declined"
                    ? "Adjustment Request updated successfully and status set back to Pending!"
                    : "Adjustment Request updated successfully!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateAdjustmentRequest: {ex.Message}");
                return Json(new { success = false, message = $"Error updating Adjustment Request: {ex.Message}" });
            }
        }

        // Approve Adjustment Request
        [HttpPost]
        public JsonResult ApproveAdjustmentRequest(int id, string approvedByUser)
        {
            if (!CanApprove)
                return Json(new { success = false, message = "Unauthorized: Only users with Full Access can approve adjustment requests." });

            try
            {
                var record = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT statusName, amount FROM c_payable WHERE id = @id AND isActive = 1",
                    new { id });

                if (record == null)
                    return Json(new { success = false, message = "Adjustment Request not found!" });

                if (record.statusName != "Pending")
                    return Json(new { success = false, message = "Only pending requests can be approved!" });

                var sql = @"
                    UPDATE c_payable 
                    SET statusName = 'Approved', approvedAmount = amount,
                        dtStatus = NOW(), statusByUser = @approvedByUser,
                        dtLastModified = NOW(), lastModifiedByUser = @approvedByUser
                    WHERE id = @id";

                _db.Execute(sql, new { id, approvedByUser });

                _auditTrail.Log("c_payable", id, "APPROVED",
                    $"Approved adjustment request by {approvedByUser}");

                return Json(new { success = true, message = "Adjustment Request approved successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ApproveAdjustmentRequest: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Decline Adjustment Request
        [HttpPost]
        public JsonResult DeclineAdjustmentRequest(int id, string declinedByUser, string reason = "")
        {
            if (!CanApprove)
                return Json(new { success = false, message = "Unauthorized: Only users with Full Access can decline adjustment requests." });

            try
            {
                var currentStatus = _db.QueryFirstOrDefault<string>(
                    "SELECT statusName FROM c_payable WHERE id = @id AND isActive = 1",
                    new { id });

                if (string.IsNullOrEmpty(currentStatus))
                    return Json(new { success = false, message = "Adjustment Request not found!" });

                if (currentStatus == "Cancelled" || currentStatus == "Processed" || currentStatus == "Declined")
                    return Json(new { success = false, message = "This request cannot be declined!" });

                var sql = @"
                    UPDATE c_payable 
                    SET statusName = 'Declined', 
                        approvedAmount = NULL,
                        dtLastModified = NOW(), lastModifiedByUser = @declinedByUser
                    WHERE id = @id";

                _db.Execute(sql, new { id, declinedByUser });

                _auditTrail.Log("c_payable", id, "DECLINED",
                    $"Declined adjustment request by {declinedByUser}{(string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}")}");

                return Json(new { success = true, message = "Adjustment Request declined successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeclineAdjustmentRequest: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        private bool RecordExists(string table, string column, string value)
        {
            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value AND isActive = 1";
            return _db.QuerySingle<int>(sql, new { value }) > 0;
        }
    }
}