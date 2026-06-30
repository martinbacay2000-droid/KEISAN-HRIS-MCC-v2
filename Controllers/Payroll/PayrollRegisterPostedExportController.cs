using Dapper;
using Microsoft.AspNetCore.Mvc;
using Mysqlx.Crud;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class PayrollRegisterPostedExportController : Controller
    {
        private readonly IDbConnection _db;

        public PayrollRegisterPostedExportController(IDbConnection db) => _db = db;

        // Exports official business request data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(
            string branch, string department, string cutOffType, string dateYear, string dateMonth, int offset = 0, int limit = -1
            )
        {
            try
            {
                var data = GetPayrollRegisterRequestData(branch, department, cutOffType, dateYear, dateMonth, offset, limit );

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data);
                var fileName = $"PayrollRegisterPosted_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // Retrieves official business request records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetPayrollRegisterRequestData(string branch, string department, string cutOffType, string dateYear, string dateMonth, int offset, int limit)
        {
            var query = new StringBuilder(@"
                SELECT 
                    p.employeeNo AS employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', LEFT(IFNULL(b.middleName,''), 1), '.') AS fullName,

                    IFNULL(p.dailyRate,0) as dailyRate,
                    IFNULL(p.basicPaySemi,0) as basicPaySemi,
                    IFNULL(p.nonBasicPay,0) as nonBasicPay,

                    IFNULL(p.totalAmountLate,0) as amountLate,
                    IFNULL(p.totalAmountUndertime,0) as amountUndertime,
                    IFNULL(p.absentAmount,0) as absentAmount,

                    IFNULL(p.totalAllowance,0) as totalAllowance,
                    IFNULL(p.otherIncome,0) as otherIncome,
                    IFNULL(p.otherEmployeePayable,0) as otherEmployeePayable,

                    IFNULL(p.deductionSSSemployee,0) as deductionSSSemployee,
                    IFNULL(p.deductionPHIemployee,0) as deductionPHIemployee,
                    IFNULL(p.deductionPIFemployee,0) as deductionPIFemployee,
                    IFNULL(p.withHeldTax,0) as withHeldTax,

                    IFNULL(p.grossIncome,0) as grossIncome,
                    IFNULL(p.totalDeduction,0) as totalDeduction,

                    IFNULL(p.totalNetPay,0) as totalNetPay,
                    p.bankCode,
                    p.accountNo

                FROM p_biometrics p
                JOIN e_basicinfo b ON b.employeeNo = p.employeeNo

                WHERE p.isActive = 1 AND p.statusName = 'Posted'");

            var parameters = new DynamicParameters();
            ApplyFilters(query, parameters, branch, department, cutOffType, dateYear, dateMonth, offset, limit);

            query.Append(" ORDER BY p.id ");

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
             string? branch, string? department, string? cutOffType, string? dateyear, string? dateMonth, int? offSet, int? limit)
        {
            // Branch filter
            if (!string.IsNullOrWhiteSpace(branch) && branch != "ALL")
            {
                query.Append(" AND p.branchCode = @branchCode");
                parameters.Add("@branchCode", branch);
            }

            // Department filter
            if (!string.IsNullOrWhiteSpace(department) && department != "ALL")
            {
                query.Append(" AND b.departmentCode = @department");
                parameters.Add("@department", department);
            }

            // cutOffType filter
            if (!string.IsNullOrWhiteSpace(cutOffType))
            {
                query.Append(" AND p.cutOffType = @cutOffType");
                parameters.Add("@cutOffType", cutOffType);
            }
            // dateyear filter
            if (!string.IsNullOrWhiteSpace(dateyear))
            {
                query.Append(" AND p.dateyear = @dateyear");
                parameters.Add("@dateyear", dateyear);
            }
            // dateyear filter
            if (!string.IsNullOrWhiteSpace(dateMonth))
            {
                query.Append(" AND p.dateMonth = @dateMonth");
                parameters.Add("@dateMonth", dateMonth);
            }

        }

        // Generates an Excel file from the provided data with formatted headers, borders, and status color coding
        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Payroll Register");

            if (data.Count == 0) return package.GetAsByteArray();

            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            // Add headers
            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[1, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            // Add data rows
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 2, col + 1];
                    var cellValue = data[row][columns[col]];
                    cell.Value = cellValue?.ToString() ?? string.Empty;

                    //if (columns[col] == "Status" && cellValue != null)
                    //    ApplyStatusColor(cell, cellValue.ToString() ?? string.Empty);
                }
            }

            // Format table
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            var range = ws.Cells[1, 1, rowCount + 1, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

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

        // Applies color coding to status cells based on status value (green for approved, red for declined, etc.)
        private void ApplyStatusColor(ExcelRange cell, string status)
        {
            if (string.IsNullOrEmpty(status)) return;

            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Font.Bold = true;

            var (bgColor, fgColor) = status.ToUpper() switch
            {
                "APPROVED" => (System.Drawing.Color.FromArgb(198, 239, 206), System.Drawing.Color.FromArgb(0, 97, 0)),
                "DECLINED" => (System.Drawing.Color.FromArgb(255, 199, 206), System.Drawing.Color.FromArgb(156, 0, 6)),
                "CANCELLED" => (System.Drawing.Color.FromArgb(196, 196, 196), System.Drawing.Color.FromArgb(68, 68, 68)),
                "PROCESSED" => (System.Drawing.Color.FromArgb(180, 235, 250), System.Drawing.Color.FromArgb(0, 67, 88)),
                _ => (System.Drawing.Color.FromArgb(255, 242, 204), System.Drawing.Color.FromArgb(156, 101, 0))
            };

            cell.Style.Fill.BackgroundColor.SetColor(bgColor);
            cell.Style.Font.Color.SetColor(fgColor);
        }
    }
}
