using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using Microsoft.AspNetCore.Mvc;
using System.Data;


namespace KEISAN_HRIS_v2.Controllers
{
    public class SetupController : Controller
    {
        private readonly IDbConnection _db;

        public SetupController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View();
        }


        // Add this action for AdjustmentList view
        public IActionResult AdjustmentList()
        {
            return View();
        }

        private IDbConnection GetConnection()
        {
            return _db;
        }


        // CRUD Operations for Adjustment List
        [HttpGet]
        public JsonResult GetAdjustmentList()
        {
            using (var con = GetConnection())
            {
                string sql = @"SELECT id, adjustmentCode, adjustmentName, isTaxable 
                              FROM s_adjustment 
                              WHERE dtDeleted IS NULL 
                              ORDER BY adjustmentCode";
                var adjustments = con.Query<AdjustmentListModel>(sql).ToList();
                return Json(new { data = adjustments });
            }
        }

        [HttpGet]
        public JsonResult GetAdjustment(int id)
        {
            using (var con = GetConnection())
            {
                string sql = @"SELECT id, adjustmentCode, adjustmentName, isTaxable 
                              FROM s_adjustment 
                              WHERE id = @Id AND isActive = 1";
                var adjustment = con.QueryFirstOrDefault<AdjustmentListModel>(sql, new { Id = id });
                return Json(adjustment);
            }
        }

