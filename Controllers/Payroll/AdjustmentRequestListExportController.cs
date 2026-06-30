using Dapper;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Payroll
{
    public class AdjustmentRequestListExportController : Controller
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public AdjustmentRequestListExportController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        [HttpGet]
        public IActionResult ExportToExcel(string? status, string? adjustmentType,
               string? dateFrom, string? dateTo, int offset = 0, int limit = -1)
        {
            try
            {
                var employeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(employeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                var employeeInfo = GetEmployeeInfo(employeeNo);
                var data = GetAdjustmentRequestData(status, adjustmentType, dateFrom, dateTo, offset, limit);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, employeeInfo);
                var fileName = $"AdjustmentRequest_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                var filterDetails = BuildFilterDetails(status, adjustmentType, dateFrom, dateTo);
                _auditTrail.Log("c_payable", 0, "EXPORTED",
                    $"Exported {data.Count} adjustment request(s) to Excel. Filters: {filterDetails}");

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        private string BuildFilterDetails(string? status, string? adjustmentType, string? dateFrom, string? dateTo)
        {
            var filters = new List<string>();

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
                filters.Add($"Status={status}");
            else if (string.IsNullOrWhiteSpace(status) || status == "Default")
                filters.Add("Status=Pending");

            if (!string.IsNullOrWhiteSpace(adjustmentType) && adjustmentType != "ALL")
                filters.Add($"Type={adjustmentType}");

            if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
                filters.Add($"Date={dateFrom} to {dateTo}");

            return filters.Any() ? string.Join(", ", filters) : "No filters applied";
        }

        private (string EmployeeNo, string EmployeeName) GetEmployeeInfo(string employeeNo)
        {
            var userQuery = @"
                SELECT 
                    userCode,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM s_user
                WHERE userCode = @employeeNo
                LIMIT 1";

            var userResult = _db.QueryFirstOrDefault<dynamic>(userQuery, new { employeeNo });
            if (userResult != null)
                return (userResult.userCode, userResult.employeeName);

            var empQuery = @"
                SELECT 
                    employeeNo,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM e_basicinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1";

            var empResult = _db.QueryFirstOrDefault<dynamic>(empQuery, new { employeeNo });
            if (empResult != null)
                return (empResult.employeeNo, empResult.employeeName);

            return (employeeNo, "Unknown User");
        }

        private List<Dictionary<string, object>> GetAdjustmentRequestData(string? status, string? adjustmentType,
            string? dateFrom, string? dateTo, int offset, int limit)
        {
            var query = new StringBuilder(@"
                SELECT 
                    ap.employeeNo AS 'Employee No',
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(CONCAT(LEFT(b.middleName, 1), '.'), '')) AS 'Employee Name',
                    COALESCE(adj.adjustmentName, sa.allowanceName) AS 'Adjustment Type',
                    ap.amount AS 'Requested Amount',
                    DATE_FORMAT(ap.dateToAdjustment, '%Y-%m-%d') AS 'Effectivity Date',
                    IFNULL(ap.reason, '') AS 'Remarks',
                    ap.statusName AS 'Status',
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(CONCAT(LEFT(req.middleName, 1), '.'), '')) AS 'Requested By',
                    DATE_FORMAT(ap.dtAdded, '%Y-%m-%d %H:%i:%s') AS 'Date Requested'
                FROM c_payable ap
                INNER JOIN e_basicinfo b ON b.employeeNo = ap.employeeNo
                LEFT JOIN s_adjustment adj ON adj.adjustmentCode = ap.adjustmentCode
                LEFT JOIN s_allowance sa ON sa.allowanceCode = ap.adjustmentCode
                LEFT JOIN e_basicinfo req ON req.employeeNo = ap.requestedByUser
                WHERE ap.isActive = 1");

            var parameters = new DynamicParameters();
            ApplyFilters(query, parameters, status, adjustmentType, dateFrom, dateTo);

            query.Append(" ORDER BY ap.id DESC");

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

        private void ApplyFilters(StringBuilder query, DynamicParameters parameters,
            string? status, string? adjustmentType, string? dateFrom, string? dateTo)
        {
            if (string.IsNullOrWhiteSpace(status) || status == "Default")
            {
                query.Append(" AND ap.statusName = @status");
                parameters.Add("@status", "Pending");
            }
            else if (status != "All")
            {
                query.Append(" AND ap.statusName = @status");
                parameters.Add("@status", status);
            }

            if (!string.IsNullOrWhiteSpace(adjustmentType) && adjustmentType != "ALL")
            {
                query.Append(" AND ap.adjustmentCode = @adjustmentType");
                parameters.Add("@adjustmentType", adjustmentType);
            }

            if (!string.IsNullOrWhiteSpace(dateFrom) && !string.IsNullOrWhiteSpace(dateTo))
            {
                query.Append(" AND DATE(ap.dateToAdjustment) BETWEEN @dateFrom AND @dateTo");
                parameters.Add("@dateFrom", dateFrom);
                parameters.Add("@dateTo", dateTo);
            }
        }

        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data, (string EmployeeNo, string EmployeeName) employeeInfo)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Adjustment Requests");
            if (data.Count == 0) return package.GetAsByteArray();
            var columns = data[0].Keys.ToList();
            var rowCount = data.Count;

            ws.Cells[1, 1].Value = "Adjustment Request List";
            ws.Cells[1, 1, 1, columns.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({employeeInfo.EmployeeNo}) {employeeInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columns.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            for (int col = 0; col < columns.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columns[col];
                StyleHeader(cell);
            }

            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columns.Count; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    var cellValue = data[row][columns[col]];
                    cell.Value = cellValue?.ToString() ?? string.Empty;

                    if (columns[col] == "Status" && cellValue != null)
                        ApplyStatusColor(cell, cellValue.ToString() ?? string.Empty);

                    if (columns[col] == "Requested Amount" && cellValue != null)
                    {
                        ApplyAmountColor(cell, cellValue.ToString() ?? string.Empty);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }
                }
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            var range = ws.Cells[4, 1, rowCount + 4, columns.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            return package.GetAsByteArray();
        }

        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

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

        private void ApplyAmountColor(ExcelRange cell, string amountStr)
        {
            if (string.IsNullOrEmpty(amountStr)) return;

            if (double.TryParse(amountStr, out double amount))
            {
                cell.Style.Font.Bold = true;
                cell.Style.Font.Color.SetColor(amount >= 0
                    ? System.Drawing.Color.FromArgb(0, 97, 0)
                    : System.Drawing.Color.FromArgb(156, 0, 6));
            }
        }
    }
}