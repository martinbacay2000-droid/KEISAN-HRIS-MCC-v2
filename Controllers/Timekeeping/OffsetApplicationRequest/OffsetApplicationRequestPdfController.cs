using Dapper;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.OffsetApplicationRequest
{
    public class OffsetApplicationRequestPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly OffsetApplicationRequestPdfService _pdfService;

        // Constructor: Initializes database connection and PDF service via dependency injection
        public OffsetApplicationRequestPdfController(IDbConnection db, OffsetApplicationRequestPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // Generates and downloads Offset Application Request PDF file with timestamped filename
        [HttpGet]
        public IActionResult GeneratePdf(int id)
        {
            try
            {
                var data = GetOffsetApplicationRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Offset Application Request not found!" });

                var pdfBytes = _pdfService.GenerateOffsetApplicationRequestPdf(data);
                var fileName = $"CTO_Application_{data.EmployeeNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF" });
            }
        }

        // Generates and displays Offset Application Request PDF in browser for preview
        [HttpGet]
        public IActionResult PreviewPdf(int id)
        {
            try
            {
                var data = GetOffsetApplicationRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Offset Application Request not found!" });

                var pdfBytes = _pdfService.GenerateOffsetApplicationRequestPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF preview: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF preview" });
            }
        }

        // Retrieves Offset Application Request data from database with formatted dates and employee information
        private OffsetApplicationRequestPdfData? GetOffsetApplicationRequestData(int id)
        {
            var sql = @"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName, 1), ''), '.') AS fullName,
                    rq.leaveCode,
                    s.leaveName,
                    DATE_FORMAT(rq.overTimeDateIN, '%M %d, %Y') AS displayDateIn,
                    TIME_FORMAT(rq.overTimeIN, '%h:%i %p') AS displayTimeIn,
                    DATE_FORMAT(rq.overTimeDateOUT, '%M %d, %Y') AS displayDateOut,
                    TIME_FORMAT(rq.overTimeOUT, '%h:%i %p') AS displayTimeOut,
                    rq.approvedRenderOT,
                    rq.overTimeReason,
                    rq.remarks,
                    rq.statusLevel4 AS statusName,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(LEFT(req.middleName, 1), ''), '.') AS requestedByUser,
                    DATE_FORMAT(rq.dtAdded, '%M %d, %Y %h:%i %p') AS dateRequested,
                    DATE_FORMAT(rq.dtLastModified, '%M %d, %Y %h:%i %p') AS lastModified
                FROM rq_cto rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN s_leave s ON rq.leaveCode = s.leaveCode
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.addedByUser
                WHERE rq.id = @Id AND rq.isActive = 1";

            return _db.QueryFirstOrDefault<OffsetApplicationRequestPdfData>(sql, new { Id = id });
        }
    }
}