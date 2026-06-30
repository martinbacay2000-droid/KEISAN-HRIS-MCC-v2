using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Globalization;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSLoansM")]
    public class LoansController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public LoansController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View("~/Views/Users/Partials/_Loans.cshtml");
        }

        public IActionResult GetLoans(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_Loans.cshtml", new List<UsersLoansModel>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_Loans.cshtml");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLoans: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_Loans.cshtml", new List<UsersLoansModel>());
            }
        }

        [HttpGet]
        public JsonResult GetLoanTypes()
        {
            try
            {
                var sql = @"
                    SELECT id, loanCode, loanName, isActive
                    FROM s_loan 
                    WHERE isActive = 1 
                    AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                    ORDER BY loanName";

                var loanTypes = _db.Query<dynamic>(sql).ToList();
                return Json(loanTypes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLoanTypes: {ex.Message}");
                return Json(new List<dynamic>());
            }
        }

        [HttpGet]
        public JsonResult GetLoansList(string employeeNo, string loanStatus)
        {
            try
            {
                string sql = @"
                    SELECT * FROM 
                    (
                        SELECT 							
                            tbl1.totalLoanAmount - tbl1.loanPayments AS outstandingBalance,
                            CASE WHEN (tbl1.totalLoanAmount - tbl1.loanPayments) <= 0 OR tbl1.statusName='Completed'
                                  THEN 'Completed' ELSE 'Ongoing' END AS loanStatus,
                            tbl1.*
                        
                        FROM
                        (	
                              SELECT r.employeeNo, 
                              r.id,
                              IFNULL(r.isActive,0) AS loanIsActive,
                              r.loanCode,
                              sa.loanName,
                              r.deductionSchedule,
                              CAST(IFNULL(r.principalAmount,0) AS DECIMAL(10,2)) AS principalAmount,
                              CAST(IFNULL(r.interestAmount,0) AS DECIMAL(10,2)) AS interestAmount,
                              CAST(IFNULL(r.totalLoanAmount,0) AS DECIMAL(10,2)) AS totalLoanAmount,
                              CAST(IFNULL(r.amortizationAmount,0) AS DECIMAL(10,2)) AS deductionPerCutoff,                          
                              r.monthsToPay,                       
                                 
                              CAST(IFNULL((SELECT SUM(credit) FROM m_loan m WHERE m.e_loanID = r.id AND m.isActive = 1)
                                            ,0) AS DECIMAL(10,2)) AS loanPayments,
                      
                              DATE_FORMAT(r.dateGranted,'%m/%d/%Y') AS dateGranted, 
                              DATE_FORMAT(r.deductionStartDate,'%m/%d/%Y') AS deductionStartDate, 
                              DATE_FORMAT(r.dtAdded,'%m/%d/%Y') AS dtAdded, 
                              CONCAT(s.lastName, ', ', s.firstName) AS addedByUser,                          
                              IFNULL(r.statusName,'Ongoing') statusName,                          
                              DATE_FORMAT(r.dtStatus,'%m/%d/%Y') AS dtStatus, 
                              CONCAT(ss.lastName, ', ', ss.firstName) AS statusByUser,
                              r.remarks

                              FROM e_loan r
                              LEFT JOIN s_user s ON s.userCode = r.addedByUser
                              LEFT JOIN s_user ss ON ss.userCode = r.statusByUser
                              LEFT JOIN s_loan sa on sa.loanCode = r.loanCode AND sa.isActive = 1

                              WHERE r.employeeNo = @employeeNo
                         ) tbl1
                         ) tbl2 
                         WHERE 
                            CASE WHEN @loanStatus = '1' THEN loanStatus = 'Ongoing' AND loanIsActive = 1
                                 WHEN @loanStatus = '0' THEN loanIsActive = 0
                                 WHEN @loanStatus = '3' THEN loanStatus = 'Completed' AND loanIsActive = 1
                                 ELSE loanIsActive IN(1,0)
                            END
                         ORDER BY id DESC";

                var loansList = _db.Query<UsersLoansModel>(sql, new { employeeNo, loanStatus }).ToList();
                return Json(new { data = loansList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLoansList: {ex.Message}");
                return Json(new { data = new List<UsersLoansModel>() });
            }
        }

        [HttpGet]
        public JsonResult GetLoansListInactive(string employeeNo)
        {
            try
            {
                string sql = @"
                    SELECT * FROM 
                    (
                        SELECT 							
                            tbl1.totalLoanAmount - tbl1.loanPayments AS outstandingBalance,
                            CASE WHEN (tbl1.totalLoanAmount - tbl1.loanPayments) <= 0 OR tbl1.statusName='Completed'
                                  THEN 'Completed' ELSE 'Ongoing' END AS loanStatus,
                            tbl1.*
                        
                        FROM
                        (	
                              SELECT r.employeeNo, 
                              r.id,
                              IFNULL(r.isActive,0) AS loanIsActive,
                              r.loanCode,
                              sa.loanName,
                              r.deductionSchedule,
                              CAST(IFNULL(r.principalAmount,0) AS DECIMAL(10,2)) AS principalAmount,
                              CAST(IFNULL(r.interestAmount,0) AS DECIMAL(10,2)) AS interestAmount,
                              CAST(IFNULL(r.totalLoanAmount,0) AS DECIMAL(10,2)) AS totalLoanAmount,
                              CAST(IFNULL(r.amortizationAmount,0) AS DECIMAL(10,2)) AS deductionPerCutoff,                          
                              r.monthsToPay,                       
                                 
                              CAST(IFNULL((SELECT SUM(credit) FROM m_loan m WHERE m.e_loanID = r.id AND m.isActive = 1)
                                            ,0) AS DECIMAL(10,2)) AS loanPayments,
                      
                              DATE_FORMAT(r.dateGranted,'%m/%d/%Y') AS dateGranted, 
                              DATE_FORMAT(r.deductionStartDate,'%m/%d/%Y') AS deductionStartDate, 
                              DATE_FORMAT(r.dtAdded,'%m/%d/%Y') AS dtAdded, 
                              CONCAT(s.lastName, ', ', s.firstName) AS addedByUser,                          
                              IFNULL(r.statusName,'Ongoing') statusName,                          
                              DATE_FORMAT(r.dtStatus,'%m/%d/%Y') AS dtStatus, 
                              CONCAT(ss.lastName, ', ', ss.firstName) AS statusByUser,
                              r.remarks

                              FROM e_loan r
                              LEFT JOIN s_user s ON s.userCode = r.addedByUser
                              LEFT JOIN s_user ss ON ss.userCode = r.statusByUser
                              LEFT JOIN s_loan sa on sa.loanCode = r.loanCode AND sa.isActive = 1

                              WHERE r.employeeNo = @employeeNo
                              AND r.isActive = 0
                         ) tbl1
                         ) tbl2 
                         ORDER BY id DESC";

                var loansList = _db.Query<UsersLoansModel>(sql, new { employeeNo }).ToList();
                return Json(new { data = loansList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLoansListInactive: {ex.Message}");
                return Json(new { data = new List<UsersLoansModel>() });
            }
        }

        [HttpGet]
        public JsonResult GetLoanPayments(int id)
        {
            try
            {
                string sql = @"
                    SELECT 
                        m.id,
                        m.e_loanID,
                        CAST(m.credit AS DECIMAL(10,2)) AS loanPayments,
                        DATE_FORMAT(m.dtAdded,'%m/%d/%Y') AS dtAdded,
                        m.details AS remarks,
                        CONCAT(s.lastName, ', ', s.firstName) AS addedByUser
                    FROM m_loan m
                    LEFT JOIN s_user s ON s.userCode = m.addedByUser
                    WHERE m.credit > 0
                    AND m.e_loanID = @id
                    ORDER BY m.dtAdded DESC";

                var paymentsList = _db.Query<dynamic>(sql, new { id }).ToList();
                return Json(new { data = paymentsList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLoanPayments: {ex.Message}");
                return Json(new { data = new List<dynamic>() });
            }
        }

        [HttpGet]
        public JsonResult GetLoanById(int id)
        {
            try
            {
                var sql = @"
                    SELECT 
                        r.id,
                        r.employeeNo,
                        r.loanCode,
                        sa.loanName,
                        DATE_FORMAT(r.dateGranted,'%Y/%m/%d') AS dateGranted,
                        CAST(IFNULL(r.principalAmount,0) AS DECIMAL(10,2)) AS principalAmount,
                        CAST(IFNULL(r.interestAmount,0) AS DECIMAL(10,2)) AS interestAmount,
                        CAST(IFNULL(r.totalLoanAmount,0) AS DECIMAL(10,2)) AS totalLoanAmount,
                        CAST(IFNULL(r.amortizationAmount,0) AS DECIMAL(10,2)) AS deductionPerCutoff,
                        r.monthsToPay,
                        DATE_FORMAT(r.deductionStartDate,'%Y/%m/%d') AS deductionStartDate,
                        r.deductionSchedule,
                        r.remarks,
                        r.isActive
                    FROM e_loan r
                    LEFT JOIN s_loan sa ON sa.loanCode = r.loanCode AND sa.isActive = 1
                    WHERE r.id = @Id";

                var loan = _db.QueryFirstOrDefault<UsersLoansModel>(sql, new { Id = id });

                return loan != null
                    ? Json(new { success = true, data = loan })
                    : Json(new { success = false, message = "Loan not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLoanById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving loan: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult SaveLoan([FromBody] UsersLoansDto model)
        {
            try
            {
                if (!ValidateLoan(model, out string validationMessage))
                {
                    return Json(new { success = false, message = validationMessage });
                }

                if (model.Id.HasValue && model.Id > 0)
                {
                    return UpdateLoan(model);
                }
                else
                {
                    return InsertLoan(model);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveLoan: {ex.Message}");
                return Json(new { success = false, message = "Error saving loan: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult CompleteLoan([FromBody] LoanStatusUpdateDto model)
        {
            try
            {
                if (model.Id <= 0)
                {
                    return Json(new { success = false, message = "Invalid loan ID." });
                }

                if (string.IsNullOrWhiteSpace(model.Remarks))
                {
                    return Json(new { success = false, message = "Remarks are required." });
                }

                var existingLoan = _db.QueryFirstOrDefault<UsersLoansModel>(
                    "SELECT * FROM e_loan WHERE id = @Id AND isActive = 1",
                    new { Id = model.Id });

                if (existingLoan == null)
                {
                    return Json(new { success = false, message = "Loan not found or already completed/inactive." });
                }

                string sql = @"
                    UPDATE e_loan 
                    SET statusName = 'Completed', 
                        remarks = @Remarks, 
                        dtStatus = NOW(), 
                        statusByUser = @StatusByUser,
                        dtLastModified = NOW(), 
                        lastModifiedByUser = @LastModifiedByUser 
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new
                {
                    Id = model.Id,
                    Remarks = model.Remarks,
                    StatusByUser = EmployeeNo,
                    LastModifiedByUser = EmployeeNo
                });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_loan", model.Id, "COMPLETED",
                        $"Loan completed. Remarks: {model.Remarks}");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Loan completed successfully!" })
                    : Json(new { success = false, message = "Failed to complete loan." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CompleteLoan: {ex.Message}");
                return Json(new { success = false, message = "Error completing loan: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult InactivateLoan([FromBody] LoanStatusUpdateDto model)
        {
            try
            {
                if (model.Id <= 0)
                {
                    return Json(new { success = false, message = "Invalid loan ID." });
                }

                if (string.IsNullOrWhiteSpace(model.Remarks))
                {
                    return Json(new { success = false, message = "Remarks are required." });
                }

                var existingLoan = _db.QueryFirstOrDefault<UsersLoansModel>(
                    "SELECT * FROM e_loan WHERE id = @Id AND isActive = 1",
                    new { Id = model.Id });

                if (existingLoan == null)
                {
                    return Json(new { success = false, message = "Loan not found or already inactive." });
                }

                string sql = @"
                    UPDATE e_loan 
                    SET statusName = 'Inactive', 
                        remarks = @Remarks,
                        isActive = 0,
                        dtStatus = NOW(), 
                        statusByUser = @StatusByUser,
                        dtLastModified = NOW(), 
                        lastModifiedByUser = @LastModifiedByUser 
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new
                {
                    Id = model.Id,
                    Remarks = model.Remarks,
                    StatusByUser = EmployeeNo,
                    LastModifiedByUser = EmployeeNo
                });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_loan", model.Id, "INACTIVATED",
                        $"Loan inactivated. Remarks: {model.Remarks}");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Loan inactivated successfully!" })
                    : Json(new { success = false, message = "Failed to inactivate loan." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactivateLoan: {ex.Message}");
                return Json(new { success = false, message = "Error inactivating loan: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreLoan(int id)
        {
            try
            {
                var existingLoan = _db.QueryFirstOrDefault<UsersLoansModel>(
                    "SELECT * FROM e_loan WHERE id = @Id AND isActive = 0",
                    new { Id = id });

                if (existingLoan == null)
                {
                    return Json(new { success = false, message = "Loan not found or already active." });
                }

                var sql = @"
                    UPDATE e_loan 
                    SET isActive = 1, 
                        statusName = 'Ongoing',
                        dtLastModified = NOW(),
                        lastModifiedByUser = @LastModifiedByUser
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new
                {
                    Id = id,
                    LastModifiedByUser = EmployeeNo
                });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_loan", id, "RESTORED", "Loan restored to active status");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Loan restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore loan." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreLoan: {ex.Message}");
                return Json(new { success = false, message = "Error restoring loan: " + ex.Message });
            }
        }

        // HELPER METHODS

        private bool ValidateLoan(UsersLoansDto model, out string message)
        {
            message = string.Empty;

            if (model == null || string.IsNullOrEmpty(model.EmployeeNo) || string.IsNullOrEmpty(model.LoanCode))
            {
                message = "Invalid data provided.";
                return false;
            }

            if (model.PrincipalAmount <= 0)
            {
                message = "Principal amount must be greater than 0.";
                return false;
            }

            if (model.MonthsToPay <= 0)
            {
                message = "Months to pay must be greater than 0.";
                return false;
            }

            if (model.DeductionPerCutoff <= 0)
            {
                message = "Deduction per cutoff must be greater than 0.";
                return false;
            }

            if (string.IsNullOrEmpty(model.DateGranted))
            {
                message = "Date granted is required.";
                return false;
            }

            if (string.IsNullOrEmpty(model.DeductionStartDate))
            {
                message = "Deduction start date is required.";
                return false;
            }

            if (string.IsNullOrEmpty(model.DeductionSchedule))
            {
                message = "Deduction schedule is required.";
                return false;
            }

            // Validate date formats
            if (!DateTime.TryParseExact(model.DateGranted, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime dateGranted))
            {
                message = "Invalid date granted format.";
                return false;
            }

            if (!DateTime.TryParseExact(model.DeductionStartDate, "yyyy/MM/dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime deductionStartDate))
            {
                message = "Invalid deduction start date format.";
                return false;
            }

            // Validate that deduction start date is not before date granted
            if (deductionStartDate < dateGranted)
            {
                message = "Deduction start date cannot be earlier than date granted.";
                return false;
            }

            return true;
        }

        private JsonResult InsertLoan(UsersLoansDto model)
        {
            var sql = @"
                INSERT INTO e_loan (
                    employeeNo,
                    loanCode,
                    dateGranted,
                    principalAmount,
                    interestAmount,
                    totalLoanAmount,
                    monthsToPay,
                    deductionStartDate,
                    amortizationAmount,
                    deductionSchedule,
                    remarks,
                    statusName,
                    dtAdded,
                    addedByUser,
                    isActive
                )
                VALUES (
                    @EmployeeNo,
                    @LoanCode,
                    @DateGranted,
                    @PrincipalAmount,
                    @InterestAmount,
                    @TotalLoanAmount,
                    @MonthsToPay,
                    @DeductionStartDate,
                    @DeductionPerCutoff,
                    @DeductionSchedule,
                    @Remarks,
                    'Ongoing',
                    NOW(),
                    @AddedByUser,
                    1
                );
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                LoanCode = model.LoanCode,
                DateGranted = model.DateGranted,
                PrincipalAmount = model.PrincipalAmount,
                InterestAmount = model.InterestAmount,
                TotalLoanAmount = model.TotalLoanAmount,
                MonthsToPay = model.MonthsToPay,
                DeductionStartDate = model.DeductionStartDate,
                DeductionPerCutoff = model.DeductionPerCutoff,
                DeductionSchedule = model.DeductionSchedule,
                Remarks = model.Remarks,
                AddedByUser = EmployeeNo
            });

            if (newId > 0)
            {
                _auditTrail.Log("e_loan", newId, "CREATED",
                    $"New loan added: {model.LoanCode} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Loan added successfully!" });
            }

            return Json(new { success = false, message = "Failed to add loan." });
        }

        private JsonResult UpdateLoan(UsersLoansDto model)
        {
            var existingRecord = _db.QueryFirstOrDefault<UsersLoansModel>(
                "SELECT * FROM e_loan WHERE id = @Id AND isActive = 1",
                new { Id = model.Id });

            if (existingRecord == null)
            {
                return Json(new { success = false, message = "Loan not found or is inactive!" });
            }

            var sql = @"
                UPDATE e_loan 
                SET loanCode = @LoanCode,
                    dateGranted = @DateGranted,
                    principalAmount = @PrincipalAmount,
                    interestAmount = @InterestAmount,
                    totalLoanAmount = @TotalLoanAmount,
                    monthsToPay = @MonthsToPay,
                    deductionStartDate = @DeductionStartDate,
                    amortizationAmount = @DeductionPerCutoff,
                    deductionSchedule = @DeductionSchedule,
                    remarks = @Remarks,
                    dtLastModified = NOW(),
                    lastModifiedByUser = @LastModifiedByUser
                WHERE id = @Id";

            var rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                LoanCode = model.LoanCode,
                DateGranted = model.DateGranted,
                PrincipalAmount = model.PrincipalAmount,
                InterestAmount = model.InterestAmount,
                TotalLoanAmount = model.TotalLoanAmount,
                MonthsToPay = model.MonthsToPay,
                DeductionStartDate = model.DeductionStartDate,
                DeductionPerCutoff = model.DeductionPerCutoff,
                DeductionSchedule = model.DeductionSchedule,
                Remarks = model.Remarks,
                LastModifiedByUser = EmployeeNo
            });

            if (rowsAffected > 0)
            {
                _auditTrail.Log("e_loan", model.Id.Value, "UPDATED",
                    $"Loan updated: {model.LoanCode} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Loan updated successfully!" });
            }

            return Json(new { success = false, message = "Failed to update loan." });
        }
    }
}