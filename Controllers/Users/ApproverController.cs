using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("approverM")]
    public class ApproverController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public ApproverController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public JsonResult GetApproverList(string searchTerm = "")
        {
            try
            {
                var sql = @"
                    SELECT 
                        employeeNo,
                        CONCAT(lastName, ', ', firstName) AS employeeName
                    FROM e_basicinfo 
                    WHERE isActive = 1";

                var parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    sql += " AND (CONCAT(lastName, ', ', firstName) LIKE @searchTerm OR employeeNo LIKE @searchTerm)";
                    parameters.Add("@searchTerm", $"%{searchTerm}%");
                }

                sql += " ORDER BY lastName, firstName LIMIT 50";

                var approverList = _db.Query(sql, parameters).ToList();
                return Json(approverList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetApproverList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetEmploymentStatusList()
        {
            try
            {
                var sql = @"
                    SELECT DISTINCT
                        employmentStatus AS value,
                        employmentStatus AS text
                    FROM e_basicinfo 
                    WHERE isActive = 1 
                    AND employmentStatus IS NOT NULL
                    ORDER BY employmentStatus";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmploymentStatusList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetBranchList()
        {
            try
            {
                var sql = @"
                    SELECT 
                        branchCode AS value,
                        branchName AS text
                    FROM s_branch 
                    WHERE isActive = 1
                    ORDER BY branchName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBranchList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetDepartmentList()
        {
            try
            {
                var sql = @"
                    SELECT 
                        departmentCode AS value,
                        departmentName AS text
                    FROM s_department 
                    WHERE isActive = 1
                    ORDER BY departmentName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDepartmentList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetRankList()
        {
            try
            {
                var sql = @"
                    SELECT 
                        positionCode AS value,
                        positionName AS text
                    FROM s_position 
                    WHERE isActive = 1
                    ORDER BY positionName";

                return Json(_db.Query(sql).ToList());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetRankList: {ex.Message}");
                return Json(new List<object>());
            }
        }

        [HttpGet]
        public JsonResult GetAvailableEmployees(string approverNo, int approverLevel, int listType)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(approverNo))
                    return Json(new { data = new List<object>() });

                var sql = new StringBuilder();

                // Build query based on list type
                switch (listType)
                {
                    case 1: // Employment Status
                        sql.Append(@"
                            SELECT DISTINCT
                                e.employmentStatus AS code,
                                e.employmentStatus AS name,
                                COUNT(e.employeeNo) AS employeeCount
                            FROM e_basicinfo e
                            WHERE e.isActive = 1
                            AND e.employmentStatus NOT IN (
                                SELECT a.employeeNo
                                FROM e_approver a
                                WHERE a.approverNo = @approverNo
                                AND a.approverLevel = @approverLevel
                                AND a.typeList = @typeList
                                AND a.isActive = 1
                            )
                            GROUP BY e.employmentStatus
                            ORDER BY e.employmentStatus");
                        break;

                    case 2: // Branch
                        sql.Append(@"
                            SELECT 
                                b.branchCode AS code,
                                b.branchName AS name,
                                (SELECT COUNT(*) FROM e_basicinfo WHERE branchCode = b.branchCode AND isActive = 1) AS employeeCount
                            FROM s_branch b
                            WHERE b.isActive = 1
                            AND b.branchCode NOT IN (
                                SELECT a.employeeNo
                                FROM e_approver a
                                WHERE a.approverNo = @approverNo
                                AND a.approverLevel = @approverLevel
                                AND a.typeList = @typeList
                                AND a.isActive = 1
                            )
                            ORDER BY b.branchName");
                        break;

                    case 3: // Department
                        sql.Append(@"
                            SELECT 
                                d.departmentCode AS code,
                                d.departmentName AS name,
                                (SELECT COUNT(*) FROM e_basicinfo WHERE departmentCode = d.departmentCode AND isActive = 1) AS employeeCount
                            FROM s_department d
                            WHERE d.isActive = 1
                            AND d.departmentCode NOT IN (
                                SELECT a.employeeNo
                                FROM e_approver a
                                WHERE a.approverNo = @approverNo
                                AND a.approverLevel = @approverLevel
                                AND a.typeList = @typeList
                                AND a.isActive = 1
                            )
                            ORDER BY d.departmentName");
                        break;

                    case 4: // Rank/Position
                        sql.Append(@"
                            SELECT 
                                p.positionCode AS code,
                                p.positionName AS name,
                                (SELECT COUNT(*) FROM e_basicinfo WHERE positionCode = p.positionCode AND isActive = 1) AS employeeCount
                            FROM s_position p
                            WHERE p.isActive = 1
                            AND p.positionCode NOT IN (
                                SELECT a.employeeNo
                                FROM e_approver a
                                WHERE a.approverNo = @approverNo
                                AND a.approverLevel = @approverLevel
                                AND a.typeList = @typeList
                                AND a.isActive = 1
                            )
                            ORDER BY p.positionName");
                        break;

                    case 6: // Individual Employee
                        sql.Append(@"
                            SELECT 
                                e.employeeNo AS code,
                                CONCAT(e.lastName, ', ', e.firstName, ' ', COALESCE(e.middleName, '')) AS name,
                                NULL AS employeeCount
                            FROM e_basicinfo e
                            WHERE e.isActive = 1
                            AND e.employeeNo NOT IN (
                                SELECT a.employeeNo
                                FROM e_approver a
                                WHERE a.approverNo = @approverNo
                                AND a.approverLevel = @approverLevel
                                AND a.typeList = @typeList
                                AND a.isActive = 1
                            )
                            ORDER BY e.lastName, e.firstName");
                        break;

                    default:
                        return Json(new { data = new List<object>() });
                }

                var parameters = new { approverNo, approverLevel, typeList = listType };
                var availableList = _db.Query<dynamic>(sql.ToString(), parameters).ToList();

                return Json(new { data = availableList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAvailableEmployees: {ex.Message}");
                return Json(new { data = new List<object>() });
            }
        }

        [HttpGet]
        public JsonResult GetAssignedEmployees(string approverNo, int approverLevel, int listType)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(approverNo))
                    return Json(new { data = new List<object>() });

                var sql = new StringBuilder();

                // Build query based on list type
                switch (listType)
                {
                    case 1: // Employment Status
                        sql.Append(@"
                            SELECT 
                                a.id,
                                a.employeeNo AS code,
                                a.employeeNo AS name,
                                (SELECT COUNT(*) FROM e_basicinfo WHERE employmentStatus = a.employeeNo AND isActive = 1) AS employeeCount
                            FROM e_approver a
                            WHERE a.approverNo = @approverNo
                            AND a.approverLevel = @approverLevel
                            AND a.typeList = @typeList
                            AND a.isActive = 1
                            ORDER BY a.employeeNo");
                        break;

                    case 2: // Branch
                        sql.Append(@"
                            SELECT 
                                a.id,
                                a.employeeNo AS code,
                                b.branchName AS name,
                                (SELECT COUNT(*) FROM e_basicinfo WHERE branchCode = a.employeeNo AND isActive = 1) AS employeeCount
                            FROM e_approver a
                            LEFT JOIN s_branch b ON b.branchCode = a.employeeNo
                            WHERE a.approverNo = @approverNo
                            AND a.approverLevel = @approverLevel
                            AND a.typeList = @typeList
                            AND a.isActive = 1
                            ORDER BY b.branchName");
                        break;

                    case 3: // Department
                        sql.Append(@"
                            SELECT 
                                a.id,
                                a.employeeNo AS code,
                                d.departmentName AS name,
                                (SELECT COUNT(*) FROM e_basicinfo WHERE departmentCode = a.employeeNo AND isActive = 1) AS employeeCount
                            FROM e_approver a
                            LEFT JOIN s_department d ON d.departmentCode = a.employeeNo
                            WHERE a.approverNo = @approverNo
                            AND a.approverLevel = @approverLevel
                            AND a.typeList = @typeList
                            AND a.isActive = 1
                            ORDER BY d.departmentName");
                        break;

                    case 4: // Rank/Position
                        sql.Append(@"
                            SELECT 
                                a.id,
                                a.employeeNo AS code,
                                p.positionName AS name,
                                (SELECT COUNT(*) FROM e_basicinfo WHERE positionCode = a.employeeNo AND isActive = 1) AS employeeCount
                            FROM e_approver a
                            LEFT JOIN s_position p ON p.positionCode = a.employeeNo
                            WHERE a.approverNo = @approverNo
                            AND a.approverLevel = @approverLevel
                            AND a.typeList = @typeList
                            AND a.isActive = 1
                            ORDER BY p.positionName");
                        break;

                    case 6: // Individual Employee
                        sql.Append(@"
                            SELECT 
                                a.id,
                                a.employeeNo AS code,
                                CONCAT(e.lastName, ', ', e.firstName, ' ', COALESCE(e.middleName, '')) AS name,
                                NULL AS employeeCount
                            FROM e_approver a
                            LEFT JOIN e_basicinfo e ON e.employeeNo = a.employeeNo
                            WHERE a.approverNo = @approverNo
                            AND a.approverLevel = @approverLevel
                            AND a.typeList = @typeList
                            AND a.isActive = 1
                            ORDER BY e.lastName, e.firstName");
                        break;

                    default:
                        return Json(new { data = new List<object>() });
                }

                var parameters = new { approverNo, approverLevel, typeList = listType };
                var assignedList = _db.Query<dynamic>(sql.ToString(), parameters).ToList();

                return Json(new { data = assignedList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAssignedEmployees: {ex.Message}");
                return Json(new { data = new List<object>() });
            }
        }

        [HttpGet]
        public JsonResult GetIfCheckedAll(string approverNo, int approverLevel)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(approverNo))
                    return Json(new { isAll = false });

                var query = @"
                    SELECT COUNT(*) 
                    FROM e_approver 
                    WHERE approverNo = @approverNo 
                    AND approverLevel = @approverLevel 
                    AND employeeNo = 'ALL' 
                    AND typeList = 5
                    AND isActive = 1";

                int count = _db.ExecuteScalar<int>(query, new { approverNo, approverLevel });
                return Json(new { isAll = count > 0 });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetIfCheckedAll: {ex.Message}");
                return Json(new { isAll = false, error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetApproverSummary()
        {
            try
            {
                var sql = @"
                    SELECT 
                        a.id,
                        a.approverNo,
                        CONCAT(e.lastName, ', ', e.firstName) AS approverName,
                        a.approverLevel,
                        a.typeList,
                        CASE 
                            WHEN a.typeList = 1 THEN a.employeeNo
                            WHEN a.typeList = 2 THEN b.branchName
                            WHEN a.typeList = 3 THEN d.departmentName
                            WHEN a.typeList = 4 THEN p.positionName
                            WHEN a.typeList = 5 THEN 'ALL EMPLOYEES'
                            WHEN a.typeList = 6 THEN CONCAT(emp.lastName, ', ', emp.firstName)
                            ELSE a.employeeNo
                        END AS assignedTo,
                        CASE 
                            WHEN a.typeList = 1 THEN (SELECT COUNT(*) FROM e_basicinfo WHERE employmentStatus = a.employeeNo AND isActive = 1)
                            WHEN a.typeList = 2 THEN (SELECT COUNT(*) FROM e_basicinfo WHERE branchCode = a.employeeNo AND isActive = 1)
                            WHEN a.typeList = 3 THEN (SELECT COUNT(*) FROM e_basicinfo WHERE departmentCode = a.employeeNo AND isActive = 1)
                            WHEN a.typeList = 4 THEN (SELECT COUNT(*) FROM e_basicinfo WHERE positionCode = a.employeeNo AND isActive = 1)
                            WHEN a.typeList = 5 THEN (SELECT COUNT(*) FROM e_basicinfo WHERE isActive = 1)
                            WHEN a.typeList = 6 THEN 1
                            ELSE 0
                        END AS employeeCount
                    FROM e_approver a
                    LEFT JOIN e_basicinfo e ON e.employeeNo = a.approverNo
                    LEFT JOIN s_branch b ON b.branchCode = a.employeeNo AND a.typeList = 2
                    LEFT JOIN s_department d ON d.departmentCode = a.employeeNo AND a.typeList = 3
                    LEFT JOIN s_position p ON p.positionCode = a.employeeNo AND a.typeList = 4
                    LEFT JOIN e_basicinfo emp ON emp.employeeNo = a.employeeNo AND a.typeList = 6
                    WHERE a.isActive = 1
                    ORDER BY a.approverNo, a.approverLevel, a.typeList";

                var summaryList = _db.Query<dynamic>(sql).ToList();
                return Json(new { data = summaryList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetApproverSummary: {ex.Message}");
                return Json(new { data = new List<object>(), error = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult SaveSelectedEmployees([FromBody] List<ApproverModel> employees)
        {
            try
            {
                if (employees == null || !employees.Any())
                    return Json(new { success = false, message = "No data provided" });

                var approverNo = employees.First().approverNo;
                var approverLevel = employees.First().approverLevel;
                var typeList = employees.First().typeList;

                // Validate approver exists
                if (!RecordExists("e_basicinfo", "employeeNo", approverNo))
                    return Json(new { success = false, message = "Approver not found!" });

                // Delete existing records for this approver/level/typeList combination
                var deleteSql = @"
                    DELETE FROM e_approver 
                    WHERE approverNo = @approverNo 
                    AND approverLevel = @approverLevel 
                    AND typeList = @typeList";

                _db.Execute(deleteSql, new { approverNo, approverLevel, typeList });

                // If empty list, just return (deletion only)
                if (employees.Count == 1 && string.IsNullOrWhiteSpace(employees[0].employeeNo))
                {
                    _auditTrail.Log("e_approver", 0, "DELETED",
                        $"Removed all assignments for approver {approverNo} - Level {approverLevel} - Type {typeList}");

                    return Json(new { success = true, message = "All assignments removed successfully." });
                }

                // Insert new records
                var insertSql = @"
                    INSERT INTO e_approver 
                    (employeeNo, approverNo, approverLevel, typeList, dtAdded, addedByUser, isActive)
                    VALUES 
                    (@employeeNo, @approverNo, @approverLevel, @typeList, NOW(), @addedByUser, 1)";

                int insertedCount = 0;
                foreach (var emp in employees)
                {
                    if (string.IsNullOrWhiteSpace(emp.employeeNo))
                        continue;

                    _db.Execute(insertSql, new
                    {
                        employeeNo = emp.employeeNo,
                        approverNo = emp.approverNo,
                        approverLevel = emp.approverLevel,
                        typeList = emp.typeList,
                        addedByUser = EmployeeNo
                    });

                    insertedCount++;
                }

                // Log to audit trail
                var logMessage = employees.Any(e => e.employeeNo == "ALL")
                    ? $"Set ALL employees for approver {approverNo} - Level {approverLevel}"
                    : $"Assigned {insertedCount} item(s) to approver {approverNo} - Level {approverLevel} - Type {typeList}";

                _auditTrail.Log("e_approver", 0, "CREATED", logMessage);

                return Json(new { success = true, message = "Employees updated successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveSelectedEmployees: {ex.Message}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult RemoveApproverAssignment(int id, string reason, string deletedByUser)
        {
            try
            {
                // Check if record exists
                if (!_db.ExecuteScalar<bool>("SELECT COUNT(*) FROM e_approver WHERE id = @id AND isActive = 1", new { id }))
                    return Json(new { success = false, message = "Assignment not found or already removed!" });

                // Validate reason
                if (string.IsNullOrWhiteSpace(reason))
                    return Json(new { success = false, message = "Reason for removal is required!" });

                var sql = @"
                    UPDATE e_approver
                    SET isActive = 0, 
                        dtDeleted = NOW(),
                        deletedByUser = @deletedByUser
                    WHERE id = @id";

                _db.Execute(sql, new { id, deletedByUser = EmployeeNo });

                // Log to audit trail
                _auditTrail.Log("e_approver", id, "DELETED",
                    $"Removed approver assignment. Reason: {reason}");

                return Json(new { success = true, message = "Assignment removed successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RemoveApproverAssignment: {ex.Message}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        private bool RecordExists(string table, string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var sql = $"SELECT COUNT(*) FROM {table} WHERE {column} = @value AND isActive = 1";
            return _db.QuerySingle<int>(sql, new { value }) > 0;
        }

    }
}