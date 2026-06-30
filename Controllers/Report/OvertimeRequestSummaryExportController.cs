using Dapper;
using Microsoft.AspNetCore.Mvc;
using Mysqlx.Crud;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Org.BouncyCastle.Asn1.X509;
using System.Data;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class OvertimeRequestSummaryExportController : Controller
    {
        private readonly IDbConnection _db;

        public OvertimeRequestSummaryExportController(IDbConnection db) => _db = db;

        // Exports request data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(
            string employeeno, string datefrom, string dateto, int offset = 0, int limit = -1
            )
        {
            try
            {
                var data = GetRequestData(employeeno, datefrom, dateto, offset, limit );

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"OvertimeRequest_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // Retrieves official business request records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetRequestData(string employeeno, string datefrom, string dateto, int offset, int limit)
        {
            var query = new StringBuilder(@"
                SELECT 
                    rq.employeeNo AS EmployeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS EmployeeName,
                    CONCAT(DATE_FORMAT(rq.overtimeDateIn, '%m/%d/%Y'), ' ', TIME_FORMAT(overTimeIn, '%h:%i %p')) AS OTRequestIN,
                    CONCAT(DATE_FORMAT(rq.OvertimeDateOut, '%m/%d/%Y'), ' ', TIME_FORMAT(overTimeOut, '%h:%i %p')) AS OTRequestOUT,
                    rq.overtimeReason AS OTReason,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y') AS DateRequested

                FROM rq_overtime rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo

                WHERE rq.isActive = 1 
                AND rq.statusName IN ('Approved', 'Processed')
                AND rq.employeeNo = @employeeno
                AND rq.overtimeDateIn BETWEEN @datefrom AND @dateto

                ");

            var parameters = new DynamicParameters();
            //ApplyFilters(query, parameters, branch, department, offset, limit);
            parameters.Add("@employeeno", employeeno);
            parameters.Add("@datefrom", datefrom);
            parameters.Add("@dateto", dateto);


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
             string? branch, string? department,int? offSet, int? limit)
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
            var ws = package.Workbook.Worksheets.Add("Overtime Request Report");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;
            int colCount = columns.Count;

            // ROW 1: Title (centered, merged)
            ws.Cells[1, 1].Value = "Overtime Request Report";
            ws.Cells[1, 1, 1, colCount].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // ROW 2: Generated by + timestamp
            var sessionUserFullName = HttpContext.Session.GetString("userFullName") ?? "Unknown";
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            ws.Cells[2, 1].Value = $"Generated By: {sessionUserFullName}     Timestamp: {timestamp}";
            ws.Cells[2, 1, 2, colCount].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;

            // ROW 3: blank spacer

            // ROW 4: Headers
            for (int col = 0; col < colCount; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // ROWS 5+: Data
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < colCount; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    cell.Value = data[row][columns[col]] ?? string.Empty;
                }
            }

            // Borders
            int totalsRow = rowCount + 4;
            var range = ws.Cells[4, 1, totalsRow, colCount];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            ws.Cells.AutoFitColumns();
            ws.View.FreezePanes(5, 1);

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
                ""
            };

            return numericColumns.Contains(columnName);
        }
    }
}
