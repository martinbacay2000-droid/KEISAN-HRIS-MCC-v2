using Dapper;
using KEISAN_HRIS_v2.Services.TimeKeeping;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Timekeeping.ChangeScheduleRequest
{
    public class ChangeScheduleRequestPdfController : Controller
    {
        private readonly IDbConnection _db;
        private readonly ChangeScheduleRequestPdfService _pdfService;

        // Constructor: Initializes database connection and PDF service via dependency injection
        public ChangeScheduleRequestPdfController(IDbConnection db, ChangeScheduleRequestPdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        // Generates and downloads Change Schedule Request PDF file with timestamped filename
        [HttpGet]
        public IActionResult GeneratePdf(int id)
        {
            try
            {
                var data = GetChangeScheduleRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Change Schedule Request not found!" });

                var pdfBytes = _pdfService.GenerateChangeScheduleRequestPdf(data);
                var fileName = $"ChangeSchedule_Request_{data.EmployeeNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF" });
            }
        }

        // Generates and displays Change Schedule Request PDF in browser for preview
        [HttpGet]
        public IActionResult PreviewPdf(int id)
        {
            try
            {
                var data = GetChangeScheduleRequestData(id);
                if (data == null)
                    return NotFound(new { success = false, message = "Change Schedule Request not found!" });

                var pdfBytes = _pdfService.GenerateChangeScheduleRequestPdf(data);
                return File(pdfBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PDF preview: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Error generating PDF preview" });
            }
        }

        // Retrieves Change Schedule Request data from database with formatted dates and employee information
        private ChangeScheduleRequestPdfData? GetChangeScheduleRequestData(int id)
        {
            var sql = @"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(LEFT(b.middleName, 1), ''), '.') AS fullName,
                    DATE_FORMAT(rq.effectivityDate, '%M %d, %Y') AS displayEffectivityDate,
                    TIME_FORMAT(rq.timeIN, '%h:%i %p') AS displayTimeIn,
                    TIME_FORMAT(rq.timeOUT, '%h:%i %p') AS displayTimeOut,
                    rq.Reason,
                    rq.scheduleTypeCode,
                    ss.scheduleTypeName,
                    rq.remarks,
                    rq.statusLevel4 AS statusName,
                    CONCAT(req.lastName, ', ', req.firstName, ' ', IFNULL(LEFT(req.middleName, 1), ''), '.') AS requestedByUser,
                    DATE_FORMAT(rq.dtAdded, '%M %d, %Y %h:%i %p') AS dateRequested,
                    DATE_FORMAT(rq.dtLastModified, '%M %d, %Y %h:%i %p') AS lastModified
                FROM rq_changeschedule rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                LEFT JOIN s_scheduleType ss ON rq.scheduleTypeCode = ss.scheduleTypeCode
                LEFT JOIN e_basicinfo req ON req.employeeNo = rq.requestedByUser
                WHERE rq.id = @Id AND rq.isActive = 1";

            return _db.QueryFirstOrDefault<ChangeScheduleRequestPdfData>(sql, new { Id = id });
        }
    }
}