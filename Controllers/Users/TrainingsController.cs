using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Globalization;

namespace KEISAN_HRIS_v2.Controllers.Trainings
{
    [ModuleAuthorize("FSTrainingsM")]
    public class TrainingsController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public TrainingsController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetTrainings(string employeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(employeeNo))
                {
                    return PartialView("~/Views/Users/Partials/_Trainings.cshtml",
                        new List<TrainingsInfo>());
                }

                var employeeName = _db.QueryFirstOrDefault<string>(
                    @"SELECT CONCAT(lastName, ', ', firstName, ' ', COALESCE(middleName, '')) 
                      FROM e_basicinfo WHERE employeeNo = @EmployeeNo",
                    new { EmployeeNo = employeeNo });

                var trainings = GetTrainingsData(employeeNo, false);

                ViewBag.EmployeeNo = employeeNo;
                ViewBag.EmployeeName = employeeName ?? "Unknown Employee";

                return PartialView("~/Views/Users/Partials/_Trainings.cshtml", trainings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTrainings: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_Trainings.cshtml",
                    new List<TrainingsInfo>());
            }
        }

        [HttpGet]
        public JsonResult GetTrainingsList(string employeeNo, string isactive)
        {
            try
            {
                // Convert isactive parameter: "2" means all, "1" means active, "0" means inactive
                bool? activeFilter = isactive == "2" ? null : isactive == "1";
                var trainings = GetTrainingsData(employeeNo, false, activeFilter);
                return Json(new { data = trainings });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTrainingsList: {ex.Message}");
                return Json(new { data = new List<TrainingsInfo>() });
            }
        }

        [HttpGet]
        public JsonResult GetTrainingById(int id)
        {
            try
            {
                var sql = BuildTrainingsQuery("WHERE t.id = @Id");
                var training = _db.QueryFirstOrDefault<TrainingsInfo>(sql, new { Id = id });

                return training != null
                    ? Json(new { success = true, data = training })
                    : Json(new { success = false, message = "Training record not found." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTrainingById: {ex.Message}");
                return Json(new { success = false, message = "Error retrieving training: " + ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetDeletedTrainings(string employeeNo)
        {
            try
            {
                var trainings = GetTrainingsData(employeeNo, true);
                return Json(new { data = trainings });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDeletedTrainings: {ex.Message}");
                return Json(new { data = new List<TrainingsInfo>() });
            }
        }

        [HttpPost]
        public JsonResult SaveTraining([FromBody] TrainingsDto model)
        {
            try
            {
                if (!ValidateTraining(model, out string validationMessage))
                {
                    return Json(new { success = false, message = validationMessage });
                }

                if (!ProcessDates(model, out DateTime fromDate, out DateTime toDate, out string dateError))
                {
                    return Json(new { success = false, message = dateError });
                }

                // Validate date range
                if (toDate < fromDate)
                {
                    return Json(new { success = false, message = "End date cannot be earlier than start date." });
                }

                if (model.Id.HasValue && model.Id > 0)
                {
                    return UpdateTraining(model, fromDate, toDate);
                }
                else
                {
                    return InsertTraining(model, fromDate, toDate);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveTraining: {ex.Message}");
                return Json(new { success = false, message = "Error saving training: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult InactiveTraining(int id, string remarks = "")
        {
            try
            {
                if (!RecordExists(id))
                {
                    return Json(new { success = false, message = "Training record not found or already deleted!" });
                }

                var sql = @"
                    UPDATE e_training 
                    SET dtStatus = NOW(), 
                        isActive = 0, 
                        statusByUser = @DeletedByUser
                    WHERE id = @Id";

                var parameters = new
                {
                    Id = id,
                    DeletedByUser = EmployeeNo
                };

                var rowsAffected = _db.Execute(sql, parameters);

                if (rowsAffected > 0)
                {
                    var auditMessage = string.IsNullOrWhiteSpace(remarks)
                        ? "Training soft deleted"
                        : $"Training soft deleted. Reason: {remarks}";

                    _auditTrail.Log("e_training", id, "DELETED", auditMessage);
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Training deleted successfully!" })
                    : Json(new { success = false, message = "Failed to delete training." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InactiveTraining: {ex.Message}");
                return Json(new { success = false, message = "Error deleting training: " + ex.Message });
            }
        }

        [HttpPost]
        public JsonResult RestoreTraining(int id)
        {
            try
            {
                var existingRecord = _db.QueryFirstOrDefault<TrainingsInfo>(
                    "SELECT * FROM e_training WHERE id = @Id AND isActive = 0",
                    new { Id = id });

                if (existingRecord == null)
                {
                    return Json(new { success = false, message = "Training record not found or not deleted!" });
                }

                var sql = @"
                    UPDATE e_training 
                    SET isActive = 1, 
                        dtStatus = NOW(),
                        statusByUser = @RestoredByUser
                    WHERE id = @Id";

                var rowsAffected = _db.Execute(sql, new
                {
                    Id = id,
                    RestoredByUser = EmployeeNo
                });

                if (rowsAffected > 0)
                {
                    _auditTrail.Log("e_training", id, "RESTORED", "Training restored");
                }

                return rowsAffected > 0
                    ? Json(new { success = true, message = "Training restored successfully!" })
                    : Json(new { success = false, message = "Failed to restore training." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreTraining: {ex.Message}");
                return Json(new { success = false, message = "Error restoring training: " + ex.Message });
            }
        }

        // HELPER METHODS

        private bool ValidateTraining(TrainingsDto model, out string message)
        {
            message = string.Empty;

            if (model == null)
            {
                message = "Invalid data provided.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.EmployeeNo))
            {
                message = "Employee number is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.TrainingTitle))
            {
                message = "Training title is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.DateFrom))
            {
                message = "From date is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.DateTo))
            {
                message = "To date is required.";
                return false;
            }

            return true;
        }

        private bool ProcessDates(TrainingsDto model, out DateTime fromDate, out DateTime toDate, out string errorMessage)
        {
            fromDate = DateTime.MinValue;
            toDate = DateTime.MinValue;
            errorMessage = string.Empty;

            // Try multiple date formats
            string[] formats = { "yyyy/MM/dd", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy" };

            if (!DateTime.TryParseExact(model.DateFrom, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out fromDate))
            {
                errorMessage = "Invalid from date format. Expected format: yyyy/MM/dd";
                return false;
            }

            if (!DateTime.TryParseExact(model.DateTo, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out toDate))
            {
                errorMessage = "Invalid to date format. Expected format: yyyy/MM/dd";
                return false;
            }

            return true;
        }

        private JsonResult UpdateTraining(TrainingsDto model, DateTime fromDate, DateTime toDate)
        {
            var existingRecord = _db.QueryFirstOrDefault<TrainingsInfo>(
                "SELECT * FROM e_training WHERE id = @Id AND isActive = 1",
                new { Id = model.Id });

            if (existingRecord == null)
            {
                return Json(new { success = false, message = "Training record not found or has been deleted!" });
            }

            var sql = @"
                UPDATE e_training
                SET trainingTitle = @TrainingTitle,
                    trainingProvider = @TrainingProvider,
                    trainingVenue = @TrainingVenue,
                    dateFrom = @DateFrom,
                    dateTo = @DateTo,
                    remarks = @Remarks,
                    dtStatus = NOW(),
                    statusByUser = @ModifiedByUser
                WHERE id = @Id";

            var rowsAffected = _db.Execute(sql, new
            {
                Id = model.Id,
                TrainingTitle = model.TrainingTitle,
                TrainingProvider = model.TrainingProvider ?? string.Empty,
                TrainingVenue = model.TrainingVenue ?? string.Empty,
                DateFrom = fromDate.ToString("yyyy-MM-dd"),
                DateTo = toDate.ToString("yyyy-MM-dd"),
                Remarks = model.Remarks ?? string.Empty,
                ModifiedByUser = EmployeeNo
            });

            if (rowsAffected > 0)
            {
                _auditTrail.Log("e_training", model.Id.Value, "UPDATED",
                    $"Updated training: {model.TrainingTitle} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Training updated successfully!" });
            }

            return Json(new { success = false, message = "Failed to update training." });
        }

        private JsonResult InsertTraining(TrainingsDto model, DateTime fromDate, DateTime toDate)
        {
            var sql = @"
                INSERT INTO e_training (
                    employeeNo, trainingTitle, trainingProvider, trainingVenue, 
                    dateFrom, dateTo, remarks, isActive, dtAdded, addedByUser
                )
                VALUES (
                    @EmployeeNo, @TrainingTitle, @TrainingProvider, @TrainingVenue,
                    @DateFrom, @DateTo, @Remarks, 1, NOW(), @AddedByUser
                );
                SELECT LAST_INSERT_ID();";

            int newId = _db.QuerySingle<int>(sql, new
            {
                EmployeeNo = model.EmployeeNo,
                TrainingTitle = model.TrainingTitle,
                TrainingProvider = model.TrainingProvider ?? string.Empty,
                TrainingVenue = model.TrainingVenue ?? string.Empty,
                DateFrom = fromDate.ToString("yyyy-MM-dd"),
                DateTo = toDate.ToString("yyyy-MM-dd"),
                Remarks = model.Remarks ?? string.Empty,
                AddedByUser = EmployeeNo
            });

            if (newId > 0)
            {
                _auditTrail.Log("e_training", newId, "CREATED",
                    $"Added training: {model.TrainingTitle} - Employee: {model.EmployeeNo}");

                return Json(new { success = true, message = "Training added successfully!" });
            }

            return Json(new { success = false, message = "Failed to add training." });
        }

        private List<TrainingsInfo> GetTrainingsData(string employeeNo, bool isDeleted, bool? isActiveFilter = null)
        {
            string whereClause;

            if (isDeleted)
            {
                whereClause = "WHERE t.employeeNo = @EmployeeNo AND t.isActive = 0";
            }
            else if (isActiveFilter.HasValue)
            {
                whereClause = isActiveFilter.Value
                    ? "WHERE t.employeeNo = @EmployeeNo AND t.isActive = 1"
                    : "WHERE t.employeeNo = @EmployeeNo AND t.isActive = 0";
            }
            else
            {
                whereClause = "WHERE t.employeeNo = @EmployeeNo AND t.isActive = 1";
            }

            var sql = BuildTrainingsQuery(whereClause);
            return _db.Query<TrainingsInfo>(sql, new { EmployeeNo = employeeNo }).ToList();
        }

        private string BuildTrainingsQuery(string whereClause)
        {
            return $@"
                SELECT 
                    t.id,
                    t.employeeNo,
                    t.trainingTitle,
                    t.trainingProvider,
                    t.trainingVenue,
                    DATE_FORMAT(t.dateFrom, '%Y/%m/%d') AS dateFrom,
                    DATE_FORMAT(t.dateTo, '%Y/%m/%d') AS dateTo,
                    t.remarks,
                    t.isActive,
                    DATE_FORMAT(t.dtAdded, '%Y/%m/%d') AS dtAdded,
                    CONCAT(COALESCE(u.lastName, ''), ', ', COALESCE(u.firstName, '')) AS addedByUser,
                    t.dtStatus,
                    t.statusByUser
                FROM e_training t
                LEFT JOIN s_user u ON u.userCode = t.addedByUser
                {whereClause}
                ORDER BY t.dateFrom DESC, t.id DESC";
        }

        private bool RecordExists(int id)
        {
            var record = _db.QueryFirstOrDefault<TrainingsInfo>(
                "SELECT * FROM e_training WHERE id = @Id AND isActive = 1",
                new { Id = id });

            return record != null;
        }
    }
}