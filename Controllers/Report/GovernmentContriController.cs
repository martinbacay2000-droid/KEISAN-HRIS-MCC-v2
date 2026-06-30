using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text;


namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTgovernmentContributionM")]
    public class GovernmentContriController : BaseController
    {
        private readonly IDbConnection _db;

        public GovernmentContriController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/GovernmentContri.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string status, string branch, string department, string dateMonth, string dateYear)
        {

            string query = "";

            switch (status)
            {
                case "SSSreport":
                    query = @"SELECT pbio.dateMonth,
                             pbio.dateYear,
                             pbio.employeeNo,
                             ebasic.departmentCode,
                             CONCAT(ebasic.lastName,' ', IFNULL(ebasic.suffix,''),', ',ebasic.firstName,' ', ebasic.middleName) AS employeeName,
                             ROUND(SUM(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200)) + pbio.nonBasicPay + pbio.allowanceTaxable + pbio.otherIncome 
                                 - pbio.totalAmountLate - pbio.totalAmountUndertime - pbio.absentAmount)) AS totalPay,
                             ROUND(SUM(pbio.deductionSSSemployee + pbio.deductionWISPemployee),2) AS deductionSSSemployee,
                             ROUND(SUM(pbio.deductionSSSemployer + pbio.deductionWISPemployer),2) AS deductionSSSemployer,
                             ROUND(SUM(pbio.deductionSSSec),2) AS deductionSSSec,
                             ROUND(SUM(pbio.deductionSSSemployee + pbio.deductionSSSemployer + pbio.deductionWISPemployee + pbio.deductionWISPemployer + pbio.deductionSSSec),2) AS deductionSSSTotal,
                             ROUND(SUM(pbio.sssLoan),2) AS sssLoan,
                             ROUND(SUM(pbio.sssCalamity),2) AS sssCalamity
                      FROM p_biometrics pbio
                      JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo
                      WHERE pbio.isActive = 1 AND pbio.statusName='POSTED'
                        
                        AND (@brcode='ALL' OR @brcode is null OR ebasic.branchCode=@brcode)
                        AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                        AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR pbio.dateMonth=@dtMonth)
                        AND (@dtYear IS NULL OR pbio.dateYear=@dtYear)
                      GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode;";
                    break;

                case "PHIreport":
                    query = @"SELECT pbio.dateMonth,
                             pbio.dateYear,
                             pbio.employeeNo,
                             ebasic.departmentCode,
                             CONCAT(ebasic.lastName,' ', IFNULL(ebasic.suffix,''),', ',ebasic.firstName,' ', ebasic.middleName) AS employeeName,
                             ROUND(SUM(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200))),2) AS totalPay,
                             ROUND(SUM(ROUND(pbio.basicpaysemi,-2)),2) AS round,
                             ROUND(SUM(pbio.deductionPHIemployee),2) AS deductionPHIemployee,
                             ROUND(SUM(pbio.deductionPHIemployer),2) AS deductionPHIemployer,
                             ROUND(SUM(pbio.deductionPHIemployee + pbio.deductionPHIemployer),2) AS deductionPHITotal,
                             DATE_FORMAT(eperson.dateOfBirth, '%Y/%m/%d') AS dateOfBirth
                      FROM p_biometrics pbio
                      JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo
                      LEFT JOIN e_personalinfo eperson ON pbio.employeeNo = eperson.employeeNo
                      WHERE pbio.isActive=1 AND pbio.statusName='POSTED'
                        
                        AND (@brcode='ALL' OR ebasic.branchCode=@brcode)
                        AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                        AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR pbio.dateMonth=@dtMonth)
                        AND (@dtYear IS NULL OR pbio.dateYear=@dtYear)
                      GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode;";
                    break;

                case "PIFreport":
                    query = @"SELECT pbio.dateMonth,
                             pbio.dateYear,
                             pbio.employeeNo,
                             ebasic.departmentCode,
                             CONCAT(ebasic.lastName,' ', IFNULL(ebasic.suffix,''),', ',ebasic.firstName,' ', ebasic.middleName) AS employeeName,
                             ROUND(SUM(pbio.deductionPIFemployee),2) AS deductionPIFemployee,
                             ROUND(SUM(pbio.deductionPIFemployer),2) AS deductionPIFemployer,
                             ROUND(SUM(pbio.deductionPIFemployee + pbio.deductionPIFemployer),2) AS deductionPIFTotal,
                             DATE_FORMAT(eperson.dateOfBirth, '%Y/%m/%d') AS dateOfBirth,
                             ROUND(SUM(pbio.hdmfLoan),2) AS hdmfLoan,
                             ROUND(SUM(pbio.hdmfCalamity),2) AS hdmfCalamity
                      FROM p_biometrics pbio
                      JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo
                      LEFT JOIN e_personalinfo eperson ON pbio.employeeNo = eperson.employeeNo
                      WHERE pbio.isActive=1 AND pbio.statusName='POSTED'
                        
                        AND (@brcode='ALL' OR @brcode is null OR ebasic.branchCode=@brcode)
                        AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                        AND (@dtMonth = 'ALL' OR @dtMonth IS NULL OR pbio.dateMonth=@dtMonth)
                        AND (@dtYear IS NULL OR @dtYear IS NULL OR pbio.dateYear=@dtYear)
                      GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode;";
                    break;

                case "TAXreport":
                    query = @"SELECT pbio.dateMonth,
                            pbio.dateYear,
                            pbio.employeeNo,
                            ebasic.branchCode,
                            ebasic.departmentCode,
                            br.branchName,
                            CONCAT(ebasic.lastName, ', ', ebasic.firstname, ' ', IFNULL(ebasic.middleName,''), ' ', IFNULL(ebasic.suffix,'')) AS employeeName,
                            DATE_FORMAT(eperson.dateofbirth,'%m/%d/%Y') AS dateOfBirth,
                            epaydet.tinNo,
                            ROUND(SUM(pbio.taxableIncome) + SUM(pbio.totalMandatory),4) AS grossCompensation,
                            ROUND(SUM(pbio.totalMandatory),4) AS totalMandatory,
                            ROUND(SUM(CAST(AES_DECRYPT(pbio.basicPaySemi,'portalkeisan') AS CHAR(200)) - pbio.totalAmountUndertime - pbio.totalAmountLate - pbio.absentAmount - pbio.totalMandatory),4) AS taxableIncome,
                            ROUND(SUM(pbio.withHeldTax),4) AS withHeldTax
                        FROM p_biometrics pbio 
                        JOIN e_basicinfo ebasic ON pbio.employeeNo = ebasic.employeeNo 
                        LEFT JOIN e_payrolldetails epaydet ON pbio.employeeNo = epaydet.employeeNo
                        LEFT JOIN e_personalinfo eperson ON pbio.employeeNo = eperson.employeeNo
                        LEFT JOIN s_branch br ON br.branchCode = pbio.branchCode
                        WHERE pbio.isActive = 1
                          AND pbio.statusName = 'Posted'
                          AND (pbio.branchCode=@brcode OR @brcode is null OR @brcode='ALL')
                          AND (@department='' OR @department IS NULL OR @department='ALL' OR ebasic.departmentCode=@department)
                          AND (pbio.dateMonth=@dtMonth OR @dtMonth = 'ALL' OR @dtMonth IS NULL) AND pbio.dateYear=@dtYear
                        GROUP BY pbio.dateMonth, pbio.dateYear, pbio.employeeNo, ebasic.departmentCode;";

                    break;

                default:
                    return new JsonResult(new { data = new List<dynamic>() });
            }

            var p = new DynamicParameters();
            p.Add("@brcode", branch);
            p.Add("@department", string.IsNullOrWhiteSpace(department) ? "ALL" : department);
            p.Add("@dtMonth", dateMonth);
            p.Add("@dtYear", dateYear);

            var contriReport = _db.Query<GovernmentModel>(query.ToString(), p).ToList();
            return Json(new { data = contriReport });
        }

        [HttpGet]
        public IActionResult GetPhilhealthOR(string branch, string dateMonth, string dateYear)
        {
            const string query = @"
                SELECT id, dateMonth, dateYear, `OR`, dateOfPayment, branchCode
                FROM t_philhealth
                WHERE branchCode = @brcode
                  AND dateMonth  = @dtMonth
                  AND dateYear   = @dtYear
                  AND isActive   = 1
                LIMIT 1";

            var p = new DynamicParameters();
            p.Add("@brcode", branch);
            p.Add("@dtMonth", dateMonth);
            p.Add("@dtYear", dateYear);

            var record = _db.QueryFirstOrDefault<PhilhealthORModel>(query, p);

            if (record == null)
                return Json(new { orNumber = (string?)null, dateOfPayment = (string?)null });

            return Json(new
            {
                orNumber = record.OR,
                dateOfPayment = record.dateOfPayment
            });
        }

        [HttpPost]
        public IActionResult SavePhilhealthOR(string branch, string dateMonth, string dateYear,
                                               string orNumber, string dateOfPayment)
        {
            if (string.IsNullOrWhiteSpace(branch) || branch == "ALL")
                return Json(new { success = false, message = "A specific branch is required." });

            if (string.IsNullOrWhiteSpace(orNumber) || string.IsNullOrWhiteSpace(dateOfPayment))
                return Json(new { success = false, message = "OR# and Date of Payment are required." });

            var currentUser = EmployeeNo;

            const string checkQuery = @"
                SELECT COUNT(1) FROM t_philhealth
                WHERE branchCode = @brcode
                  AND dateMonth  = @dtMonth
                  AND dateYear   = @dtYear
                  AND isActive   = 1";

            var p = new DynamicParameters();
            p.Add("@brcode", branch);
            p.Add("@dtMonth", dateMonth);
            p.Add("@dtYear", dateYear);

            int exists = _db.ExecuteScalar<int>(checkQuery, p);

            string upsertQuery;

            if (exists > 0)
            {
                upsertQuery = @"
                    UPDATE t_philhealth
                    SET `OR`               = @orNumber,
                        dateOfPayment      = @dop,
                        dtLastModified     = NOW(),
                        lastModifiedByUser = @user
                    WHERE branchCode = @brcode
                      AND dateMonth  = @dtMonth
                      AND dateYear   = @dtYear
                      AND isActive   = 1";
            }
            else
            {
                upsertQuery = @"
                    INSERT INTO t_philhealth
                        (dateMonth, dateYear, `OR`, dateOfPayment, branchCode,
                         isActive, dtAdded, addedByUser)
                    VALUES
                        (@dtMonth, @dtYear, @orNumber, @dop, @brcode,
                         1, NOW(), @user)";
            }

            p.Add("@orNumber", orNumber);
            p.Add("@dop", dateOfPayment);
            p.Add("@user", currentUser);

            _db.Execute(upsertQuery, p);

            var action = exists > 0 ? "updated" : "saved";
            return Json(new { success = true, message = $"OR# {action} successfully." });
        }
    }
}