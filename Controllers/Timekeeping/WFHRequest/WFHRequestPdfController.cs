using Dapper;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.WFHRequest
{
    public class WFHRequestPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly WFHRequestPdfService _pdfService;

        // Constructor: Initializes database connection and PDF service via dependency injection
        public WFHRequestPdfController(IDbConnection db, WFHRequestPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // Generates and downloads WFH Request PDF file with timestamped filename
        [HttpGet]
        public IActionResult GeneratePdf(int id)
        {
            try
            {
                var data = GetWFHRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "WFH Request not found!" });

                var pdfBytes = _pdfService.GenerateWFHRequestPdf(data);
                var fileName = $"WFH_Request_{data.EmployeeNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF" });
            }
        }

        // Generates and displays WFH Request PDF in browser for preview
        [HttpGet]
        public IActionResult PreviewPdf(int id)
        {
            try
            {
                var data = GetWFHRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "WFH Request not found!" });

                var pdfBytes = _pdfService.GenerateWFHRequestPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF preview: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF preview" });
            }
        }

        // Retrieves WFH Request data from database with formatted dates and employee information
        private WFHRequestPdfData? GetWFHRequestData(int id)
        {
            var sql = @"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName, 1), ''), '.') AS employeeName,
                    DATE_FORMAT(rq.wfhDateIn, '%M %d, %Y') as displayDateIn,
                    TIME_FORMAT(rq.wfhTimeIn, '%h:%i %p') as displayTimeIn,
                    DATE_FORMAT(rq.wfhDateOut, '%M %d, %Y') as displayDateOut,
                    TIME_FORMAT(rq.wfhTimeOut, '%h:%i %p') as displayTimeOut,
                    rq.wfhReason,
                    rq.remarks,
                    rq.statusLevel4 AS statusName,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(LEFT(req.middleName, 1), ''), '.') AS requestedByUser,
                    DATE_FORMAT(rq.dtAdded, '%M %d, %Y %h:%i %p') as dateRequested,
                    DATE_FORMAT(rq.dtLastModified, '%M %d, %Y %h:%i %p') as lastModified
                FROM rq_workfromhome rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                WHERE rq.id = @Id AND rq.isActive = 1";

            return _db.QueryFirstOrDefault<WFHRequestPdfData>(sql, new { Id = id });
        }
    }
}