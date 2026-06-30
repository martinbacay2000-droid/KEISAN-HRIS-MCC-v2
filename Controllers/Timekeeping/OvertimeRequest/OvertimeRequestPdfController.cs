using Dapper;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.OvertimeRequest
{
    public class OvertimeRequestPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly OvertimeRequestPdfService _pdfService;

        // Constructor: Initializes database connection and PDF service via dependency injection
        public OvertimeRequestPdfController(IDbConnection db, OvertimeRequestPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // Generates and downloads Overtime Request PDF file with timestamped filename
        [HttpGet]
        public IActionResult GeneratePdf(int id)
        {
            try
            {
                var data = GetOvertimeRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Overtime Request not found!" });

                var pdfBytes = _pdfService.GenerateOvertimeRequestPdf(data);
                var fileName = $"Overtime_Request_{data.EmployeeNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF" });
            }
        }

        // Generates and displays Overtime Request PDF in browser for preview
        [HttpGet]
        public IActionResult PreviewPdf(int id)
        {
            try
            {
                var data = GetOvertimeRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Overtime Request not found!" });

                var pdfBytes = _pdfService.GenerateOvertimeRequestPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF preview: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF preview" });
            }
        }

        // Retrieves Overtime Request data from database with formatted dates and employee information
        private OvertimeRequestPdfData? GetOvertimeRequestData(int id)
        {
            var sql = @"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName, 1), ''), '.') AS employeeName,
                    DATE_FORMAT(rq.overTimeDateIN, '%M %d, %Y') as displayDateIn,
                    TIME_FORMAT(rq.overTimeIN, '%h:%i %p') as displayTimeIn,
                    DATE_FORMAT(rq.overTimeDateOUT, '%M %d, %Y') as displayDateOut,
                    TIME_FORMAT(rq.overTimeOUT, '%h:%i %p') as displayTimeOut,
                    rq.overTimeReason,
                    rq.remarks,
                    rq.statusLevel4 AS statusName,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(LEFT(req.middleName, 1), ''), '.') AS requestedByUser,
                    DATE_FORMAT(rq.dtAdded, '%M %d, %Y %h:%i %p') as dateRequested,
                    DATE_FORMAT(rq.dtLastModified, '%M %d, %Y %h:%i %p') as lastModified
                FROM rq_overtime rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                WHERE rq.id = @Id AND rq.isActive = 1";

            return _db.QueryFirstOrDefault<OvertimeRequestPdfData>(sql, new { Id = id });
        }
    }
}