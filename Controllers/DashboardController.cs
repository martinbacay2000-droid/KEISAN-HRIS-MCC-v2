using KEISAN_HRIS_v2.Helpers;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

namespace KEISAN_HRIS_v2.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DashboardController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public DashboardController(
            IConfiguration configuration,
            ILogger<DashboardController> logger,
            IWebHostEnvironment webHostEnvironment)
        {
            _configuration = configuration;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        public IActionResult Index()
        {
            return View();
        }

        #region Profile Picture Methods

        [HttpPost]
        public async Task<IActionResult> UploadProfilePicture(IFormFile profilePicture)
        {
            try
            {
                if (profilePicture == null || profilePicture.Length == 0)
                {
                    return Json(new { success = false, message = "Please select a file to upload." });
                }

                // Validate file type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(fileExtension))
                {
                    return Json(new { success = false, message = "Only JPG and PNG files are allowed." });
                }

                // Validate file size (4MB = 4 * 1024 * 1024 bytes)
                var maxFileSize = 4 * 1024 * 1024;
                if (profilePicture.Length > maxFileSize)
                {
                    return Json(new { success = false, message = "File size must not exceed 4MB." });
                }

                var employeeNo = EmployeeNo;
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "profile-pictures");

                // Create directory if it doesn't exist
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Generate unique filename with employee number
                var fileName = $"{employeeNo}_{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                // Delete old profile picture if exists
                await DeleteOldProfilePicture(employeeNo);

                // Save the new file
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await profilePicture.CopyToAsync(fileStream);
                }

                // Save to database
                var relativePath = $"/uploads/profile-pictures/{fileName}";
                var saved = await SaveProfilePictureToDatabase(employeeNo, relativePath);

                if (saved)
                {
                    return Json(new
                    {
                        success = true,
                        message = "Profile picture uploaded successfully.",
                        profilePicturePath = relativePath
                    });
                }
                else
                {
                    // Delete the uploaded file if database save failed
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                    return Json(new { success = false, message = "Failed to save profile picture to database." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading profile picture");
                return Json(new { success = false, message = "An error occurred while uploading the profile picture." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveProfilePicture()
        {
            try
            {
                var employeeNo = EmployeeNo;

                // Delete physical file
                await DeleteOldProfilePicture(employeeNo);

                // Update database
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var query = @"
                    UPDATE e_profile 
                    SET profilePicturePath = NULL,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE employeeNo = @employeeNo AND isActive = 1";

                var result = await con.ExecuteAsync(query, new
                {
                    employeeNo = employeeNo,
                    lastModifiedByUser = EmployeeNo
                });

                if (result > 0)
                {
                    return Json(new { success = true, message = "Profile picture removed successfully." });
                }
                else
                {
                    return Json(new { success = false, message = "No profile picture found to remove." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing profile picture");
                return Json(new { success = false, message = "An error occurred while removing the profile picture." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadEmployeeProfilePicture(string targetEmployeeNo, IFormFile profilePicture)
        {
            try
            {
                if (string.IsNullOrEmpty(targetEmployeeNo))
                    return Json(new { success = false, message = "Invalid employee." });

                if (profilePicture == null || profilePicture.Length == 0)
                    return Json(new { success = false, message = "Please select a file to upload." });

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(fileExtension))
                    return Json(new { success = false, message = "Only JPG and PNG files are allowed." });

                var maxFileSize = 4 * 1024 * 1024;
                if (profilePicture.Length > maxFileSize)
                    return Json(new { success = false, message = "File size must not exceed 4MB." });

                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "profile-pictures");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{targetEmployeeNo}_{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                await DeleteOldProfilePicture(targetEmployeeNo);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await profilePicture.CopyToAsync(fileStream);
                }

                var relativePath = $"/uploads/profile-pictures/{fileName}";
                var saved = await SaveProfilePictureToDatabase(targetEmployeeNo, relativePath);

                if (saved)
                {
                    return Json(new
                    {
                        success = true,
                        message = "Profile picture uploaded successfully.",
                        profilePicturePath = relativePath
                    });
                }
                else
                {
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);

                    return Json(new { success = false, message = "Failed to save profile picture to database." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading employee profile picture by HR");
                return Json(new { success = false, message = "An error occurred while uploading the profile picture." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveEmployeeProfilePicture(string targetEmployeeNo)
        {
            try
            {
                if (string.IsNullOrEmpty(targetEmployeeNo))
                    return Json(new { success = false, message = "Invalid employee." });

                await DeleteOldProfilePicture(targetEmployeeNo);

                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var query = @"
                    UPDATE e_profile 
                    SET profilePicturePath = NULL,
                        dtLastModified = NOW(),
                        lastModifiedByUser = @lastModifiedByUser
                    WHERE employeeNo = @employeeNo AND isActive = 1";

                var result = await con.ExecuteAsync(query, new
                {
                    employeeNo = targetEmployeeNo,
                    lastModifiedByUser = EmployeeNo  // logged-in HR user
                });

                if (result > 0)
                    return Json(new { success = true, message = "Profile picture removed successfully." });
                else
                    return Json(new { success = false, message = "No profile picture found to remove." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing employee profile picture by HR");
                return Json(new { success = false, message = "An error occurred while removing the profile picture." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProfilePicture()
        {
            try
            {
                var employeeNo = EmployeeNo;

                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var query = @"
                    SELECT profilePicturePath 
                    FROM e_profile 
                    WHERE employeeNo = @employeeNo AND isActive = 1
                    LIMIT 1";

                var profilePicturePath = await con.QueryFirstOrDefaultAsync<string>(query, new { employeeNo });

                return Json(new
                {
                    success = true,
                    profilePicturePath = profilePicturePath ?? "/Fillow/images/user-profile.jpg" // Default image
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting profile picture");
                return Json(new
                {
                    success = true,
                    profilePicturePath = "/Fillow/images/user-profile.jpg"
                });
            }
        }

        [HttpGet]
        public IActionResult GetEmployeeProfilePicture(string employeeNo)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var query = @"
            SELECT profilePicturePath 
            FROM e_profile 
            WHERE employeeNo = @employeeNo AND isActive = 1
            LIMIT 1";

                var profilePicturePath = con.QueryFirstOrDefault<string>(query, new { employeeNo });

                return Json(new
                {
                    success = true,
                    profilePicturePath = profilePicturePath ?? "/Fillow/images/user-profile.jpg"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting profile picture");
                return Json(new
                {
                    success = true,
                    profilePicturePath = "/Fillow/images/user-profile.jpg"
                });
            }
        }

        private async Task<bool> SaveProfilePictureToDatabase(string employeeNo, string profilePicturePath)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                // Check if profile exists
                var checkQuery = "SELECT COUNT(*) FROM e_profile WHERE employeeNo = @employeeNo AND isActive = 1";
                var exists = await con.ExecuteScalarAsync<int>(checkQuery, new { employeeNo }) > 0;

                if (exists)
                {
                    // Update existing profile
                    var updateQuery = @"
                        UPDATE e_profile 
                        SET profilePicturePath = @profilePicturePath,
                            dtLastModified = NOW(),
                            lastModifiedByUser = @lastModifiedByUser
                        WHERE employeeNo = @employeeNo AND isActive = 1";

                    await con.ExecuteAsync(updateQuery, new
                    {
                        profilePicturePath,
                        employeeNo,
                        lastModifiedByUser = employeeNo
                    });
                }
                else
                {
                    // Insert new profile
                    var insertQuery = @"
                        INSERT INTO e_profile 
                        (employeeNo, profilePicturePath, isActive, dtAdded, addedByUser)
                        VALUES 
                        (@employeeNo, @profilePicturePath, 1, NOW(), @addedByUser)";

                    await con.ExecuteAsync(insertQuery, new
                    {
                        employeeNo,
                        profilePicturePath,
                        addedByUser = employeeNo
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving profile picture to database");
                return false;
            }
        }

        private async Task DeleteOldProfilePicture(string employeeNo)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var query = "SELECT profilePicturePath FROM e_profile WHERE employeeNo = @employeeNo AND isActive = 1";
                var oldPath = await con.QueryFirstOrDefaultAsync<string>(query, new { employeeNo });

                if (!string.IsNullOrEmpty(oldPath))
                {
                    var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, oldPath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting old profile picture");
            }
        }

        #endregion

        #region Dashboard Methods

        [HttpGet]
        public IActionResult GetDashboardCounts()
        {
            try
            {
                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                var isAdmin = RoleCode == "RL-000000";
                var employeeNo = EmployeeNo;

                var counts = new
                {
                    leave = GetRequestCounts(con, "rq_leave", employeeNo, isAdmin),
                    changeSchedule = GetRequestCounts(con, "rq_changeschedule", employeeNo, isAdmin),
                    officialBusiness = GetRequestCounts(con, "rq_officialbusiness", employeeNo, isAdmin),
                    cto = GetRequestCounts(con, "rq_cto", employeeNo, isAdmin),
                    offsetCredit = GetRequestCounts(con, "rq_offset", employeeNo, isAdmin),
                    overtime = GetRequestCounts(con, "rq_overtime", employeeNo, isAdmin),
                    undertime = GetRequestCounts(con, "rq_undertime", employeeNo, isAdmin),
                    workFromHome = GetRequestCounts(con, "rq_workfromhome", employeeNo, isAdmin)
                };

                return Json(new { success = true, data = counts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard counts");
                return Json(new { success = false, message = "Error loading dashboard data" });
            }
        }

        private object GetRequestCounts(MySqlConnection con, string table, string employeeNo, bool isAdmin)
        {
            var statusField = GetStatusField(table);
            var baseWhere = isAdmin ? "isActive = 1" : "isActive = 1 AND employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    SUM(CASE WHEN {statusField} = 'Pending' THEN 1 ELSE 0 END) as pending,
                    SUM(CASE WHEN {statusField} = 'Approved' THEN 1 ELSE 0 END) as approved,
                    SUM(CASE WHEN {statusField} = 'Declined' THEN 1 ELSE 0 END) as declined,
                    SUM(CASE WHEN {statusField} = 'Cancelled' THEN 1 ELSE 0 END) as cancelled
                FROM {table}
                WHERE {baseWhere}
            ";

            var result = con.QueryFirstOrDefault<dynamic>(query, new { employeeNo });

            return new
            {
                pending = (int)(result?.pending ?? 0),
                approved = (int)(result?.approved ?? 0),
                declined = (int)(result?.declined ?? 0),
                cancelled = (int)(result?.cancelled ?? 0)
            };
        }

        private string GetStatusField(string table)
        {
            return table switch
            {
                "rq_leave" => "statusLevel4",
                "rq_changeschedule" => "statusLevel4",
                "rq_officialbusiness" => "statusLevel4",
                "rq_cto" => "statusLevel4",
                "rq_offset" => "statusName",
                "rq_overtime" => "statusLevel4",
                "rq_undertime" => "statusName",
                "rq_workfromhome" => "statusLevel4",
                _ => "statusName"
            };
        }

        [HttpGet]
        public IActionResult GetRecentRequests(string requestType, int limit = 5)
        {
            try
            {
                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                var isAdmin = RoleCode == "RL-000000";
                var employeeNo = EmployeeNo;

                var requests = requestType switch
                {
                    "leave" => GetLeaveRequests(con, employeeNo, isAdmin, limit),
                    "changeSchedule" => GetChangeScheduleRequests(con, employeeNo, isAdmin, limit),
                    "officialBusiness" => GetOfficialBusinessRequests(con, employeeNo, isAdmin, limit),
                    "cto" => GetCTORequests(con, employeeNo, isAdmin, limit),
                    "offsetCredit" => GetOffsetCreditRequests(con, employeeNo, isAdmin, limit),
                    "overtime" => GetOvertimeRequests(con, employeeNo, isAdmin, limit),
                    "undertime" => GetUndertimeRequests(con, employeeNo, isAdmin, limit),
                    "workFromHome" => GetWFHRequests(con, employeeNo, isAdmin, limit),
                    _ => new List<dynamic>()
                };

                return Json(new { success = true, data = requests });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent requests");
                return Json(new { success = false, message = "Error loading requests" });
            }
        }

        private List<dynamic> GetLeaveRequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.leaveDateStart, '%m/%d/%Y') as dateStart,
                    DATE_FORMAT(rq.leaveDateEnd, '%m/%d/%Y') as dateEnd,
                    l.leaveDescription as leaveType,
                    rq.statusLevel4 AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_leave rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                JOIN s_leave l ON l.leaveCode = rq.leaveCode
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        private List<dynamic> GetChangeScheduleRequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.changeScheduleDate, '%m/%d/%Y') as scheduleDate,
                    TIME_FORMAT(rq.changeScheduleTimeIn, '%H:%i') as timeIn,
                    TIME_FORMAT(rq.changeScheduleTimeOut, '%H:%i') as timeOut,
                    rq.statusLevel4 AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_changeschedule rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        private List<dynamic> GetOfficialBusinessRequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.obDateStart, '%m/%d/%Y') as dateStart,
                    DATE_FORMAT(rq.obDateEnd, '%m/%d/%Y') as dateEnd,
                    rq.obPurpose as purpose,
                    rq.statusLevel4 AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_officialbusiness rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        private List<dynamic> GetCTORequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.overTimeDateIN, '%m/%d/%Y') as dateIn,
                    DATE_FORMAT(rq.overTimeDateOUT, '%m/%d/%Y') as dateOut,
                    rq.overTimeReason as reason,
                    rq.statusLevel4 AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_cto rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        private List<dynamic> GetOffsetCreditRequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.offsetDateIn, '%m/%d/%Y') as dateIn,
                    DATE_FORMAT(rq.offsetDateOut, '%m/%d/%Y') as dateOut,
                    rq.offsetMinutes as minutes,
                    rq.statusName AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_offset rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        private List<dynamic> GetOvertimeRequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.overTimeDateIN, '%m/%d/%Y') as dateIn,
                    DATE_FORMAT(rq.overTimeDateOUT, '%m/%d/%Y') as dateOut,
                    rq.overTimeReason as reason,
                    rq.statusLevel4 AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_overtime rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        private List<dynamic> GetUndertimeRequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.undertimeDateIN, '%m/%d/%Y') as dateIn,
                    DATE_FORMAT(rq.undertimeDateOUT, '%m/%d/%Y') as dateOut,
                    TIME_FORMAT(rq.undertimeTimeOUT, '%H:%i') as timeOut,
                    rq.statusName AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_undertime rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        private List<dynamic> GetWFHRequests(MySqlConnection con, string employeeNo, bool isAdmin, int limit)
        {
            var baseWhere = isAdmin ? "rq.isActive = 1" : "rq.isActive = 1 AND rq.employeeNo = @employeeNo";

            var query = $@"
                SELECT 
                    rq.id,
                    rq.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName) AS employeeName,
                    DATE_FORMAT(rq.wfhDateIn, '%m/%d/%Y') as dateIn,
                    DATE_FORMAT(rq.wfhDateOut, '%m/%d/%Y') as dateOut,
                    rq.wfhReason as reason,
                    rq.statusLevel4 AS status,
                    DATE_FORMAT(rq.dtAdded, '%m/%d/%Y %h:%i %p') as dateRequested
                FROM rq_workfromhome rq
                JOIN e_basicinfo b ON b.employeeNo = rq.employeeNo
                WHERE {baseWhere}
                ORDER BY rq.id DESC
                LIMIT @limit
            ";

            return con.Query<dynamic>(query, new { employeeNo, limit }).AsList();
        }

        [HttpGet]
        public IActionResult GetEmployeeSchedule()
        {
            try
            {
                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                var employeeNo = EmployeeNo;

                var query = @"
                    SELECT 
                        e.id,
                        e.employeeNo,
                        e.weekdayName,
                        TIME_FORMAT(e.timeIn, '%h:%i %p') as timeIn,
                        TIME_FORMAT(e.timeOut, '%h:%i %p') as timeOut,
                        e.totalRenderHour,
                        e.totalBreaktimeMinute,
                        e.scheduleTypeCode,
                        s.scheduleTypeName,
                        DATE_FORMAT(e.effectivityDate, '%m/%d/%Y') AS effectivityDate,
                        CONCAT(b.lastName, ', ', b.firstName) AS fullname
                    FROM e_schedule e
                    INNER JOIN (
                        -- Subquery: Get most recent effectivity date per weekday
                        SELECT 
                            weekdayName,
                            MAX(effectivityDate) as maxEffectivityDate
                        FROM e_schedule
                        WHERE employeeNo = @employeeNo
                        AND DATE(effectivityDate) <= CURDATE()
                        AND isActive = 1
                        GROUP BY weekdayName
                    ) latest ON e.weekdayName = latest.weekdayName 
                           AND e.effectivityDate = latest.maxEffectivityDate
                    LEFT JOIN e_basicinfo b ON b.employeeNo = e.employeeNo
                    LEFT JOIN s_scheduleType s ON s.scheduleTypeCode = e.scheduleTypeCode
                    WHERE e.employeeNo = @employeeNo 
                    AND e.isActive = 1
                    ORDER BY FIELD(e.weekdayName, 'Monday', 'Tuesday', 'Wednesday', 
                                  'Thursday', 'Friday', 'Saturday', 'Sunday')";

                var schedules = con.Query<dynamic>(query, new { employeeNo }).AsList();

                return Json(new { success = true, data = schedules });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting employee schedule");
                return Json(new { success = false, message = "Error loading schedule" });
            }
        }

        [HttpGet]
        public IActionResult GetEmployeeStatistics()
        {
            try
            {
                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                var statistics = new
                {
                    totalEmployees = GetTotalEmployees(con),
                    employeesByPosition = GetEmployeesByPosition(con),
                    employeesByRank = GetEmployeesByRank(con),
                    employeesByEmploymentStatus = GetEmployeesByEmploymentStatus(con),
                    employeesByDepartment = GetEmployeesByDepartment(con)
                };

                return Json(new { success = true, data = statistics });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting employee statistics");
                return Json(new { success = false, message = "Error loading employee statistics" });
            }
        }

        private int GetTotalEmployees(MySqlConnection con)
        {
            var query = @"
                SELECT COUNT(*) 
                FROM e_basicinfo 
                WHERE isActive = 1";

            return con.QueryFirstOrDefault<int>(query);
        }

        private List<dynamic> GetEmployeesByPosition(MySqlConnection con)
        {
            var query = @"
                SELECT 
                    COALESCE(p.positionName, 'Unassigned') as name,
                    COUNT(e.id) as value
                FROM e_basicinfo e
                LEFT JOIN s_position p ON p.positionCode = e.positionCode
                WHERE e.isActive = 1
                GROUP BY p.positionName
                ORDER BY value DESC
                LIMIT 10";

            return con.Query<dynamic>(query).AsList();
        }

        private List<dynamic> GetEmployeesByRank(MySqlConnection con)
        {
            var query = @"
                SELECT 
                    COALESCE(r.rankName, 'Unassigned') as name,
                    COUNT(e.id) as value
                FROM e_basicinfo e
                LEFT JOIN s_rank r ON r.rankCode = e.rankCode
                WHERE e.isActive = 1
                GROUP BY r.rankName
                ORDER BY value DESC";

            return con.Query<dynamic>(query).AsList();
        }

        private List<dynamic> GetEmployeesByEmploymentStatus(MySqlConnection con)
        {
            var query = @"
                SELECT 
                    COALESCE(es.employmentStatusName, 'Unassigned') as name,
                    COUNT(e.id) as value
                FROM e_basicinfo e
                LEFT JOIN s_employmentstatus es ON es.employmentStatusCode = e.employmentStatus
                WHERE e.isActive = 1
                GROUP BY es.employmentStatusName
                ORDER BY value DESC";

            return con.Query<dynamic>(query).AsList();
        }

        private List<dynamic> GetEmployeesByDepartment(MySqlConnection con)
        {
            var query = @"
                SELECT 
                    COALESCE(d.departmentName, 'Unassigned') as name,
                    COUNT(e.id) as value
                FROM e_basicinfo e
                LEFT JOIN s_department d ON d.departmentCode = e.departmentCode
                WHERE e.isActive = 1
                GROUP BY d.departmentName
                ORDER BY value DESC
                LIMIT 10";

            return con.Query<dynamic>(query).AsList();
        }

        #endregion

        #region Notification Methods

        [HttpGet]
        public IActionResult GetNotificationCount()
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                var employeeNo = EmployeeNo;

                var query = @"
            SELECT COUNT(*) 
            FROM s_notification 
            WHERE recipientEmployeeNo = @employeeNo 
            AND isRead = 0 
            AND isActive = 1";

                var count = con.QueryFirstOrDefault<int>(query, new { employeeNo });

                return Json(new { success = true, unreadCount = count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification count");
                return Json(new { success = false, unreadCount = 0 });
            }
        }

        [HttpGet]
        public IActionResult GetNotifications(bool isUnread = true)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                var employeeNo = EmployeeNo;

                var query = @"
                    SELECT 
                        n.id,
                        n.notificationCode,
                        n.recipientEmployeeNo,
                        n.requestType,
                        n.requestId,
                        n.requestorEmployeeNo,
                        n.actionType,
                        n.message,
                        n.isRead,
                        n.dtCreated,
                        n.dtRead,
                        CONCAT(e.lastName, ', ', e.firstName) as requestorName,
                        CASE 
                            WHEN n.actionType = 'pending' THEN 'Pending Request'
                            WHEN n.actionType = 'approved' THEN 'Request Approved'
                            WHEN n.actionType = 'declined' THEN 'Request Declined'
                            WHEN n.actionType = 'cancelled' THEN 'Request Cancelled'
                            WHEN n.actionType = 'probationary_alert' THEN 'Probationary Alert'
                            ELSE 'Notification'
                        END as title
                    FROM s_notification n
                    LEFT JOIN e_basicinfo e ON e.employeeNo = n.requestorEmployeeNo
                    WHERE n.recipientEmployeeNo = @employeeNo 
                    AND n.isRead = @isRead
                    AND n.isActive = 1
                    ORDER BY n.dtCreated DESC";

                var notifications = con.Query<dynamic>(query, new
                {
                    employeeNo,
                    isRead = !isUnread
                }).AsList();

                // Get counts
                var unreadCount = con.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) 
                    FROM s_notification 
                    WHERE recipientEmployeeNo = @employeeNo 
                    AND isRead = 0 
                    AND isActive = 1", new { employeeNo });

                var readCount = con.QueryFirstOrDefault<int>(@"
                    SELECT COUNT(*) 
                    FROM s_notification 
                    WHERE recipientEmployeeNo = @employeeNo 
                    AND isRead = 1 
                    AND isActive = 1", new { employeeNo });

                return Json(new
                {
                    success = true,
                    data = notifications,
                    unreadCount = unreadCount,
                    readCount = readCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications");
                return Json(new { success = false, message = "Error loading notifications" });
            }
        }

        [HttpPost]
        public IActionResult MarkNotificationAsRead(int notificationId)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                var employeeNo = EmployeeNo;

                var query = @"
                    UPDATE s_notification 
                    SET isRead = 1, 
                        dtRead = NOW() 
                    WHERE id = @notificationId 
                    AND recipientEmployeeNo = @employeeNo 
                    AND isActive = 1";

                con.Execute(query, new { notificationId, employeeNo });

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification as read");
                return Json(new { success = false });
            }
        }

        [HttpPost]
        public IActionResult MarkAllNotificationsAsRead()
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                var employeeNo = EmployeeNo;

                var query = @"
                    UPDATE s_notification 
                    SET isRead = 1, 
                        dtRead = NOW() 
                    WHERE recipientEmployeeNo = @employeeNo 
                    AND isRead = 0 
                    AND isActive = 1";

                con.Execute(query, new { employeeNo });

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read");
                return Json(new { success = false });
            }
        }

        /// <summary>
        /// Create a notification for a request action
        /// This should be called whenever a request status changes
        /// </summary>
        public void CreateNotification(
            string recipientEmployeeNo,
            string requestType,
            int requestId,
            string requestorEmployeeNo,
            string actionType,
            string customMessage = null)
        {
            try
            {
                using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var message = customMessage ?? GenerateNotificationMessage(requestType, actionType, requestorEmployeeNo);
                var notificationCode = $"NOTIF-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 8)}";

                var query = @"
            INSERT INTO s_notification 
            (notificationCode, recipientEmployeeNo, requestType, requestId, 
             requestorEmployeeNo, actionType, message, isRead, dtCreated, isActive)
            VALUES 
            (@notificationCode, @recipientEmployeeNo, @requestType, @requestId, 
             @requestorEmployeeNo, @actionType, @message, 0, NOW(), 1)";

                con.Execute(query, new
                {
                    notificationCode,
                    recipientEmployeeNo,
                    requestType,
                    requestId,
                    requestorEmployeeNo,
                    actionType,
                    message
                });

                _logger.LogInformation(
                    "Notification created: Type={RequestType}, Action={ActionType}, Recipient={RecipientEmployeeNo}",
                    requestType, actionType, recipientEmployeeNo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification");
            }
        }

        private string GenerateNotificationMessage(string requestType, string actionType, string requestorEmployeeNo)
        {
            using var con = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

            var requestorName = con.QueryFirstOrDefault<string>(
                "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                new { employeeNo = requestorEmployeeNo }) ?? "An employee";

            var requestTypeDisplay = GetRequestTypeDisplay(requestType);

            return actionType switch
            {
                "pending" => $"{requestorName} submitted a {requestTypeDisplay} request that requires your approval.",
                "approved" => $"Your {requestTypeDisplay} request has been approved.",
                "declined" => $"Your {requestTypeDisplay} request has been declined.",
                "cancelled" => $"Your {requestTypeDisplay} request has been cancelled.",
                _ => $"Status update for your {requestTypeDisplay} request."
            };
        }

        private string GetRequestTypeDisplay(string requestType)
        {
            return requestType switch
            {
                "leave" => "Leave",
                "changeSchedule" => "Change Schedule",
                "officialBusiness" => "Official Business",
                "cto" => "CTO",
                "offsetCredit" => "Offset Credit",
                "overtime" => "Overtime",
                "undertime" => "Undertime",
                "workFromHome" => "Work From Home",
                _ => "Request"
            };
        }

        [HttpGet]
        public IActionResult GetMyCommendations()
        {
            try
            {
                using var con = new MySqlConnection(
                    _configuration.GetConnectionString("DefaultConnection"));

                var sql = @"
                    SELECT 
                        c.id,
                        c.activity,
                        DATE_FORMAT(c.dateissued, '%m/%d/%Y') AS dateissued,
                        c.addedby AS issuedBy,
                        IFNULL(s.commendationName, '') AS commendationType
                    FROM e_commendation c
                    LEFT JOIN s_commendation s ON s.commendationCode = c.commendationCode
                    WHERE c.employeeNo = @employeeNo
                      AND c.isActive = 1
                    ORDER BY c.dateissued DESC";

                var commendations = con.Query<dynamic>(sql, new { employeeNo = EmployeeNo }).AsList();

                return Json(new { success = true, data = commendations });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting employee commendations");
                return Json(new { success = false, message = "Error loading commendations" });
            }
        }

        #endregion

        [HttpGet]
        public async Task<IActionResult> GetPendingMemoNotifications()
        {
            try
            {
                var db = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var notifications = await db.QueryAsync<dynamic>(@"
                    SELECT
                        n.id            AS notificationId,
                        n.requestId     AS memoId,
                        n.message,
                        n.dtCreated,
                        m.seriesNo,
                        m.title,
                        m.remarks,
                        m.createdBy,
                        DATE_FORMAT(m.effectivityDate, '%Y-%m-%d') AS effectivityDateFormatted,
                        DATE_FORMAT(m.dtAdded, '%M %d, %Y')        AS dateAdded
                    FROM s_notification n
                    INNER JOIN ad_memo m ON m.id = n.requestId AND m.isActive = 1
                    WHERE n.recipientEmployeeNo = @employeeNo
                      AND n.requestType         = 'memo'
                      AND n.actionType          = 'new_memo'
                      AND n.isRead              = 0
                      AND n.isActive            = 1
                      AND m.dtAdded            >= DATE_SUB(NOW(), INTERVAL 7 DAY)
                    ORDER BY n.dtCreated DESC",
                    new { employeeNo = EmployeeNo });

                return Json(new { success = true, data = notifications.ToList() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPendingMemoNotifications");
                return Json(new { success = false, data = new List<object>() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkMemoReceived(int notificationId)
        {
            try
            {
                if (notificationId <= 0)
                    return Json(new { success = false, message = "Invalid notification ID." });

                using var db = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                await db.ExecuteAsync(@"
                    UPDATE s_notification
                    SET isRead = 1,
                        dtRead = NOW()
                    WHERE id                  = @notificationId
                      AND recipientEmployeeNo = @employeeNo
                      AND requestType         = 'memo'
                      AND isActive            = 1",
                    new { notificationId, employeeNo = EmployeeNo });

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MarkMemoReceived");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMemoAttachments(int memoId)
        {
            if (memoId <= 0)
                return Json(new List<object>());

            try
            {
                using var db = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));

                var attachments = (await db.QueryAsync<dynamic>(@"
                    SELECT id, attachmentPath, dtAdded
                    FROM e_attachment
                    WHERE employeeNo         = @memoId
                      AND attachmentTypeCode = 'MEMO'
                      AND isActive           = 1
                    ORDER BY dtAdded DESC",
                    new { memoId = memoId.ToString() })).ToList();

                return Json(attachments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetMemoAttachments");
                return Json(new List<object>());
            }
        }
    }
}