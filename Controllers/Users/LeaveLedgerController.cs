using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSleaveLedgerM")]
    public class LeaveLedgerController : Controller
    {
        private readonly IDbConnection _db;

        public LeaveLedgerController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetLeaveLedger(string employeeNo)
        {
            var employee = _db.QueryFirstOrDefault<userLeaveLedger>(
                "SELECT e.* " +
                "FROM e_loan e " +
                " WHERE e.employeeNo = @employeeNo", new { employeeNo });

            // If no record, create an empty model
            employee ??= new userLeaveLedger();
            employee.employeeNo = employeeNo;

            return PartialView("~/Views/Users/Partials/_LeaveLedger.cshtml", employee);
        }

        [HttpGet]
        public JsonResult GetLeaveLedgerList(string employeeNo, string status = "active")
        {
            string sql = @"SELECT 
                    m.id,
                    m.employeeNo,
                    m.leaveCode,
                    s.leaveName,
                    m.statusName,
                    m.beginningBalance,
                    m.accrual,
                    m.usedCredits,
                    m.availableBalance, 
                    m.rq_leaveID AS referenceID,
                    m.dtDeleted,
                    DATE_FORMAT(m.dtAdded,'%m/%d/%Y') AS dtAdded
                FROM m_leave m
                LEFT JOIN s_leave s ON s.leaveCode = m.leaveCode
                WHERE m.employeeNo = @employeeNo";

            sql += status == "active" ? " AND m.isActive = 1" : " AND m.isActive = 0";
            sql += " ORDER BY leaveName, m.dtAdded ASC, id ASC";

            var leaveLedger = _db.Query<userLeaveLedger>(sql, new { employeeNo }).ToList();
            return Json(new { data = leaveLedger });
        }

        [HttpPost]
        public JsonResult UpdateAllowanceDetails(userLeaveLedger model)
        {
            try
            {
                // Check if the record exists and is active
                string checkSql = @"SELECT COUNT(*) FROM e_payrolldetails 
                                   WHERE employeeNo = @employeeNo AND isActive = 1 ";
                int recordExists = _db.QuerySingle<int>(checkSql, new { model.employeeNo });
                string sql = "";

                if (recordExists == 0)
                {
                    //Inserts new record
                    sql = @"
                    INSERT INTO e_payrolldetails (
                        employeeNo,
                        isActive,
                        isMinimumWageEarner,
                        fixedNetPay,
                        meritServicePay,
                        basicSalary,
                        basicMonthlyPay,
                        dailyRate,
                        hourlyRate,
                        effectivityDate,
                        payrollBasis,
                        payrollType,
                        mp2,
                        contriPIFadditional,
                        tinNo,
                        sssNo,
                        philhealthNo,
                        hdmfNo,
                        bankType,
                        bankCode,
                        accountNo,
                        isNoLate,
                        isNoOTPremium,
                        payrollGroup,
                        dtAdded,
                        addedByUser
                    )
                    VALUES (
                        @employeeNo,
                        @isActive,
                        @isMinimumWageEarner,
                        @fixedNetPay,
                        @meritServicePay,
                        @basicSalary,
                        @basicMonthlyPay,
                        @dailyRate,
                        @hourlyRate,
                        @effectivityDate,
                        @payrollBasis,
                        @payrollType,
                        @mp2,
                        @contriPIFadditional,
                        @tinNo,
                        @sssNo,
                        @philhealthNo,
                        @hdmfNo,
                        @bankType,
                        @bankCode,
                        @accountNo,
                        @isNoLate,
                        @isNoOTPremium,
                        @payrollGroup,
                        @dtAdded,
                        @addedByUser
                    )";
                }
                else
                {
                    //update existing
                    sql = @"
                    UPDATE e_payrolldetails
                    SET
                        isMinimumWageEarner = @isMinimumWageEarner,
                        fixedNetPay         = @fixedNetPay,
                        meritServicePay     = @meritServicePay,
                        basicSalary         = @basicSalary,
                        basicMonthlyPay     = @basicMonthlyPay,
                        dailyRate           = @dailyRate,
                        hourlyRate          = @hourlyRate,
                        effectivityDate     = @effectivityDate,
                        payrollBasis        = @payrollBasis,
                        payrollType         = @payrollType,
                        mp2                 = @mp2,
                        contriPIFadditional = @contriPIFadditional,
                        tinNo               = @tinNo,
                        sssNo               = @sssNo,
                        philhealthNo        = @philhealthNo,
                        hdmfNo              = @hdmfNo,
                        bankType            = @bankType,
                        bankCode            = @bankCode,
                        accountNo           = @accountNo,
                        isNoLate            = @isNoLate,
                        isNoOTPremium       = @isNoOTPremium,
                        payrollGroup        = @payrollGroup,
                        dtLastModified      = @dtAdded,
                        lastModifiedByUser  = @addedByUser
                    WHERE
                        employeeNo = @employeeNo";
                }

                _db.Execute(sql, new
                {
                    employeeNo = model.employeeNo,
                    isActive = 1,
                    dtAdded = DateTime.Now,
                    addedByUser = User.Identity?.Name ?? "System"
                });

                return Json(new { success = true, message = "Payroll details updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error updating record: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult SoftDeleteLeave(int id, string reason = "")
        {
            try
            {
                // Check if it's a CTO leave type
                var checkSql = @"SELECT leaveCode FROM m_leave WHERE id = @Id AND isActive = 1";
                var leaveCode = _db.QueryFirstOrDefault<string>(checkSql, new { Id = id });

                if (string.IsNullOrEmpty(leaveCode))
                {
                    return Json(new { success = false, message = "Leave record not found or already inactive." });
                }

                if (leaveCode != "CTO")
                {
                    return Json(new { success = false, message = "Only CTO leave records can be deleted." });
                }

                var sql = @"
                    UPDATE m_leave 
                    SET isActive = 0, dtLastModified = NOW(), lastModifiedByUser = @User
                    WHERE id = @Id AND isActive = 1";

                int rowsAffected = _db.Execute(sql, new
                {
                    Id = id,
                    User = User.Identity?.Name ?? "System"
                });

                return rowsAffected > 0
                    ? Json(new { success = true, message = "CTO leave record deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete leave record." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error deleting record: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult RestoreLeave(int id)
        {
            try
            {
                var sql = @"
                    UPDATE m_leave 
                    SET isActive = 1, dtLastModified = NOW(), lastModifiedByUser = @User
                    WHERE id = @Id AND isActive = 0";

                int rowsAffected = _db.Execute(sql, new
                {
                    Id = id,
                    User = User.Identity?.Name ?? "System"
                });

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Leave record restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore leave record or record not found." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error restoring record: {ex.Message}" });
            }
        }
    }
}