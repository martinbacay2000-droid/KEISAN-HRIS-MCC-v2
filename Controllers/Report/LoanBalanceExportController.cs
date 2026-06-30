using Dapper;
using Microsoft.AspNetCore.Mvc;
using Mysqlx.Crud;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Org.BouncyCastle.Asn1.X509;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class LoanBalanceExportController : Controller
    {
        private readonly IDbConnection _db;

        public LoanBalanceExportController(IDbConnection db) => _db = db;

        // Exports request data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(
            string branch, string department, string loancode, string loanStatus = "Ongoing", int offset = 0, int limit = -1
            )
        {
            try
            {
                var data = GetRequestData(branch, department, loancode, loanStatus, offset, limit);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"LoanBalanceReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // Retrieves official business request records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetRequestData(string branch, string department, string loancode, string loanStatus, int offset, int limit)
        {
            var query = new StringBuilder(@"
                SELECT
                    br.branchName Company,
                    dep.departmentName Department,
                    el.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName,1),''), CASE WHEN IFNULL(b.middleName,'')<>'' THEN '.' ELSE '' END) AS EmployeeName,
                    sl.LoanName,
                    DATE_FORMAT(el.dateGranted,'%m/%d/%Y') DateGranted,
                    DATE_FORMAT(el.deductionStartDate,'%m/%d/%Y') DeductionStartDate,
                    el.DeductionSchedule,
                    el.PrincipalAmount,
                    el.amortizationAmount `Monthly Amortization`,
                    ROUND(
                        el.principalAmount - IFNULL((SELECT SUM(IFNULL(m.credit,0))
                                FROM m_loan m
                                WHERE m.e_loanID = el.id
                                AND m.isActive = 1
                                AND m.statusName = 'Added'), 0)
                    , 2) AS LoanBalance,
                    el.statusName AS Status
                FROM e_loan el
                LEFT JOIN s_loan sl ON sl.loanCode = el.loanCode
                LEFT JOIN e_basicinfo b ON b.employeeNo = el.employeeNo
                LEFT JOIN s_department dep ON dep.departmentCode = b.departmentCode
                LEFT JOIN s_branch br ON br.branchCode = b.branchCode
                WHERE b.isActive = 1
                AND el.isActive = 1
                AND (@brcode = '' OR @brcode = 'ALL' OR b.branchCode = @brcode)
                AND (@department = '' OR @department = 'ALL' OR b.departmentCode = @department)
                AND (@loancode = '' OR @loancode = 'ALL' OR el.loanCode = @loancode)
                AND (@loanStatus = 'ALL' OR el.statusName = @loanStatus)
                ORDER BY 1,2,3

                ");

            var parameters = new DynamicParameters();
            //ApplyFilters(query, parameters, branch, department, offset, limit);
            parameters.Add("@brcode", branch);
            parameters.Add("@department", department);
            parameters.Add("@loancode", loancode);
            parameters.Add("@loanStatus", loanStatus);


            if (limit > 0)
                query.Append($" LIMIT {limit} OFFSET {offset}");

            var result = _db.Query(query.ToString(), parameters);
            var dataList = new List<Dictionary<string, object>>();

            foreach (var row in result)
            {
                var rowDict = (IDictionary<string, object>)row;
                dataList.Add(rowDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty));
            }

            return dataList;
        }

        // Applies filter conditions to the SQL query based on status, branch, department, and date range parameters
        private void ApplyFilters(StringBuilder query, DynamicParameters parameters,
             string? branch, string? department, int? offSet, int? limit)
        {
            // Branch filter
            if (!string.IsNullOrWhiteSpace(branch) && branch != "ALL")
            {
                query.Append(" AND b.branchCode = @branchCode");
                parameters.Add("@branchCode", branch);
            }

            // Department filter
            if (!string.IsNullOrWhiteSpace(department) && department != "ALL")
            {
                query.Append(" AND b.departmentCode = @department");
                parameters.Add("@department", department);
            }

        }

        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Loan Balance Report");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            var employeeNo = HttpContext.Session.GetString("employeeNo");
            var employeeName = HttpContext.Session.GetString("userFullName") ?? "Unknown User";

            // ROW 1 - Title
            ws.Cells[1, 1].Value = "Loan Balance Report";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ROW 2 - Generated by
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({employeeNo}) {employeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // ROW 4 - Headers
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // ROW 5+ - Data
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    var columnName = columns[col];
                    var cellValue = data[row][columnName];

                    cell.Value = cellValue ?? string.Empty;

                    if (IsNumericColumn(columnName))
                    {
                        if (decimal.TryParse(cellValue?.ToString(), out decimal numValue))
                        {
                            cell.Value = numValue;
                            cell.Style.Numberformat.Format = "#,##0.00";
                            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                        }
                    }
                }
            }

            // TOTALS ROW
            int firstDataRow = 5;
            int lastDataRow = rowCount + 4;
            int totalsRow = rowCount + 5;

            ws.Cells[totalsRow, 1].Value = "TOTAL";
            ws.Cells[totalsRow, 1].Style.Font.Bold = true;

            for (int col = 0; col < columns.Count; col++)
            {
                if (IsNumericColumn(columns[col]))
                {
                    var cell = ws.Cells[totalsRow, col + 1];
                    string excelCol = ExcelCellAddress.GetColumnLetter(col + 1);

                    cell.Formula = $"SUM({excelCol}{firstDataRow}:{excelCol}{lastDataRow})";
                    cell.Style.Numberformat.Format = "#,##0.00";
                    cell.Style.Font.Bold = true;
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                }
            }

            // Borders
            var range = ws.Cells[4, 1, totalsRow, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells[totalsRow, 1, totalsRow, columns.Count]
                .Style.Border.Top.Style = ExcelBorderStyle.Medium;

            ws.Cells.AutoFitColumns();

            return package.GetAsByteArray();
        }

        // Applies bold blue header styling with white text and center alignment to the specified cell
        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }


        private bool IsNumericColumn(string columnName)
        {
            var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "LoanBalance",
                "PrincipalAmount",
                "Monthly Amortization"
            };
            // Status column is non-numeric, no change needed here
            // but ensures it won't accidentally be formatted as a number

            return numericColumns.Contains(columnName);
        }

    }
}
