using Dapper;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class PayrollRegisterExportController : Controller
    {
        private readonly IDbConnection _db;

        public PayrollRegisterExportController(IDbConnection db) => _db = db;

        // -------------------------------------------------------
        // Whitelist of safe SQL column expressions keyed by the
        // DataTable column name sent from the front-end.
        // AES_DECRYPT columns use a sub-expression so ORDER BY
        // can reference them without repeating the full cast.
        // -------------------------------------------------------
        private static readonly Dictionary<string, string> SortableColumns =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { "employeeNo",           "p.employeeNo" },
            { "fullName",             "b.lastName" },
            { "dailyRate",            "CAST(IFNULL(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))" },
            { "presentCount",         "p.presentCount" },
            { "basicPaySemi",         "CAST(IFNULL(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))" },
            { "totalRenderLate",      "p.totalRenderLate" },
            { "totalAmountLate",      "p.totalAmountLate" },
            { "totalRenderUndertime", "p.totalRenderUndertime" },
            { "totalAmountUndertime", "p.totalAmountUndertime" },
            { "absentCount",          "p.absentCount" },
            { "absentAmount",         "p.absentAmount" },
            { "renderOT",             "p.renderOT" },
            { "amountL",              "p.amountL" },
            { "amountNSDL",           "p.amountNSDL" },
            { "amountOTL",            "p.amountOTL" },
            { "amountREST",           "p.amountREST" },
            { "amountNSD",            "p.amountNSD" },
            { "amountNSDOT",          "p.amountNSDOT" },
            { "amountOT",             "p.amountOT" },
            { "amountS",              "p.amountS" },
            { "amountNSDS",           "p.amountNSDS" },
            { "amountOTS",            "p.amountOTS" },
            { "nonBasicPay",          "p.nonBasicPay" },
            { "totalAllowance",       "p.totalAllowance" },
            { "otherIncome",          "p.otherIncome" },
            { "otherEmployeePayable", "p.otherEmployeePayable" },
            { "grossIncome",          "CAST(IFNULL(CAST(AES_DECRYPT(p.grossIncome,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))" },
            { "deductionSSSemployee", "p.deductionSSSemployee" },
            { "deductionWISPemployee","p.deductionWISPemployee" },
            { "deductionPHIemployee", "p.deductionPHIemployee" },
            { "deductionPIFemployee", "p.deductionPIFemployee" },
            { "withHeldTax",          "p.withHeldTax" },
            { "reg_basic_al",         "p.reg_basic_al" },
            { "tardy_al",             "p.tardy_al" },
            { "undertime_al",         "p.undertime_al" },
            { "absent_al",            "p.absent_al" },
            { "salary_adjustment_al", "p.salary_adjustment_al" },
            { "lh_basic_al",          "p.lh_basic_al" },
            { "lh_nd_al",             "p.lh_nd_al" },
            { "lh_ot_al",             "p.lh_ot_al" },
            { "rd_basic_al",          "p.rd_basic_al" },
            { "reg_nd_al",            "p.reg_nd_al" },
            { "reg_ndot_al",          "p.reg_ndot_al" },
            { "reg_ot_al",            "p.reg_ot_al" },
            { "sh_basic_al",          "p.sh_basic_al" },
            { "sh_nd_al",             "p.sh_nd_al" },
            { "sh_ot_al",             "p.sh_ot_al" },
            { "sh_ndot_al",           "p.sh_ndot_al" },
            { "sssLoan",              "p.sssLoan" },
            { "sssCalamity",          "p.sssCalamity" },
            { "hdmfLoan",             "p.hdmfLoan" },
            { "hdmfCalamity",         "p.hdmfCalamity" },
            { "csbLoan",              "p.csbLoan" },
            { "hmoLoan",              "p.hmoLoan" },
            { "employeeLedger",       "p.employeeLedger" },
            { "otherLoan1",           "p.otherLoan1" },
            { "otherLoan2",           "p.otherLoan2" },
            { "otherLoan3",           "p.otherLoan3" },
            { "otherLoan4",           "p.otherLoan4" },
            { "totalDeduction",       "p.totalDeduction" },
            { "totalNetPay",          "CAST(IFNULL(CAST(AES_DECRYPT(p.totalNetPay,'portalkeisan') AS CHAR(200)),0) AS DECIMAL(10,2))" },
            { "departmentName",       "dep.departmentName" },
            { "bankCode",             "p.bankCode" },
            { "accountNo",            "p.accountNo" },
        };

        // -------------------------------------------------------
        // GET /PayrollRegisterExport/ExportToExcel
        //
        // sortColumn    — DataTable data property name (e.g. "totalNetPay")
        // sortDirection — "asc" or "desc"
        // offset/limit  — IGNORED: we always export the full filtered set
        // -------------------------------------------------------
        [HttpGet]
        public IActionResult ExportToExcel(
            string branch, string department, string cutOffType,
            string dateYear, string dateMonth, string statusName,
            string? sortColumn = null, string? sortDirection = "asc",
            int offset = 0, int limit = -1)   // kept for signature compat, not used
        {
            try
            {
                var data = GetPayrollRegisterData(
                    branch, department, cutOffType,
                    dateYear, dateMonth, statusName,
                    sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"{statusName}PayrollRegister_{dateMonth}_{dateYear}_{cutOffType}.xlsx";

                return File(
                    excelFile,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // -------------------------------------------------------
        // DATA RETRIEVAL — all matching rows, dynamic ORDER BY
        // -------------------------------------------------------
        private List<Dictionary<string, object>> GetPayrollRegisterData(
            string branch, string department, string cutOffType,
            string dateYear, string dateMonth, string statusName,
            string? sortColumn, string? sortDirection)
        {
            var query = new StringBuilder(@"
                SELECT
                    -- ── Identity ─────────────────────────────────────────────
                    p.employeeNo AS `Employee No`,
                    CONCAT(b.lastName, ', ', b.firstName, ' ',
                           LEFT(IFNULL(b.middleName,''), 1), '.') AS `Employee Name`,
                            dep.departmentName          AS `Department`,

                    -- ── Pay basics ───────────────────────────────────────────
                    CAST(IFNULL(CAST(AES_DECRYPT(p.dailyRate,'portalkeisan')
                         AS CHAR(200)),0) AS DECIMAL(10,2))                         AS `Daily Rate`,
                    CAST(IFNULL(p.presentCount,0) AS DECIMAL(10,2))                AS `Days Worked`,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan')
                         AS CHAR(200)),0) AS DECIMAL(10,2))                         AS `Basic Pay`,

                    -- ── Attendance deductions ────────────────────────────────
                    CAST(IFNULL(p.totalRenderLate,0)      AS DECIMAL(10,2))        AS `Late(mins)`,
                    CAST(IFNULL(p.totalAmountLate,0)      AS DECIMAL(10,2))        AS `Late Amount`,
                    CAST(IFNULL(p.totalRenderUndertime,0) AS DECIMAL(10,2))        AS `Undertime(mins)`,
                    CAST(IFNULL(p.totalAmountUndertime,0) AS DECIMAL(10,2))        AS `Undertime Amount`,
                    CAST(IFNULL(p.absentCount,0)          AS DECIMAL(10,2))        AS `Days Absent`,
                    CAST(IFNULL(p.absentAmount,0)         AS DECIMAL(10,2))        AS `Absent Amount`,

                    -- ── OT hours ─────────────────────────────────────────────
                    CAST(IFNULL(p.renderOT,0) AS DECIMAL(10,2))                    AS `OT(hrs)`,

                    -- ── Holiday / Rest Day pay amounts ───────────────────────
                    CAST(IFNULL(p.amountL,0)     AS DECIMAL(10,2))                 AS `LH BASIC`,
                    CAST(IFNULL(p.amountNSDL,0)  AS DECIMAL(10,2))                 AS `LH ND`,
                    CAST(IFNULL(p.amountOTL,0)   AS DECIMAL(10,2))                 AS `LH OT`,
                    CAST(IFNULL(p.amountREST,0)  AS DECIMAL(10,2))                 AS `RD BASIC`,
                    CAST(IFNULL(p.amountNSD,0)   AS DECIMAL(10,2))                 AS `REG ND`,
                    CAST(IFNULL(p.amountNSDOT,0) AS DECIMAL(10,2))                 AS `REG NDOT`,
                    CAST(IFNULL(p.amountOT,0)    AS DECIMAL(10,2))                 AS `REG OT`,
                    CAST(IFNULL(p.amountS,0)     AS DECIMAL(10,2))                 AS `SH BASIC`,
                    CAST(IFNULL(p.amountNSDS,0)  AS DECIMAL(10,2))                 AS `SH ND`,
                    CAST(IFNULL(p.amountOTS,0)   AS DECIMAL(10,2))                 AS `SH OT`,

                    -- ── OT Pay ───────────────────────────────────────────────
                    CAST(IFNULL(p.nonBasicPay,0) AS DECIMAL(10,2))                 AS `OT Pay`,

                    -- ── Allowance & income ───────────────────────────────────
                    CAST(IFNULL(p.totalAllowance,0)       AS DECIMAL(10,2))        AS `Total Allowance`,
                    CAST(IFNULL(p.otherIncome,0)          AS DECIMAL(10,2))        AS `Adjustment Taxable`,
                    CAST(IFNULL(p.otherEmployeePayable,0) AS DECIMAL(10,2))        AS `Adjustment Non-Tax`,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.grossIncome,'portalkeisan')
                         AS CHAR(200)),0) AS DECIMAL(10,2))                         AS `Gross Income`,

                    -- ── Government deductions ────────────────────────────────
                    CAST(IFNULL(p.deductionSSSemployee,0)  AS DECIMAL(10,2))       AS `SSS`,
                    CAST(IFNULL(p.deductionWISPemployee,0) AS DECIMAL(10,2))       AS `SSS WISP`,
                    CAST(IFNULL(p.deductionPHIemployee,0)  AS DECIMAL(10,2))       AS `PHI`,
                    CAST(IFNULL(p.deductionPIFemployee,0)  AS DECIMAL(10,2))       AS `PIF`,
                    CAST(IFNULL(p.withHeldTax,0)           AS DECIMAL(10,2))       AS `TAX`,

                    -- ── AL section ───────────────────────────────────────────
                    CAST(IFNULL(p.reg_basic_al,0)         AS DECIMAL(10,2))        AS `Reg Basic AL`,
                    CAST(IFNULL(p.tardy_al,0)             AS DECIMAL(10,2))        AS `Tardy AL`,
                    CAST(IFNULL(p.undertime_al,0)         AS DECIMAL(10,2))        AS `Undertime AL`,
                    CAST(IFNULL(p.absent_al,0)            AS DECIMAL(10,2))        AS `Absent AL`,
                    CAST(IFNULL(p.salary_adjustment_al,0) AS DECIMAL(10,2))        AS `Salary Adj AL`,
                    CAST(IFNULL(p.lh_basic_al,0)          AS DECIMAL(10,2))        AS `LH Basic AL`,
                    CAST(IFNULL(p.lh_nd_al,0)             AS DECIMAL(10,2))        AS `LH ND AL`,
                    CAST(IFNULL(p.lh_ot_al,0)             AS DECIMAL(10,2))        AS `LH OT AL`,
                    CAST(IFNULL(p.rd_basic_al,0)          AS DECIMAL(10,2))        AS `RD Basic AL`,
                    CAST(IFNULL(p.reg_nd_al,0)            AS DECIMAL(10,2))        AS `REG ND AL`,
                    CAST(IFNULL(p.reg_ndot_al,0)          AS DECIMAL(10,2))        AS `REG NDOT AL`,
                    CAST(IFNULL(p.reg_ot_al,0)            AS DECIMAL(10,2))        AS `REG OT AL`,
                    CAST(IFNULL(p.sh_basic_al,0)          AS DECIMAL(10,2))        AS `SH BASIC AL`,
                    CAST(IFNULL(p.sh_nd_al,0)             AS DECIMAL(10,2))        AS `SH ND AL`,
                    CAST(IFNULL(p.sh_ot_al,0)             AS DECIMAL(10,2))        AS `SH OT AL`,
                    CAST(IFNULL(p.sh_ndot_al,0)           AS DECIMAL(10,2))        AS `SH ND OT AL`,

                    -- ── Loans ────────────────────────────────────────────────
                    CAST(IFNULL(p.sssLoan,0)        AS DECIMAL(10,2))              AS `SSS Salary Loan`,
                    CAST(IFNULL(p.sssCalamity,0)    AS DECIMAL(10,2))              AS `SSS Calamity Loan`,
                    CAST(IFNULL(p.hdmfLoan,0)       AS DECIMAL(10,2))              AS `HDMF Salary Loan`,
                    CAST(IFNULL(p.hdmfCalamity,0)   AS DECIMAL(10,2))              AS `HDMF Calamity Loan`,
                    CAST(IFNULL(p.csbLoan,0)        AS DECIMAL(10,2))              AS `China Bank Savings Loan`,
                    CAST(IFNULL(p.hmoLoan,0)        AS DECIMAL(10,2))              AS `HMO Dependent`,
                    CAST(IFNULL(p.employeeLedger,0) AS DECIMAL(10,2))              AS `Employee Ledger`,
                    CAST(IFNULL(p.otherLoan1,0)     AS DECIMAL(10,2))              AS `Other Loan1`,
                    CAST(IFNULL(p.otherLoan2,0)     AS DECIMAL(10,2))              AS `Other Loan2`,
                    CAST(IFNULL(p.otherLoan3,0)     AS DECIMAL(10,2))              AS `Other Loan3`,
                    CAST(IFNULL(p.otherLoan4,0)     AS DECIMAL(10,2))              AS `Other Loan4`,

                    CAST(IFNULL(p.otherEmployeeReceivable,0)     AS DECIMAL(10,2)) AS `Other Deduction`,

                    -- ── Totals ───────────────────────────────────────────────
                    CAST(IFNULL(p.totalDeduction,0) AS DECIMAL(10,2))              AS `Total Deduction`,
                    CAST(IFNULL(CAST(AES_DECRYPT(p.totalNetPay,'portalkeisan')
                         AS CHAR(200)),0) AS DECIMAL(10,2))                         AS `NET PAY`,

                    -- ── Bank details ─────────────────────────────────────────
                    IFNULL(p.bankCode,'')  AS `Bank Code`,
                    IFNULL(p.accountNo,'') AS `Account No.`

                FROM p_biometrics p
                JOIN e_basicinfo b ON b.employeeNo = p.employeeNo
                LEFT JOIN s_department dep ON dep.departmentCode = p.departmentCode

                WHERE p.isActive = 1 ");

            var parameters = new DynamicParameters();
            ApplyFilters(query, parameters, branch, department, cutOffType, dateYear, dateMonth, statusName);

            // ── Dynamic ORDER BY ─────────────────────────────────────────────
            // Resolve the sort column from the whitelist; fall back to lastName.
            string orderByExpr = "p.employeeNo";
            if (!string.IsNullOrWhiteSpace(sortColumn)
                && SortableColumns.TryGetValue(sortColumn, out var sqlExpr))
            {
                orderByExpr = sqlExpr;
            }

            // Direction is safe: we only ever emit "ASC" or "DESC"
            string direction = string.Equals(sortDirection, "desc",
                                   StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

            query.Append($" ORDER BY {orderByExpr} {direction}");
            // No LIMIT — export all filtered rows

            var result = _db.Query(query.ToString(), parameters);
            var dataList = new List<Dictionary<string, object>>();

            foreach (var row in result)
            {
                var rowDict = (IDictionary<string, object>)row;
                dataList.Add(rowDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty));
            }

            return dataList;
        }

        // -------------------------------------------------------
        // FILTERS
        // -------------------------------------------------------
        private static void ApplyFilters(
            StringBuilder query, DynamicParameters parameters,
            string? branch, string? department, string? cutOffType,
            string? dateYear, string? dateMonth, string? statusName)
        {
            if (!string.IsNullOrWhiteSpace(branch) && branch != "ALL")
            {
                query.Append(" AND p.branchCode = @branchCode");
                parameters.Add("@branchCode", branch);
            }

            if (!string.IsNullOrWhiteSpace(department) && department != "ALL")
            {
                query.Append(" AND b.departmentCode = @department");
                parameters.Add("@department", department);
            }

            if (!string.IsNullOrWhiteSpace(cutOffType))
            {
                query.Append(" AND p.cutOffType = @cutOffType");
                parameters.Add("@cutOffType", cutOffType);
            }

            if (!string.IsNullOrWhiteSpace(dateYear))
            {
                query.Append(" AND p.dateYear = @dateYear");
                parameters.Add("@dateYear", dateYear);
            }

            if (!string.IsNullOrWhiteSpace(dateMonth))
            {
                query.Append(" AND p.dateMonth = @dateMonth");
                parameters.Add("@dateMonth", dateMonth);
            }

            if (!string.IsNullOrWhiteSpace(statusName))
            {
                query.Append(" AND p.statusName = @statusName");
                parameters.Add("@statusName", statusName);
            }
        }

        // -------------------------------------------------------
        // EXCEL GENERATION
        // -------------------------------------------------------
        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Payroll Register");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // ------ Row 1: Main Title (merged, centered — matches Review DTR style) ------
            ws.Cells[1, 1].Value = "Payroll Register";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Cells[1, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            ws.Row(1).Height = 26;

            // ------ Row 2: Generated by + Timestamp (merged, single row — matches Review DTR style) ------
            var sessionUserFullName = HttpContext.Session.GetString("userFullName");
            var sessionEmployeeNo = HttpContext.Session.GetString("employeeNo") ?? "";
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({sessionEmployeeNo}) {sessionUserFullName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // Row 3: blank spacer

            // ------ Row 5: Column headers ------
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[5, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // ------ Rows 6+: Data ------
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 6, col + 1];
                    var columnName = columns[col];
                    var cellValue = data[row][columnName];

                    cell.Value = cellValue ?? string.Empty;

                    if (IsNumericColumn(columnName))
                    {
                        cell.Style.Numberformat.Format = "#,##0.00";
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }
                }
            }

            // ------ Totals row ------
            int firstDataRow = 6;
            int lastDataRow = rowCount + 5;
            int totalsRow = rowCount + 6;

            ws.Cells[totalsRow, 1].Value = "TOTAL";
            ws.Cells[totalsRow, 1].Style.Font.Bold = true;

            for (int col = 0; col < columns.Count; col++)
            {
                if (!IsNumericColumn(columns[col])) continue;

                var cell = ws.Cells[totalsRow, col + 1];
                string excelCol = ExcelCellAddress.GetColumnLetter(col + 1);

                cell.Formula = $"SUM({excelCol}{firstDataRow}:{excelCol}{lastDataRow})";
                cell.Style.Numberformat.Format = "#,##0.00";
                cell.Style.Font.Bold = true;
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            }

            // ------ Borders ------
            var range = ws.Cells[5, 1, totalsRow, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells[totalsRow, 1, totalsRow, columns.Count]
                .Style.Border.Top.Style = ExcelBorderStyle.Medium;

            ws.Cells.AutoFitColumns();

            return package.GetAsByteArray();
        }

        // -------------------------------------------------------
        // STYLING
        // -------------------------------------------------------
        private static void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(
                System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        // -------------------------------------------------------
        // NUMERIC COLUMN DETECTION
        // -------------------------------------------------------
        private static bool IsNumericColumn(string columnName)
        {
            var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Daily Rate", "Days Worked", "Basic Pay",
                "Late(mins)", "Late Amount",
                "Undertime(mins)", "Undertime Amount",
                "Days Absent", "Absent Amount",
                "OT(hrs)", "OT Pay",
                "LH BASIC", "LH ND", "LH OT",
                "RD BASIC",
                "REG ND", "REG NDOT", "REG OT",
                "SH BASIC", "SH ND", "SH OT",
                "Total Allowance", "Adjustment Taxable", "Adjustment Non-Tax",
                "Gross Income",
                "SSS", "SSS WISP", "PHI", "PIF", "TAX",
                "Reg Basic AL", "Tardy AL", "Undertime AL", "Absent AL", "Salary Adj AL",
                "LH Basic AL", "LH ND AL", "LH OT AL",
                "RD Basic AL",
                "REG ND AL", "REG NDOT AL", "REG OT AL",
                "SH BASIC AL", "SH ND AL", "SH OT AL", "SH ND OT AL",
                "SSS Salary Loan", "SSS Calamity Loan",
                "HDMF Salary Loan", "HDMF Calamity Loan",
                "China Bank Savings Loan", "HMO Dependent", "Employee Ledger",
                "Other Loan1", "Other Loan2", "Other Loan3", "Other Loan4",
                "Total Deduction", "NET PAY", "Other Deduction"
            };

            return numericColumns.Contains(columnName);
        }
    }
}