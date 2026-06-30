using Dapper;
using KEISAN_HRIS_v2.Models.Setup;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    [ModuleAuthorize("FemployeeBenefitsSettingM")]
    public class BenefitsController : Controller
    {
        private readonly IDbConnection _db;

        public BenefitsController(IDbConnection db)
        {
            _db = db;
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public JsonResult GetUserBenefitsList()
        {

            var benefitsSetting = new StringBuilder(
                @"SELECT 
                    b.employeeNo,
                    CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(b.middleName, '')) AS employeeName,

                    IFNULL(a.tax, 0) AS tax,
                    IFNULL(a.sss, 0) AS sss,
                    IFNULL(a.philhealth, 0) AS philhealth,
                    IFNULL(a.pagibig, 0) AS pagibig,

                    IFNULL(a.sssManualEE, 0) AS sssManualEE,
                    IFNULL(a.philhealthManualEE, 0) AS philhealthManualEE,
                    IFNULL(a.pagibigManualEE, 0) AS pagibigManualEE,

                    IFNULL(a.sssManualER, 0) AS sssManualER,
                    IFNULL(a.philhealthManualER, 0) AS philhealthManualER,
                    IFNULL(a.pagibigManualER, 0) AS pagibigManualER

                FROM e_benefitssetting a
                RIGHT JOIN e_basicInfo b ON a.employeeNo = b.employeeNo
                WHERE b.isActive = 1;");

            var employees = _db.Query<userEmployeeBenefits>(benefitsSetting.ToString()).ToList();
            return Json(new { data = employees });

        }
        [HttpGet]
        public JsonResult GetEmployeeBenefits(string employeeNo)
        {
            try
            {
                // If not existed insert
                string insertSql = @"
                    INSERT INTO e_benefitssetting (employeeNo, tax, sss, philhealth, pagibig, isActive, dtAdded)
                    SELECT @employeeNo, 0, 0, 0, 0, 1, NOW()
                    WHERE NOT EXISTS (
                        SELECT 1 FROM e_benefitssetting WHERE employeeNo = @employeeNo
                    );";

                _db.Execute(insertSql, new { employeeNo });
                // Get employee benefits
                string sql = @"
                    SELECT 
                        b.employeeNo,
                        CONCAT(e.lastName, ', ', e.firstName, ' ', IFNULL(e.middleName, '')) AS employeeName,
                        b.tax, b.sss, b.philhealth, b.pagibig,
                        b.taxManual, 
                        b.sssManualEE, b.sssManualER,
                        b.philhealthManualEE, b.philhealthManualER,
                        b.pagibigManualEE, b.pagibigManualER
                    FROM e_benefitssetting b
                    LEFT JOIN e_basicinfo e ON e.employeeNo = b.employeeNo
                    WHERE b.employeeNo = @employeeNo";

                var employee = _db.QuerySingleOrDefault(sql, new { employeeNo });
                return Json(employee);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public JsonResult UpdateBenefitSetting(string employeeNo, string field, int value)
        {
            try
            {
                // Whitelist allowed columns to prevent SQL injection
                var allowedFields = new[] { "tax", "sss", "philhealth", "pagibig" };
                if (!allowedFields.Contains(field.ToLower()))
                {
                    return Json(new { success = false, message = "Invalid field name." });
                }

                // Check if record exists
                string checkBenefits = @"SELECT COUNT(*) FROM e_benefitssetting WHERE employeeNo = @employeeNo";
                int count = _db.ExecuteScalar<int>(checkBenefits, new { employeeNo });

                if (count > 0)
                {
                    // ✅ Update existing record
                    string updateSql = $@"
                        UPDATE e_benefitssetting
                        SET 
                            {field} = @value,
                            taxManual = CASE WHEN '{field}' = 'tax' AND @value = 1 THEN 0 ELSE taxManual END,
                            sssManualEE = CASE WHEN '{field}' = 'sss' AND @value = 1 THEN 0 ELSE sssManualEE END,
                            sssManualER = CASE WHEN '{field}' = 'sss' AND @value = 1 THEN 0 ELSE sssManualER END,
                            philhealthManualEE = CASE WHEN '{field}' = 'philhealth' AND @value = 1 THEN 0 ELSE philhealthManualEE END,
                            philhealthManualER = CASE WHEN '{field}' = 'philhealth' AND @value = 1 THEN 0 ELSE philhealthManualER END,
                            pagibigManualEE = CASE WHEN '{field}' = 'pagibig' AND @value = 1 THEN 0 ELSE pagibigManualEE END,
                            pagibigManualER = CASE WHEN '{field}' = 'pagibig' AND @value = 1 THEN 0 ELSE pagibigManualER END,
                            dtLastModified = NOW()
                        WHERE employeeNo = @employeeNo";
                    _db.Execute(updateSql, new { value, employeeNo });
                }
                else
                {
                    // ✅ Insert record if not exists, then update the selected field
                    string insertAndUpdateSql = $@"
                        INSERT INTO e_benefitssetting (employeeNo, tax, sss, philhealth, pagibig, isActive, dtAdded)
                        SELECT @employeeNo, 0, 0, 0, 0, 1, NOW()
                        WHERE NOT EXISTS (
                            SELECT 1 FROM e_benefitssetting WHERE employeeNo = @employeeNo
                        );

                        UPDATE e_benefitssetting
                        SET 
                            {field} = @value,
                            taxManual = CASE WHEN '{field}' = 'tax' AND @value = 1 THEN 0 ELSE taxManual END,
                            sssManualEE = CASE WHEN '{field}' = 'sss' AND @value = 1 THEN 0 ELSE sssManualEE END,
                            sssManualER = CASE WHEN '{field}' = 'sss' AND @value = 1 THEN 0 ELSE sssManualER END,
                            philhealthManualEE = CASE WHEN '{field}' = 'philhealth' AND @value = 1 THEN 0 ELSE philhealthManualEE END,
                            philhealthManualER = CASE WHEN '{field}' = 'philhealth' AND @value = 1 THEN 0 ELSE philhealthManualER END,
                            pagibigManualEE = CASE WHEN '{field}' = 'pagibig' AND @value = 1 THEN 0 ELSE pagibigManualEE END,
                            pagibigManualER = CASE WHEN '{field}' = 'pagibig' AND @value = 1 THEN 0 ELSE pagibigManualER END,
                            dtLastModified = NOW()
                        WHERE employeeNo = @employeeNo;";

                    _db.Execute(insertAndUpdateSql, new { value, employeeNo });
                }


                return Json(new { success = true, message = $"Employee benefits have been updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpPost]
        public JsonResult UpdateBenefitEmployee(string employeeNo, int tax, int sss, int philhealth, int pagibig, decimal? taxManual, decimal? sssManualEE,
    decimal? sssManualER, decimal? philhealthManualEE, decimal? philhealthManualER, decimal? pagibigManualEE, decimal? pagibigManualER)
        {
            try
            {
                // Check if record exists
                string checkQuery = "SELECT COUNT(*) FROM e_benefitssetting WHERE employeeNo = @employeeNo";
                int count = _db.ExecuteScalar<int>(checkQuery, new { employeeNo });

                if (count == 0)
                {
                    // Insert new record
                    string insertSql = @"
                INSERT INTO e_benefitssetting 
                (employeeNo, tax, sss, philhealth, pagibig, taxManual, sssManualEE, sssManualER,
                 philhealthManualEE, philhealthManualER, pagibigManualEE, pagibigManualER, isActive, dtAdded)
                VALUES (@employeeNo, @tax, @sss, @philhealth, @pagibig, 
                        @taxManual, @sssManualEE, @sssManualER, 
                        @philhealthManualEE, @philhealthManualER, 
                        @pagibigManualEE, @pagibigManualER, 1, NOW())";
                    _db.Execute(insertSql, new
                    {
                        employeeNo,
                        tax,
                        sss,
                        philhealth,
                        pagibig,
                        taxManual,
                        sssManualEE,
                        sssManualER,
                        philhealthManualEE,
                        philhealthManualER,
                        pagibigManualEE,
                        pagibigManualER
                    });
                }
                else
                {
                    // Update existing record
                    string updateSql = @"
                UPDATE e_benefitssetting SET
                    tax = @tax,
                    sss = @sss,
                    philhealth = @philhealth,
                    pagibig = @pagibig,
                    taxManual = CASE WHEN @tax = 1 THEN 0 ELSE @taxManual END,
                    sssManualEE = CASE WHEN @sss = 1 THEN 0 ELSE @sssManualEE END,
                    sssManualER = CASE WHEN @sss = 1 THEN 0 ELSE @sssManualER END,
                    philhealthManualEE = CASE WHEN @philhealth = 1 THEN 0 ELSE @philhealthManualEE END,
                    philhealthManualER = CASE WHEN @philhealth = 1 THEN 0 ELSE @philhealthManualER END,
                    pagibigManualEE = CASE WHEN @pagibig = 1 THEN 0 ELSE @pagibigManualEE END,
                    pagibigManualER = CASE WHEN @pagibig = 1 THEN 0 ELSE @pagibigManualER END,
                    dtLastModified = NOW()
                WHERE employeeNo = @employeeNo";
                    _db.Execute(updateSql, new
                    {
                        employeeNo,
                        tax,
                        sss,
                        philhealth,
                        pagibig,
                        taxManual,
                        sssManualEE,
                        sssManualER,
                        philhealthManualEE,
                        philhealthManualER,
                        pagibigManualEE,
                        pagibigManualER
                    });
                }

                return Json(new { success = true, message = "Employee benefits updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