        [HttpGet]
        public JsonResult GetEmployeeName(string employeeName)
        {
            using (var con = GetConnection())
            {
                string sql = @"SELECT employeeNo, CONCAT(lastName, ', ', firstName) AS employeeName
                       FROM e_basicinfo
                       WHERE isActive = 1
                       AND (@employeeName = '' OR @employeeName IS NULL OR firstName LIKE @search OR lastName LIKE @search)";

                var employees = con.Query(sql, new { employeeName, search = $"%{employeeName}%" }).ToList();
                return Json(employees);
            }
        }
        [HttpPost]
        public JsonResult SaveAdjustment(AdjustmentListModel model)
        {
            try
            {
                using (var con = GetConnection())
                {
                    if (model.id == 0) // Insert
                    {
                        string sql = @"INSERT INTO s_adjustment (adjustmentCode, adjustmentName, isActive, isTaxable, dtAdded, addedByUser) 
                                      VALUES (@adjustmentCode, @adjustmentName, @isActive, @isTaxable, NOW(), 'system')";
                        con.Execute(sql, model);
                    }
                    else // Update
                    {
                        string sql = @"UPDATE s_adjustment 
                                      SET adjustmentCode = @adjustmentCode, 
                                          adjustmentName = @adjustmentName, 
                                          isActive = @isActive,
                                          isTaxable = @isTaxable,
                                          dtLastModified = NOW(),
                                          lastModifiedByUser = 'system'
                                      WHERE id = @id";
                        con.Execute(sql, model);
                    }
                    return Json(new { success = true, message = "Adjustment saved successfully!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult DeleteAdjustment(int id)
        {
            try
            {
                using (var con = GetConnection())
                {
                    string sql = @"UPDATE s_adjustment 
                                  SET dtDeleted = NOW(), isActive = 0,
                                      deletedByUser = 'system' 
                                  WHERE id = @Id";
                    con.Execute(sql, new { Id = id });
                    return Json(new { success = true, message = "Adjustment deleted successfully!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Existing methods...
        [HttpGet]
        public JsonResult GetEmploymentStatuses()
        {

            using (var con = GetConnection())
            {
                string sql = @"SELECT employmentStatusCode, employmentStatusName 
                       FROM s_employmentstatus  where isActive = 1 ";
                var statuses = _db.Query(sql).ToList();
                return Json(statuses);
            }
        }

        [HttpGet]
        public JsonResult GetEmployeePositions()
        {
            string sql = @"SELECT positionCode, positionName 
                   FROM s_position 
                   WHERE isActive = 1 
                   ORDER BY positionName ASC";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }
        [HttpGet]
        public JsonResult GetEmployeeCitizenship()
        {
            string sql = @"SELECT citizenshipCode, citizenshipName 
                       FROM s_citizenship where isActive = 1 ";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetEmployeeRanks()
        {
            string sql = @"SELECT rankCode, rankName 
                   FROM s_rank 
                   WHERE isActive = 1 
                   ORDER BY rankName ASC";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetEmploymentStatus()
        {

            string sql = @"SELECT employmentStatusCode, employmentStatusName 
                       FROM s_employmentstatus where isActive = 1";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetEmployeeBranch()
        {
            string sql = @"SELECT branchCode, branchName 
                   FROM s_branch 
                   WHERE isActive = 1 
                   ORDER BY branchName ASC";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetEmployeeList()
        {

            string sql = @"SELECT b.employeeNo, CONCAT(b.lastname,', ', b.firstname, ' ', b.middlename) as employeeName
                       FROM e_basicinfo b WHERE b.isActive = 1 ";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetOpenCutOff()
        {
            string sql = @"

                    SELECT DISTINCT 
                        p.methodType,
                        p.cutOffType,
                        p.dateMonth,
                        p.dateYear,
                        DATE_FORMAT(p.dateFrom, '%Y/%m/%d') AS dateFrom,
                        DATE_FORMAT(p.dateTo, '%Y/%m/%d') AS dateTo
                    FROM p_biometricsline AS p
                    JOIN e_basicinfo AS b ON b.employeeNo = p.employeeNo
                    WHERE b.isActive = 1 
                      AND p.isActive = 1
                      AND p.statusName = 'Open'
                      ORDER BY p.dateFrom DESC
                    ";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetPostedCutOff()
        {
            string sql = @"

                    SELECT DISTINCT 
                        p.methodType,
                        p.cutOffType,
                        p.dateMonth,
                        p.dateYear,
                        DATE_FORMAT(p.dateFrom, '%Y/%m/%d') AS dateFrom,
                        DATE_FORMAT(p.dateTo, '%Y/%m/%d') AS dateTo
                    FROM p_biometricsline AS p
                    JOIN e_basicinfo AS b ON b.employeeNo = p.employeeNo
                    WHERE b.isActive = 1 
                      AND p.isActive = 1
                      AND p.statusName = 'Posted'
                      ORDER BY p.dateFrom DESC
                    ";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetEmployeeDepartment()
        {
            string sql = @"SELECT departmentCode, departmentName 
                   FROM s_department 
                   WHERE isActive = 1 
                   ORDER BY departmentName ASC";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);
        }

        [HttpGet]
        public JsonResult GetEmployeeUnit()
        {

            string sql = @"SELECT unitCode, unitName 
                       FROM s_unit  where isAcive = 1 ";
            var statuses = _db.Query(sql).ToList();
            return Json(statuses);

        }

        [HttpGet]
        public JsonResult GetBankType()
        {

            string sql = @"SELECT bankCode as bankType, bankName 
                       FROM s_bank  where isAcive = 1  ";
            var bankTypes = _db.Query(sql).ToList();
            return Json(bankTypes);

        }

        [HttpGet]
        public JsonResult GetAllowanceList()
        {

            string sql = @"SELECT allowanceCode, allowanceName, CASE WHEN ISTAXABLE = 1 THEN 'TAXABLE' ELSE 'NON-TAXABLE' END AS taxType,
                       basis as allowanceSchedule
                       FROM s_allowance  where isAcive = 1  ";
            var bankTypes = _db.Query(sql).ToList();
            return Json(bankTypes);

        }

        [HttpGet]
        public JsonResult GetLoanList()
        {

            string sql = @"SELECT loanCode, loanName
                       FROM s_loan   where isActive = 1  ";
            var bankTypes = _db.Query(sql).ToList();
            return Json(bankTypes);

        }


        [HttpGet]
        public JsonResult GetLeaveList()
        {

            string sql = @"SELECT leaveCode, leaveName
                       FROM s_leave WHERE isActive = 1 ";
            var leaveTypes = _db.Query(sql).ToList();
            return Json(leaveTypes);

        }

        [HttpGet]
        public JsonResult GetEmployees()
        {
            string sql = @"SELECT id, employeeNo, 
                     UPPER(IFNULL(lastName,'')) as lastName, 
                     UPPER(IFNULL(firstName,'')) as firstName, 
                     UPPER(IFNULL(middleName,'')) as middleName 
               FROM e_basicinfo 
               WHERE isActive = 1 
               ORDER BY lastName ASC";
            var employeeList = _db.Query(sql).ToList();
            return Json(employeeList);
        }


        [HttpGet]
        public JsonResult GetScheduleType()
        {
            string sql = @"SELECT id, scheduleTypeCode, scheduleTypeName 
                       FROM s_scheduleType  where isActive = 1 ";
            var scheduleTypes = _db.Query(sql).ToList();
            return Json(scheduleTypes);
        }

        [HttpGet]
        public JsonResult GetEmployeeRank()
        {
            string sql = @"SELECT rankCode, rankName 
                   FROM s_rank 
                   WHERE isActive = 1 
                   AND dtDeleted IS NULL
                   ORDER BY rankName";
            var ranks = _db.Query(sql).ToList();
            return Json(ranks);
        }
    }
}