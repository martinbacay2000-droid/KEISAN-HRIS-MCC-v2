using Dapper;
using KEISAN_HRIS_v2.Models.Payroll;
using KEISAN_HRIS_v2.Services.Payroll;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class ThirteenthMonthPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly ThirteenthMonthPdfService _pdfService;

        public ThirteenthMonthPdfController(IDbConnection db, ThirteenthMonthPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        [HttpGet]
        public IActionResult PreviewPdf(string employeeNo, string? dateYear = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeNo))
                    return BadRequest("Missing required parameter: employeeNo");

                // null  → resigned path: SQL derives year from dateOfEmpTermInitial
                // value → list-page path: SQL uses the supplied year directly
                int? resolvedYear = null;
                if (!string.IsNullOrWhiteSpace(dateYear) && int.TryParse(dateYear, out int parsedYear))
                    resolvedYear = parsedYear;

                var header = GetHeader(employeeNo, resolvedYear);
                if (header == null)
                    return NotFound("Employee record not found.");

                var lines = GetLines(employeeNo, resolvedYear);
                header.TotalAmount = lines.Sum(x => x.ThirteenthMonthPay);

                var pdfBytes = _pdfService.GenerateThirteenthMonthPdf(header, lines);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating 13th Month PDF: {ex.Message}");
                return StatusCode(500, "Error generating PDF");
            }
        }

        private ThirteenthMonthPdfData? GetHeader(string employeeNo, int? resolvedYear)
        {
            const string sql = @"
                SELECT
                    b.employeeNo                                        AS EmployeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ',
                           LEFT(IFNULL(b.middleName,''), 1), '.')       AS FullName,
                    IFNULL(sp.positionName,  'N/A')                     AS PositionName,
                    IFNULL(dep.departmentName, 'N/A')                   AS Department,
                    DATE_FORMAT(b.dateHired,            '%m/%d/%Y')     AS DateHired,
                    DATE_FORMAT(b.dateOfEmpTermInitial, '%m/%d/%Y')     AS DateResigned
                FROM e_basicinfo b
                LEFT JOIN s_position   sp  ON sp.positionCode   = b.positionCode
                LEFT JOIN s_department dep ON dep.departmentCode = b.departmentCode
                WHERE b.employeeNo = @employeeNo
                LIMIT 1";

            return _db.QueryFirstOrDefault<ThirteenthMonthPdfData>(sql, new { employeeNo });
        }

        private List<ThirteenthMonthLineItem> GetLines(string employeeNo, int? resolvedYear)
        {
            const string sql = @"
                SELECT
                    dateYear,
                    dateMonth,
                    cutoffType                                              AS CutoffType,
                    basicPaySemi                                            AS BasicPay,
                    absentAmount                                            AS Absent,
                    totalAmountLate                                         AS Late,
                    totalAmountUndertime                                    AS Undertime,
                    allow_basic                                             AS BasicAllowance,
                    (allow_tardy + allow_undertime + allow_absent)          AS AllowanceTardyUndertimeAbsent,
                    adjustment                                              AS Adjustment,
                    CAST((basicPaySemi
                          + adjustment
                          - totalAmountLate
                          - totalAmountUndertime
                          - absentAmount
                          + allow_basic
                          + allow_adjustment
                          - allow_tardy
                          - allow_undertime
                          - allow_absent) / 12 AS DECIMAL(10,2))            AS ThirteenthMonthPay
                FROM (
                    SELECT
                        p.dateYear,
                        p.dateMonth,
                        CASE WHEN p.cutOffType = 1 THEN '1st' ELSE '2nd' END    AS cutoffType,
                        p.employeeNo,
                        CAST(SUM(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan')
                             AS CHAR(200))) AS DECIMAL(10,2))                   AS basicPaySemi,
                        SUM(p.absentAmount)                                     AS absentAmount,
                        SUM(p.totalAmountLate)                                  AS totalAmountLate,
                        SUM(p.totalAmountUndertime)                             AS totalAmountUndertime,
                        SUM(p.reg_basic_al)                                     AS allow_basic,
                        SUM(p.tardy_al)                                         AS allow_tardy,
                        SUM(p.undertime_al)                                     AS allow_undertime,
                        SUM(p.absent_al)                                        AS allow_absent,
                        SUM(p.salary_adjustment_al)                             AS allow_adjustment,
                        IFNULL(t_13.adj_13th, 0)                                AS adjustment
                    FROM p_biometrics p
                    LEFT JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                    LEFT JOIN rq_13thmonth rq
                        ON rq.employeeNo = p.employeeNo
                        AND rq.isActive = 1
                        AND rq.statusName = 'Approved'
                        AND rq.dateYear = COALESCE(@resolvedYear, YEAR(b.dateOfEmpTermInitial))
                    LEFT JOIN (
                        SELECT cp.employeeNo, SUM(cp.approvedAmount) AS adj_13th
                        FROM c_payable cp
                        LEFT JOIN e_basicinfo ba ON ba.employeeNo = cp.employeeNo
                        WHERE cp.adjustmentCode IN ('TK ADJ', 'TARDY', 'INCLOGS')
                          AND cp.statusName IN ('Approved', 'Processed')
                          AND cp.isActive = 1
                          AND cp.dateToAdjustment
                              BETWEEN DATE(CONCAT(COALESCE(@resolvedYear, YEAR(ba.dateOfEmpTermInitial)) - 1, '-12-26'))
                              AND     DATE(CONCAT(COALESCE(@resolvedYear, YEAR(ba.dateOfEmpTermInitial)),   '-12-25'))
                    ) AS t_13 ON t_13.employeeNo = p.employeeNo
                    WHERE p.employeeNo = @employeeNo
                      AND p.isActive = 1
                      AND p.statusName IN ('Posted', 'POSTED')
                      AND ((    p.dateYear  = COALESCE(@resolvedYear, YEAR(b.dateOfEmpTermInitial))
                              AND p.dateMonth <> 'December')
                           OR ( p.dateYear  = COALESCE(@resolvedYear, YEAR(b.dateOfEmpTermInitial)) - 1
                              AND p.dateMonth  = 'December'))
                    GROUP BY p.employeeNo, p.dateYear, p.dateMonth, p.cutOffType
                    ORDER BY p.dateYear,
                             FIELD(p.dateMonth,'January','February','March','April','May','June',
                                   'July','August','September','October','November','December'),
                             p.cutOffType
                ) tbl";

            return _db.Query<ThirteenthMonthLineItem>(
                sql,
                new { employeeNo, resolvedYear = (object?)resolvedYear ?? DBNull.Value })
                .ToList();
        }
    }
}