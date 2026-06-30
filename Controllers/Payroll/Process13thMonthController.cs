using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    [ModuleAuthorize("Tprocess13thMonthM")]
    public class Process13thMonthController : Controller
    {
        private readonly IDbConnection _db;

        public Process13thMonthController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Payroll/Process13thMonth.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string branch, string dateYear)
        {
            string query = @"
                SELECT *,
                    ROUND(SLVL * dailyRate, 2) AS totalSLVL,
                    CAST(
                        (
                            (basicPay + rataAmount - totalLate - totalUndertime - absentAmount) / 12
                            + ROUND(SLVL * dailyRate, 2)
                            + adjustment
                            - deduction
                        ) AS DECIMAL(10,2)
                    ) AS v13thMonth,
                    deduction AS v13thMonthDeduction
                FROM
                (
                    SELECT *,
                        (basicMonthlyPay + allowanceAmount) AS monthly,
                        CASE
                            WHEN payrollBasis = 'MONTHLY' THEN (basicMonthlyPay + allowanceAmount) / 26
                            ELSE dailyRate1
                        END AS dailyRate
                    FROM
                    (
                        SELECT
                            pbio.employeeNo,
                            CONCAT(
                                b.lastName, ', ',
                                b.firstName, ' ',
                                LEFT(IFNULL(b.middleName,''),1), '.'
                            ) AS fullName,
                            pbio.employmentStatus,
                            CASE
                                WHEN p.payrollBasis = 'D' THEN 'DAILY'
                                ELSE 'MONTHLY'
                            END AS payrollBasis,
                            b.dateHired,

                            (
                                SELECT COUNT(DISTINCT DATE_FORMAT(pb.dateTo, '%Y-%m'))
                                FROM p_biometrics pb
                                WHERE pb.employeeNo = b.employeeNo
                                  AND pb.isActive = 1
                                  AND pb.statusName = 'POSTED'
                                  AND pb.dateTo BETWEEN
                                        STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED) - 1, '-12-01'), '%Y-%m-%d')
                                    AND LAST_DAY(STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED), '-11-01'), '%Y-%m-%d'))
                            ) AS tenure,

                            pbio.branchCode,
                            br.branchName,

                            SUM(
                                IFNULL(CAST(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(50)) AS DECIMAL(18,2)), 0)
                                + IFNULL(pbio.workOnOffPresentAmount, 0)
                                + IFNULL(pbio.amountRestOT, 0)
                                + IFNULL(pbio.amountNSDRest, 0)
                                + IFNULL(pbio.legalPresentAmount, 0)
                                + IFNULL(pbio.specialPresentAmount, 0)
                                + IFNULL(pbio.reg_basic_al, 0)
                            ) AS basicPay,

                            SUM(IFNULL(pbio.totalAmountLate, 0)) AS totalLate,
                            SUM(IFNULL(pbio.totalAmountUndertime, 0)) AS totalUndertime,
                            SUM(IFNULL(pbio.absentAmount, 0)) AS absentAmount,
                            SUM(IFNULL(pbio.rataAmount, 0) - IFNULL(pbio.allowanceDeductionAbsent, 0) - IFNULL(pbio.allowanceDeductionLate, 0)) AS rataAmount,

                            IFNULL(CAST(CAST(AES_DECRYPT(p.basicMonthlyPay,'portalkeisan') AS CHAR(50)) AS DECIMAL(18,2)), 0) AS basicMonthlyPay,

                            CASE
                                WHEN pbio.employeeNo = 'C-049' AND CAST(@dtYear AS UNSIGNED) = 2025 THEN 550
                                ELSE IFNULL(CAST(CAST(AES_DECRYPT(pbio.dailyRate,'portalkeisan') AS CHAR(50)) AS DECIMAL(18,2)), 0)
                            END AS dailyRate1,

                            IFNULL(
                                (
                                    SELECT al.allowanceAmount
                                    FROM e_allowance al
                                    WHERE al.employeeNo = b.employeeNo
                                      AND al.isActive = 1
                                      AND al.allowanceCode = 'SALARY'
                                    ORDER BY al.effectivityDate DESC
                                    LIMIT 1
                                ),
                                0
                            ) AS allowanceAmount,

                            IFNULL(
                                (
                                    SELECT SUM(cp.approvedAmount)
                                    FROM c_payable cp
                                    WHERE cp.adjustmentCode = 'YEARENDBONUS'
                                      AND cp.statusName = 'Approved'
                                      AND cp.isActive = 1
                                      AND cp.employeeNo = b.employeeNo
                                      AND cp.dateToAdjustment BETWEEN
                                            STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED) - 1, '-12-01'), '%Y-%m-%d')
                                        AND LAST_DAY(STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED), '-11-01'), '%Y-%m-%d'))
                                ),
                                0
                            ) AS adjustment,

                            IFNULL(
                                (
                                    SELECT SUM(cp.amount)
                                    FROM c_receivable cp
                                    WHERE cp.otherdeductionCode = 'MONTH13THDEDUCTION'
                                      AND cp.statusName = 'Approved'
                                      AND cp.isActive = 1
                                      AND cp.employeeNo = b.employeeNo
                                      AND cp.dtAdded BETWEEN
                                            STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED) - 1, '-12-01'), '%Y-%m-%d')
                                        AND LAST_DAY(STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED), '-11-01'), '%Y-%m-%d'))
                                ),
                                0
                            ) AS deduction,

                            IFNULL(
                                (
                                    SELECT m.availableBalance
                                    FROM m_leave m
                                    WHERE m.employeeNo = b.employeeNo
                                      AND m.leaveCode = 'LC-000001'
                                    ORDER BY m.id DESC
                                    LIMIT 1
                                ),
                                0
                            )
                            +
                            IFNULL(
                                (
                                    SELECT m.availableBalance
                                    FROM m_leave m
                                    WHERE m.employeeNo = b.employeeNo
                                      AND m.leaveCode = 'LC-000002'
                                    ORDER BY m.id DESC
                                    LIMIT 1
                                ),
                                0
                            ) AS SLVL,

                            IFNULL(
                                (
                                    SELECT SUM(cp.amount)
                                    FROM c_receivable cp
                                    WHERE cp.otherdeductionCode = 'MONTH13THDEDUCTION'
                                      AND cp.statusName = 'Approved'
                                      AND cp.isActive = 1
                                      AND cp.employeeNo = b.employeeNo
                                      AND cp.dtAdded BETWEEN
                                            STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED) - 1, '-12-01'), '%Y-%m-%d')
                                        AND LAST_DAY(STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED), '-11-01'), '%Y-%m-%d'))
                                ),
                                0
                            ) AS longPay,

                            0 AS riceAllowanceAmount,
                            0 AS leaveAmount,

                            IFNULL(rq.id, 0) AS requestID

                        FROM p_biometrics pbio
                        LEFT JOIN e_basicinfo b
                            ON b.employeeNo = pbio.employeeNo
                        LEFT JOIN e_payrolldetails p
                            ON p.employeeNo = pbio.employeeNo
                        LEFT JOIN s_branch br
                            ON br.branchCode = pbio.branchCode
                        LEFT JOIN rq_13thmonth rq
                            ON rq.employeeNo = pbio.employeeNo
                           AND rq.dateYear = CAST(@dtYear AS UNSIGNED)

                        WHERE
                            (@brcode = '' OR @brcode = 'ALL' OR pbio.branchCode = @brcode)
                            AND pbio.isActive = 1
                            AND pbio.statusName = 'POSTED'
                            AND IFNULL(rq.id, 0) = 0
                            AND pbio.dateTo BETWEEN
                                    STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED) - 1, '-12-01'), '%Y-%m-%d')
                                AND LAST_DAY(STR_TO_DATE(CONCAT(CAST(@dtYear AS UNSIGNED), '-11-01'), '%Y-%m-%d'))

                        GROUP BY
                            pbio.employeeNo,
                            b.lastName,
                            b.firstName,
                            b.middleName,
                            pbio.employmentStatus,
                            p.payrollBasis,
                            b.dateHired,
                            pbio.branchCode,
                            br.branchName,
                            p.basicMonthlyPay,
                            pbio.dailyRate,
                            rq.id

                        ORDER BY MIN(pbio.id)
                    ) AS t1
                    WHERE t1.basicMonthlyPay <> 0
                ) AS t2;
                ";

            var p = new DynamicParameters();
            p.Add("@brcode", branch ?? "");
            p.Add("@dtYear", string.IsNullOrWhiteSpace(dateYear) ? "2026" : dateYear);

            var list = _db.Query<Month13Model>(query, p).ToList();

            return Json(new { data = list });
        }

    }
}