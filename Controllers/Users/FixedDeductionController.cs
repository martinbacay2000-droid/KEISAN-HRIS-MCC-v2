using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Models.Users;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    public class FixedDeductionController : Controller
    {
        private readonly IDbConnection _db;

        public FixedDeductionController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetFixedDeduction(string employeeNo)
        {
            var employee = _db.QueryFirstOrDefault<userFixedDeduction>(
            @"SELECT 
                e.id,
                e.employeeNo,
                s.fixedDeductionCode,
                s.fixedDeductionName,
                e.fixedDeductionAmount,
                e.fixedDeductionDateStart,
                e.isActive,

                IFNULL((
                    SELECT SUM(credit)
                    FROM m_fixeddeduction
                    WHERE isActive = 1
                      AND employeeNo = @employeeNo
                      AND fixedDeductionCode = e.fixedDeductionCode
                      AND e_fixedDeductionID = e.id
                ), 0) AS totalAmountDeducted,

                IFNULL((
                    SELECT SUM(debit)
                    FROM m_fixeddeduction
                    WHERE isActive = 1
                      AND employeeNo = @employeeNo
                      AND fixedDeductionCode = e.fixedDeductionCode
                      AND e_fixedDeductionID = e.id
                ), 0) AS caAmount

              FROM e_fixeddeduction e
              JOIN s_fixeddeduction s 
                ON e.fixedDeductionCode = s.fixedDeductionCode
              WHERE e.employeeNo = @employeeNo",
            new { employeeNo });

            employee ??= new userFixedDeduction
            {
                employeeNo = employeeNo
            };

            return PartialView("~/Views/Users/Partials/_FixedDeduction.cshtml", employee);
        }

        [HttpGet]
        public JsonResult GetFixedDeductionList(string employeeNo, string loanStatus)
        {
            // Use Query instead of QueryFirstOrDefault to get a list of records
            var employeeDeductions = _db.Query<userFixedDeduction>(
                @"SELECT e.id,
                 e.employeeNo,
                 s.fixedDeductionCode,
                 s.fixedDeductionName,
                 e.fixedDeductionAmount,
                 DATE_FORMAT(e.fixedDeductionDateStart, '%Y/%m/%d') AS fixedDeductionDateStart,
                 IFNULL(m.totalCredit, 0) AS totalPaidBalance,
                 IFNULL(e.fixedDeductionAmount, 0) - IFNULL(m.totalCredit, 0) AS remainingBalance,
                 e.deductionSchedule, 
                 e.isActive,
                 e.remarks
          FROM e_fixeddeduction e
          JOIN s_fixeddeduction s ON e.fixedDeductionCode = s.fixedDeductionCode
          LEFT JOIN (
              SELECT 
                  e_fixedDeductionID,
                  SUM(credit) AS totalCredit,
                  SUM(debit) AS totalDebit
              FROM m_fixeddeduction
              WHERE isActive = 1
              GROUP BY e_fixedDeductionID
          ) m ON m.e_fixedDeductionID = e.id
          WHERE e.employeeNo = @employeeNo",
                new { employeeNo }).ToList();

            return Json(new { data = employeeDeductions });
        }

        [HttpGet]
        public JsonResult GetLoanPayments(string id)
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

            // Updated to use UsersLoansModel instead of userLoans
            var loansList = _db.Query<UsersLoansModel>(sql, new { id }).ToList();
            return Json(new { data = loansList });
        }

        [HttpPost]
        public JsonResult NewDeduction(userFixedDeduction model)
        {
            if (_db.State != System.Data.ConnectionState.Open)
                _db.Open();
            using (var tran = _db.BeginTransaction())
            {
                try
                {
                    // 1️ Insert into e_fixeddeduction
                    string sql = @"INSERT INTO e_fixeddeduction
                                (
                                    employeeNo,
                                    fixedDeductionCode,
                                    fixedDeductionAmount,
                                    fixedDeductionDateStart,
                                    isActive,
                                    dtAdded,
                                    addedByUser,
                                    deductionSchedule,
                                    remarks
                                )
                                VALUES
                                (
                                    @employeeNo,
                                    @fixedDeductionCode,
                                    @fixedDeductionAmount,
                                    @fixedDeductionDateStart,
                                    @isActive,
                                    @dtAdded,
                                    @addedByUser,
                                    @deductionSchedule,
                                    @remarks
                                );
                                ";

                    // Execute insert
                    _db.Execute(sql, new
                    {
                        employeeNo = model.employeeNo,
                        fixedDeductionCode = model.fixedDeductionCode,
                        fixedDeductionAmount = model.fixedDeductionAmount,
                        fixedDeductionDateStart = model.fixedDeductionDateStart,
                        deductionSchedule = model.deductionSchedule,
                        remarks = model.remarks,
                        isActive = 1,
                        dtAdded = DateTime.Now,
                        addedByUser = User.Identity?.Name ?? "System"
                    }, transaction: tran);

                    // 2️ Get the last inserted ID
                    int eFixedDeductionID = _db.QuerySingle<int>("SELECT LAST_INSERT_ID();", transaction: tran);

                    // 3️ Insert into m_fixeddeduction
                    string monitoringSql = @"INSERT INTO m_fixeddeduction
                                            (
                                                employeeNo,
                                                fixedDeductionCode,
                                                fixedDeductionAmountDeducted,
                                                fixedDeductionDateDeducted,
                                                statusName,
                                                dtStatus,
                                                statusByUser,
                                                isActive,
                                                dtAdded,
                                                addedByUser,
                                                e_fixedDeductionID,
                                                debit,
                                                details
                                            )
                                            VALUES
                                            (
                                                @employeeNo,
                                                @fixedDeductionCode,
                                                @fixedDeductionAmountDeducted,
                                                @fixedDeductionDateDeducted,
                                                @statusName,
                                                @dtStatus,
                                                @statusByUser,
                                                @isActive,
                                                @dtAdded,
                                                @addedByUser,
                                                @e_fixedDeductionID,
                                                @debit,
                                                @details
                                            );
                                            ";

                    _db.Execute(monitoringSql, new
                    {
                        employeeNo = model.employeeNo,
                        fixedDeductionCode = model.fixedDeductionCode,
                        fixedDeductionAmountDeducted = model.fixedDeductionAmount,
                        fixedDeductionDateDeducted = model.fixedDeductionDateStart,
                        statusName = "Added",
                        dtStatus = DateTime.Now,
                        statusByUser = User.Identity?.Name ?? "System",
                        isActive = 1,
                        dtAdded = DateTime.Now,
                        addedByUser = User.Identity?.Name ?? "System",
                        e_fixedDeductionID = eFixedDeductionID,
                        debit = model.fixedDeductionAmount,
                        details = "Added"
                    }, transaction: tran);
                    // Commit transaction
                    tran.Commit();

                    return Json(new { success = true, message = "New deduction added successfully!" });
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return Json(new { success = false, message = $"Error updating record: {ex.Message}" });
                }
            }
        }

        [HttpPost]
        public JsonResult UpdateDeduction(userFixedDeduction model)
        {
            if (_db.State != System.Data.ConnectionState.Open)
                _db.Open();

            using (var tran = _db.BeginTransaction())
            {
                try
                {
                    // 1️ Update e_fixeddeduction
                    string updateSql = @"
                        UPDATE e_fixeddeduction
                        SET
                            fixedDeductionCode = @fixedDeductionCode,
                            fixedDeductionAmount = @fixedDeductionAmount,
                            fixedDeductionDateStart = @fixedDeductionDateStart,
                            deductionSchedule = @deductionSchedule,
                            remarks = @remarks,
                            dtLastModified = @dtLastModified,
                            lastModifiedByUser = @lastModifiedByUser
                        WHERE id = @id and isActive = 1;
                    ";

                    _db.Execute(updateSql, new
                    {
                        fixedDeductionCode = model.fixedDeductionCode,
                        fixedDeductionAmount = model.fixedDeductionAmount,
                        fixedDeductionDateStart = model.fixedDeductionDateStart,
                        deductionSchedule = model.deductionSchedule,
                        remarks = model.remarks,
                        dtLastModified = DateTime.Now,
                        lastModifiedByUser = User.Identity?.Name ?? "System",
                        id = model.id // The e_fixeddeduction ID to update
                    }, transaction: tran);

                    // 2️ Update corresponding m_fixeddeduction record(s)
                    string updateMonitoringSql = @"
                        UPDATE m_fixeddeduction
                        SET
                            fixedDeductionAmountDeducted = @fixedDeductionAmountDeducted,
                            dtStatus = @dtStatus,
                            debit = @debit
                        WHERE e_fixedDeductionID = @e_fixedDeductionID and debit is not null and isActive = 1;
                    ";

                    _db.Execute(updateMonitoringSql, new
                    {
                        fixedDeductionAmountDeducted = model.fixedDeductionAmount,
                        fixedDeductionDateDeducted = model.fixedDeductionDateStart,
                        dtStatus = DateTime.Now,
                        debit = model.fixedDeductionAmount,
                        details = "Updated",
                        e_fixedDeductionID = model.id
                    }, transaction: tran);

                    // ✅ Commit transaction
                    tran.Commit();

                    return Json(new { success = true, message = "Deduction updated successfully!" });
                }
                catch (Exception ex)
                {
                    tran.Rollback();
                    return Json(new { success = false, message = $"Error updating record: {ex.Message}" });
                }
            }
        }
    }
}