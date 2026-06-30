using Dapper;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.OffsetCreditRequest
{
    public class OffsetCreditRequestPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly OffsetCreditRequestPdfService _pdfService;

        // Constructor: Initializes database connection and PDF service via dependency injection
        public OffsetCreditRequestPdfController(IDbConnection db, OffsetCreditRequestPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // Generates and downloads Offset Credit Request PDF file with timestamped filename
        [HttpGet]
        public IActionResult GeneratePdf(int id)
        {
            try
            {
                var data = GetOffsetCreditRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Offset Credit Request not found!" });

                var pdfBytes = _pdfService.GenerateOffsetCreditRequestPdf(data);
                var fileName = $"OffsetCredit_Request_{data.EmployeeNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF" });
            }
        }

        // Generates and displays Offset Credit Request PDF in browser for preview
        [HttpGet]
        public IActionResult PreviewPdf(int id)
        {
            try
            {
                var data = GetOffsetCreditRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Offset Credit Request not found!" });

                var pdfBytes = _pdfService.GenerateOffsetCreditRequestPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF preview: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF preview" });
            }
        }

        // Retrieves Offset Credit Request data from database with formatted dates and employee information
        private OffsetCreditRequestPdfData? GetOffsetCreditRequestData(int id)
        {
            var sql = @"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName, 1), ''), '.') AS employeeName,
                    DATE_FORMAT(rq.offsetDateIn, '%M %d, %Y') as displayDateIn,
                    TIME_FORMAT(rq.offsetTimeIn, '%h:%i %p') as displayTimeIn,
                    DATE_FORMAT(rq.offsetDateOut, '%M %d, %Y') as displayDateOut,
                    TIME_FORMAT(rq.offsetTimeOut, '%h:%i %p') as displayTimeOut,
                    rq.offsetMinutes,
                    rq.offsetReason,
                    rq.remarks,
                    rq.statusName,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(LEFT(req.middleName, 1), ''), '.') AS requestedByUser,
                    DATE_FORMAT(rq.dtAdded, '%M %d, %Y %h:%i %p') as dateRequested,
                    DATE_FORMAT(rq.dtLastModified, '%M %d, %Y %h:%i %p') as lastModified
                FROM rq_offset rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                WHERE rq.id = @Id AND rq.isActive = 1";

            return _db.QueryFirstOrDefault<OffsetCreditRequestPdfData>(sql, new { Id = id });
        }
    }
}