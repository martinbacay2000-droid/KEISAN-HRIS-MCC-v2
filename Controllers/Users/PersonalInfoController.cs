using Dapper;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.OtherServices;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FSPersonalInfoM")]
    public class PersonalInfoController : BaseController
    {
        private readonly IDbConnection _db;
        private readonly IAuditTrailService _auditTrail;

        public PersonalInfoController(IDbConnection db, IAuditTrailService auditTrail)
        {
            _db = db;
            _auditTrail = auditTrail;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetPersonalInfo(string employeeNo, string mode = "VIEW")
        {
            try
            {
                var employee = _db.QueryFirstOrDefault<userPersonalInfo>(
                    @"SELECT 
                        b.id, 
                        b.employeeNo, 
                        e.gender, 
                        e.weight,
                        e.height, 
                        e.bmi, 
                        e.dateOfBirth, 
                        e.birthPlace,
                        e.homePhoneNo, 
                        e.mobileNo, 
                        e.emailAddress,
                        e.religion, 
                        e.zipCode, 
                        e.presentAddress,
                        e.permanentAddress, 
                        e.fatherName, 
                        e.motherMaidenName,
                        e.personToNotify, 
                        e.relationship, 
                        e.contactNo,
                        e.civilStatus, 
                        e.nameOfSpouse, 
                        e.spouseDateOfBirth,
                        e.occupation, 
                        e.isActive, 
                        e.dtAdded, 
                        e.addedByUser,
                        e.citizenshipCode, 
                        c.citizenshipName
                    FROM e_basicInfo b  
                    LEFT JOIN e_personalinfo e ON b.employeeNo = e.employeeNo
                    LEFT JOIN s_citizenship c ON e.citizenshipCode = c.citizenshipCode AND c.isActive = 1
                    WHERE b.employeeNo = @employeeNo",
                            new { employeeNo });

                // If no personal info exists, create empty model with employeeNo
                if (employee == null || string.IsNullOrEmpty(employee.gender))
                {
                    employee = new userPersonalInfo
                    {
                        employeeNo = employeeNo
                    };
                }

                ViewBag.Mode = mode;
                return PartialView("~/Views/Users/Partials/_PersonalInfo.cshtml", employee);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPersonalInfo: {ex.Message}");
                return PartialView("~/Views/Users/Partials/_PersonalInfo.cshtml", new userPersonalInfo { employeeNo = employeeNo });
            }
        }

        [HttpPost]
        public JsonResult UpdatePersonalInfo(userPersonalInfo model)
        {
            try
            {
                // Get employee name for audit trail
                var employeeName = _db.QueryFirstOrDefault<string>(
                    "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { model.employeeNo });

                var exists = _db.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM e_personalinfo WHERE employeeNo = @employeeNo",
                    new { model.employeeNo });

                if (exists == 0) // Insert new record
                {
                    string sql = @"
                        INSERT INTO e_personalinfo (
                            employeeNo, gender, weight, height, civilStatus, citizenshipCode,
                            dateOfBirth, birthPlace, homePhoneNo, mobileNo, emailAddress, religion, zipCode,
                            presentAddress, permanentAddress, fatherName, motherMaidenName, personToNotify,
                            relationship, contactNo, nameOfSpouse, spouseDateOfBirth, occupation, isActive, dtAdded
                        ) VALUES (
                            @employeeNo, @gender, @weight, @height, @civilStatus, @citizenshipCode,
                            @dateOfBirth, @birthPlace, @homePhoneNo, @mobileNo, @emailAddress, @religion, @zipCode,
                            @presentAddress, @permanentAddress, @fatherName, @motherMaidenName, @personToNotify,
                            @relationship, @contactNo, @nameOfSpouse, @spouseDateOfBirth, @occupation, 1, NOW()
                        )";

                    _db.Execute(sql, model);

                    _auditTrail.Log("e_personalinfo", 0, "CREATED",
                        $"Created personal info for employee {model.employeeNo} - {employeeName}");

                    return Json(new { success = true, message = "Personal information created successfully!" });
                }
                else // Update existing record
                {
                    string sql = @"
                        UPDATE e_personalinfo SET
                            gender = @gender,
                            weight = @weight,
                            height = @height,
                            civilStatus = @civilStatus,
                            citizenshipCode = @citizenshipCode,
                            dateOfBirth = @dateOfBirth,
                            birthPlace = @birthPlace,
                            homePhoneNo = @homePhoneNo,
                            mobileNo = @mobileNo,
                            emailAddress = @emailAddress,
                            religion = @religion,
                            zipCode = @zipCode,
                            presentAddress = @presentAddress,
                            permanentAddress = @permanentAddress,
                            fatherName = @fatherName,
                            motherMaidenName = @motherMaidenName,
                            personToNotify = @personToNotify,
                            relationship = @relationship,
                            contactNo = @contactNo,
                            nameOfSpouse = @nameOfSpouse,
                            spouseDateOfBirth = @spouseDateOfBirth,
                            occupation = @occupation,
                            dtLastModified = NOW()
                        WHERE employeeNo = @employeeNo";

                    _db.Execute(sql, model);

                    _auditTrail.Log("e_personalinfo", 0, "UPDATED",
                        $"Updated personal info for employee {model.employeeNo} - {employeeName}");

                    return Json(new { success = true, message = "Personal information updated successfully!" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdatePersonalInfo: {ex.Message}");
                return Json(new { success = false, message = $"Error updating personal info: {ex.Message}" });
            }
        }

        [HttpGet]
        public JsonResult GetSiblingsList(string employeeNo)
        {
            try
            {
                string sql = @"
                    SELECT 
                        e.id,
                        e.employeeNo,
                        e.nameOfSibling,
                        DATE_FORMAT(e.dateOfBirth,'%Y/%m/%d') as dateOfBirth,
                        e.relationship,
                        e.gender,
                        e.dependent 
                    FROM e_siblings e 
                    WHERE e.isActive = 1
                        AND e.employeeNo = @employeeNo
                    ORDER BY e.dateOfBirth DESC";

                var siblings = _db.Query<SiblingList>(sql, new { employeeNo }).ToList();

                return Json(new { data = siblings });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetSiblingsList: {ex.Message}");
                return Json(new { data = new List<SiblingList>(), error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetSibling(int id)
        {
            try
            {
                string sql = @"
                    SELECT 
                        id,
                        employeeNo,
                        nameOfSibling,
                        DATE_FORMAT(dateOfBirth,'%Y/%m/%d') as dateOfBirth,
                        relationship,
                        gender,
                        dependent
                    FROM e_siblings 
                    WHERE id = @id AND isActive = 1";

                var sibling = _db.QueryFirstOrDefault<SiblingList>(sql, new { id });

                return Json(sibling);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetSibling: {ex.Message}");
                return Json(null);
            }
        }

        [HttpPost]
        public JsonResult AddSibling(SiblingList model)
        {
            try
            {
                // Check if sibling already exists
                string checkSql = @"
                    SELECT COUNT(*) 
                    FROM e_siblings
                    WHERE employeeNo = @employeeNo 
                        AND nameOfSibling = @nameOfSibling
                        AND isActive = 1";

                int exists = _db.ExecuteScalar<int>(checkSql, model);

                if (exists > 0)
                {
                    return Json(new { success = false, message = "This relative already exists for the employee." });
                }

                string insertSql = @"
                    INSERT INTO e_siblings
                    (employeeNo, nameOfSibling, dateOfBirth, gender, relationship, dependent, isActive, dtAdded)
                    VALUES (@employeeNo, @nameOfSibling, @dateOfBirth, @gender, @relationship, @dependent, 1, NOW())";

                _db.Execute(insertSql, model);

                // Get employee name for audit trail
                var employeeName = _db.QueryFirstOrDefault<string>(
                    "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { model.employeeNo });

                _auditTrail.Log("e_siblings", 0, "CREATED",
                    $"Added relative '{model.nameOfSibling}' for employee {model.employeeNo} - {employeeName}");

                return Json(new { success = true, message = "Relative information added successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddSibling: {ex.Message}");
                return Json(new { success = false, message = $"Error adding relative: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult UpdateSibling(SiblingList model)
        {
            try
            {
                string updateSql = @"
                    UPDATE e_siblings
                    SET dateOfBirth = @dateOfBirth,
                        gender = @gender,
                        relationship = @relationship,
                        dependent = @dependent,
                        dtLastModified = NOW()
                    WHERE id = @id AND isActive = 1";

                int rows = _db.Execute(updateSql, model);

                if (rows > 0)
                {
                    // Get employee name for audit trail
                    var employeeName = _db.QueryFirstOrDefault<string>(
                        "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                        new { model.employeeNo });

                    _auditTrail.Log("e_siblings", model.id, "UPDATED",
                        $"Updated relative '{model.nameOfSibling}' for employee {model.employeeNo} - {employeeName}");

                    return Json(new { success = true, message = "Relative information updated successfully!" });
                }
                else
                {
                    return Json(new { success = false, message = "No record found to update." });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateSibling: {ex.Message}");
                return Json(new { success = false, message = $"Error updating relative: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult DeleteSibling(int id, string reason = "")
        {
            try
            {
                // Get sibling info before deleting
                var sibling = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo, nameOfSibling FROM e_siblings WHERE id = @id",
                    new { id });

                if (sibling == null)
                    return Json(new { success = false, message = "Relative record not found!" });

                var sql = @"
                    UPDATE e_siblings 
                    SET isActive = 0,
                        dtDeleted = NOW(),
                        deletedByUser = @deletedBy
                    WHERE id = @id";

                _db.Execute(sql, new { id, deletedBy = EmployeeNo });

                // Get employee name for audit trail
                var employeeName = _db.QueryFirstOrDefault<string>(
                    "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { employeeNo = sibling.employeeNo });

                _auditTrail.Log("e_siblings", id, "DELETED",
                    $"Deleted relative '{sibling.nameOfSibling}' for employee {sibling.employeeNo} - {employeeName}" +
                    (string.IsNullOrWhiteSpace(reason) ? "" : $". Reason: {reason}"));

                return Json(new { success = true, message = "Relative record deleted successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteSibling: {ex.Message}");
                return Json(new { success = false, message = $"Error deleting relative: {ex.Message}" });
            }
        }

        [HttpPost]
        public JsonResult RestoreSibling(int id)
        {
            try
            {
                // Get sibling info before restoring
                var sibling = _db.QueryFirstOrDefault<dynamic>(
                    "SELECT employeeNo, nameOfSibling FROM e_siblings WHERE id = @id",
                    new { id });

                if (sibling == null)
                    return Json(new { success = false, message = "Relative record not found!" });

                var sql = @"
                    UPDATE e_siblings 
                    SET isActive = 1,
                        dtDeleted = NULL,
                        deletedByUser = NULL
                    WHERE id = @id";

                _db.Execute(sql, new { id });

                // Get employee name for audit trail
                var employeeName = _db.QueryFirstOrDefault<string>(
                    "SELECT CONCAT(firstName, ' ', lastName) FROM e_basicinfo WHERE employeeNo = @employeeNo",
                    new { employeeNo = sibling.employeeNo });

                _auditTrail.Log("e_siblings", id, "RESTORED",
                    $"Restored relative '{sibling.nameOfSibling}' for employee {sibling.employeeNo} - {employeeName}");

                return Json(new { success = true, message = "Relative record restored successfully!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RestoreSibling: {ex.Message}");
                return Json(new { success = false, message = $"Error restoring relative: {ex.Message}" });
            }
        }
    }
}