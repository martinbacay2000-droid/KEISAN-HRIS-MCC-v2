using Dapper;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.LeaveRequest
{
    public class LeaveRequestPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly LeaveRequestPdfService _pdfService;

        // Constructor: Initializes database connection and PDF service via dependency injection
        public LeaveRequestPdfController(IDbConnection db, LeaveRequestPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // Generates and downloads Leave Request PDF file with timestamped filename
        [HttpGet]
        public IActionResult GeneratePdf(int id)
        {
            try
            {
                var data = GetLeaveRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Leave Request not found!" });

                var pdfBytes = _pdfService.GenerateLeaveRequestPdf(data);
                var fileName = $"Leave_Request_{data.EmployeeNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF" });
            }
        }

        // Generates and displays Leave Request PDF in browser for preview
        [HttpGet]
        public IActionResult PreviewPdf(int id)
        {
            try
            {
                var data = GetLeaveRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Leave Request not found!" });

                var pdfBytes = _pdfService.GenerateLeaveRequestPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF preview: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF preview" });
            }
        }

        // Retrieves Leave Request data from database with formatted dates and employee information
        private LeaveRequestPdfData? GetLeaveRequestData(int id)
        {
            var sql = @"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName, 1), ''), '.') AS fullName,
                    rq.leaveCode,
                    s.leaveName,
                    rq.leaveType,
                    DATE_FORMAT(rq.leaveDateFrom, '%M %d, %Y') AS displayDateFrom,
                    DATE_FORMAT(rq.leaveDateTo, '%M %d, %Y') AS displayDateTo,
                    rq.leaveCountDays,
                    rq.leaveCountHours,
                    rq.leaveReason,
                    rq.creditDeductionOnly,
                    rq.remarks,
                    rq.statusLevel4 AS statusName,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(LEFT(req.middleName, 1), ''), '.') AS requestedByUser,
                    DATE_FORMAT(rq.dtAdded, '%M %d, %Y %h:%i %p') AS dateRequested,
                    DATE_FORMAT(rq.dtLastModified, '%M %d, %Y %h:%i %p') AS lastModified
                FROM rq_leave rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN s_leave s ON rq.leaveCode = s.leaveCode
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.addedByUser
                WHERE rq.id = @Id AND rq.isActive = 1";

            return _db.QueryFirstOrDefault<LeaveRequestPdfData>(sql, new { Id = id });
        }
    }
}