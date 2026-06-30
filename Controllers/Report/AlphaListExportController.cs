using Dapper;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    public class AlphaListExportController : Controller
    {
        private readonly IDbConnection _db;

        public AlphaListExportController(IDbConnection db) => _db = db;

        // Exports alphalist data to Excel based on specified filters and returns the file for download
        [HttpGet]
        public IActionResult ExportToExcel(string? branch, string? dateYear, int offset = 0, int limit = -1,
            string? sortColumn = null, string? sortDirection = "asc")
        {
            try
            {
                // Get current employee number from session
                var employeeNo = HttpContext.Session.GetString("employeeNo");
                if (string.IsNullOrEmpty(employeeNo))
                    return BadRequest(new { success = false, message = "User not authenticated" });

                // Get employee details
                var employeeInfo = GetEmployeeInfo(employeeNo);

                var data = GetAlphalistData(branch, dateYear, offset, limit, sortColumn, sortDirection);

                if (data.Count == 0)
                    return BadRequest(new { success = false, message = "No data to export" });

                var excelFile = GenerateExcelFile(data, dateYear, employeeInfo);
                var fileName = $"Alphalist_{dateYear}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                return File(excelFile, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Export failed: {ex.Message}" });
            }
        }

        // Retrieves employee information for the export header
        private (string EmployeeNo, string EmployeeName) GetEmployeeInfo(string employeeNo)
        {
            // First try to get from s_user table (for system users)
            var userQuery = @"
                SELECT 
                    userCode,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM s_user
                WHERE userCode = @employeeNo
                LIMIT 1";

            var userResult = _db.QueryFirstOrDefault<dynamic>(userQuery, new { employeeNo });

            if (userResult != null)
            {
                return (userResult.userCode, userResult.employeeName);
            }

            // Fallback to e_basicinfo table (for employees)
            var empQuery = @"
                SELECT 
                    employeeNo,
                    CONCAT(lastName, ', ', firstName, ' ', IFNULL(CONCAT(LEFT(middleName, 1), '.'), '')) AS employeeName
                FROM e_basicinfo
                WHERE employeeNo = @employeeNo
                LIMIT 1";

            var empResult = _db.QueryFirstOrDefault<dynamic>(empQuery, new { employeeNo });

            if (empResult != null)
            {
                return (empResult.employeeNo, empResult.employeeName);
            }

            return (employeeNo, "Unknown User");
        }

        // Map frontend column names to database column names
        private string GetDatabaseColumnName(string sortColumn)
        {
            return sortColumn?.ToLower() switch
            {
                "employeeno" => "employeeNo",
                "employeename" => "employeeName",
                "totalbasicsalary" => "totalBasicSalary",
                "b13thmonthpay" => "b13thMonthPay",
                "totaldeminimis" => "totalDeminimis",
                "totalsss" => "totalSSS",
                "totalphi" => "totalPHI",
                "totalhdmf" => "totalHDMF",
                "totalmandatory" => "totalMandatory",
                "salaryandothercompensation" => "salaryAndOtherCompensation",
                "totalnontaxablecompensationincome" => "totalNonTaxableCompensationIncome",
                "totalgrosscompensationincome" => "totalGrossCompensationIncome",
                "taxablebasic" => "taxableBasic",
                "totaltaxable13monthbonus" => "totalTaxable13monthbonus",
                "nettaxablebasicsalary" => "netTaxableBasicSalary",
                "prevemployerbasicsalary" => "prevEmployerBasicsalary",
                "prevemployerbenefitsand13thmonth" => "prevEmployerBenefitsand13thmonth",
                "prevemployerdeminimis" => "prevEmployerDeminimis",
                "prevemployertotalmandatory" => "prevEmployerTotalMandatory",
                "prevemployerothernonTax" => "prevEmployerOtherNonTax",
                "prevemployertaxablebasic" => "prevEmployerTaxableBasic",
                "prevemployertaxwithheld" => "prevEmployerTaxWithHeld",
                "prevemployernontaxable13monthadjustment" => "prevEmployerNonTaxable13monthAdjustment",
                "prevemployertaxable13monthadjustment" => "prevEmployerTaxable13monthAdjustment",
                "prevemployernettaxablebasic" => "prevEmployerNetTaxableBasic",
                "totalnontaxable13monthbenefitsprevpresentemployer" => "totalNontaxable13monthBenefitsPrevPresentEmployer",
                "nettaxablecompensationprevpresentemployer" => "netTaxableCompensationPrevPresentEmployer",
                "taxdue" => "taxDue",
                "totalwithholdingtax" => "TotalWithHoldingTax",
                "taxduerefund" => "taxDueRefund",
                "issuedtaxduerefund" => "issuedTaxDueRefund",
                "totaltaxwithheldadjusted" => "totalTaxWithHeldAdjusted",
                _ => "employeeNo"
            };
        }

        // Retrieves alphalist records from the database with applied filters and pagination
        private List<Dictionary<string, object>> GetAlphalistData(string? branch, string? dateYear,
            int offset, int limit, string? sortColumn, string? sortDirection)
        {
            var query = new StringBuilder(@"
                SELECT 
                    0 AS MWE,
                    'M' AS typeOfEmployer,
                    @dtYear AS dateYear, T5.*, 
                    T5.totalWithHeld AS TotalWithHoldingTax, 
                    taxDue - TOTALWITHHOLDINGTAX1 - tax13thMonth AS issuedTaxDueRefund,
                    taxDue - TOTALWITHHOLDINGTAX1 - tax13thMonth AS taxDueRefund
                FROM
                (
                    SELECT T4.*,
                        TotalWithHoldingTax1 + tax13thMonth AS totalWithHeld,
                        CAST((SELECT 
                              ((CAST(netTaxableCompensationPrevPresentEmployer AS DECIMAL(10,2)) - taxCompensationRangeMin) * (taxPercent/100)) + taxPWT AS taxDue
                                FROM s_taxtable t  
                              WHERE taxCompensationRangeMin <=  CAST(netTaxableCompensationPrevPresentEmployer AS DECIMAL(10,2))
                              AND CASE WHEN taxCompensationRangeMax = 0 
                              THEN taxCompensationRangeMin <=  CAST(netTaxableCompensationPrevPresentEmployer AS DECIMAL(10,2))
                              ELSE taxCompensationRangeMax >= CAST(netTaxableCompensationPrevPresentEmployer AS DECIMAL(10,2)) END  
                              AND taxType = 'Annual' 
                              AND t.effectivityDate <= NOW()) 
                        AS DECIMAL(10,2)) 
                        AS taxDue		
                    FROM 
                    (
                        SELECT 
                            employeeNo,
                            employeeName, LastName, FirstName, MiddleName, TIN,
                            branchName, branchCode, sbuAddress, sbuTIN, rdoCode, zipCode,
                            totalBasicSalary,
                            basic13THMONTH,
                            SSSDIFFERENTIAL,
                            RETROACTIVEPAYBASIC,
                            13thMonthPay,PERFORMANCEBONUS ,OVERTIMEMEAL, MEALADJ, OTHERINCENTIVES ,TAXABLE, SL, TAXABLEOTHERDEDUCTION,
                            ROUND(LEAST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES  +Prev_13thMonth), 90000),2) AS b13thMonthPay,
                            totalDeminimis,
                            totalSSS,
                            totalPHI,
                            totalHDMF,
                            (totalSSS + totalPHI + totalHDMF)  AS totalMandatory,
                            0 salaryAndOtherCompensation,
                            ROUND((LEAST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES), 90000) 
                              + totalDeminimis + totalSSS + totalPHI + totalHDMF + 0
                              + Prev_13thMonth
                            ),2) AS totalNonTaxableCompensationIncome,
                            ROUND((totalBasicSalary +13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES 
                             +Prev_13thMonth
                            ),2) AS totalGrossCompensationIncome,
                            ( totalBasicSalary -SSSDIFFERENTIAL - totalDeminimis - totalSSS - totalPHI - totalHDMF
                            ) AS taxableBasic,
                            GREATEST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES +Prev_13thMonth)-90000, 0) totalTaxable13monthbonus, 
                            IFNULL(
                            GREATEST(ROUND((SELECT ((( GREATEST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES  +Prev_13thMonth)-90000, 0) - taxCompensationRangeMin) * (taxPercent / 100)) + taxPWT) FROM `s_taxtable` WHERE isActive = 1 
                            AND GREATEST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES  +Prev_13thMonth)-90000, 0)
                            BETWEEN taxCompensationRangeMin AND CASE WHEN taxCompensationRangeMax = 0 THEN 9999999 ELSE taxCompensationRangeMax END AND taxType = 'Semi-Monthly' ORDER BY effectivityDate DESC LIMIT 1 ), 2), 0),0) 
                            as tax13thMonth,
                            ROUND((totalBasicSalary -SSSDIFFERENTIAL - totalDeminimis - totalSSS - totalPHI - totalHDMF
                            + GREATEST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES  +Prev_13thMonth)-90000, 0) 
                            ),2) AS netTaxableBasicSalary,
                            0 prevEmployerBasicsalary,
                            Prev_13thMonth prevEmployerBenefitsand13thmonth,
                            Prev_Deminimis prevEmployerDeminimis,
                            Prev_Statutory prevEmployerTotalMandatory,
                            0 prevEmployerOtherNonTax,
                            Prev_TaxableBasic prevEmployerTaxableBasic,
                            Prev_TaxWithheld prevEmployerTaxWithHeld,
                            0 prevEmployerNonTaxable13monthAdjustment,
                            0 prevEmployerTaxable13monthAdjustment,
                            0 prevEmployerNetTaxableBasic,
                            ROUND(LEAST((13thMonthPay +Prev_13thMonth +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES ), 90000),2) 		
                            AS totalNontaxable13monthBenefitsPrevPresentEmployer,
                            ROUND((totalBasicSalary -SSSDIFFERENTIAL - totalDeminimis - totalSSS - totalPHI - totalHDMF
                            + GREATEST((13thMonthPay +Prev_13thMonth +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES )-90000, 0)
                            ),2) + Prev_TaxableBasic 
                            AS netTaxableCompensationPrevPresentEmployer,
                            TotalWithHoldingTax AS payrollTax,
                            PERFORMANCEBONUS_TAX,
                            TotalWithHoldingTax + Prev_TaxWithheld AS TotalWithHoldingTax1,
                            `periodFrom`, `periodTo`, birthDate, dateResigned, mobileNo,  presentAddress, permanentAddress
                        FROM
                        (
                            SELECT 
                              employeeNo,
                              employeeName, LastName, FirstName, MiddleName, TIN,
                              branchName, branchCode, sbuAddress, sbuTIN, rdoCode, zipCode,
                              basic13THMONTH,
                              basicLessTardiness,
                              SSSDIFFERENTIAL,
                              RETROACTIVEPAY,
                              RETROACTIVEPAYBASIC,
                              OVERTIMEMEAL,
                              MEALADJ,
                              OTHERINCENTIVES,
                              TAXABLE,
                              Prev_13thMonth,
                              Prev_Deminimis,
                              Prev_Statutory,
                              Prev_TaxableBasic,
                              Prev_TaxWithheld,
                              SL,
                              TAXABLEOTHERDEDUCTION,
                              ROUND(SUM(basicLessTardiness) 
                               + SUM(OVERTIMEPAY)
                                + SSSDIFFERENTIAL
                                + RETROACTIVEPAY
                                + OVERTIMEMEAL
                                + TAXABLE
                                + SUM(allowanceTaxable)
                                + SUM(AdjustmentToBasic)
                                - TAXABLEOTHERDEDUCTION
                                + SL,2) as totalBasicSalary,
                                ROUND((basic13THMONTH 
                                + SSSDIFFERENTIAL
                                + RETROACTIVEPAYBASIC 
                                + TAXABLE
                               )/ 12
                                ,2) as 13thMonthPay,
                                IFNULL((select ROUND(SUM(IFNULL(p.amount,0) ),2) as ammount FROM p_performancebonus p WHERE p.employeeNo = T2.employeeNo AND p.isActive = 1 AND p.dateFrom
                                BETWEEN CONCAT((@dtYear - 1),'/12/26') AND CONCAT(@dtYear,'/12/10')) ,0) as 'PERFORMANCEBONUS',		
                                IFNULL((select ROUND(SUM(IFNULL(p.withHeldTax,0) ),2) as ammount FROM p_performancebonus p WHERE p.employeeNo = T2.employeeNo AND p.isActive = 1 AND p.dateFrom
                                BETWEEN CONCAT((@dtYear - 1),'/12/26') AND CONCAT(@dtYear,'/12/10')) ,0) as 'PERFORMANCEBONUS_TAX', 
                                SUM(DEMINIMIS) AS totalDeminimis,
                                SUM(AdjustmentToBasic) AS AdjustmentToBasic,
                                SUM(allowanceTaxable) AS allowanceTaxable,
                                SUM(deductionSSSemployee) AS totalSSS,
                                SUM(deductionPHIemployee) AS totalPHI,
                                SUM(CASE WHEN deductionPIFemployee>200 THEN 200 ELSE deductionPIFemployee END) AS totalHDMF,
                                SUM(withheldTax) AS TotalWithHoldingTax,
                                `periodFrom`, `periodTo`, birthDate, dateResigned, mobileNo,  presentAddress, permanentAddress
                            FROM
                            (
                                SELECT 
                                  b.employeeNo,  b.lastName, b.firstName, b.middleName, pay.tinNo AS TIN, 
                                  CONCAT(b.lastName, ', ', b.firstName, ' ', IFNULL(b.middleName,''), IFNULL(b.suffix,'')) AS employeeName,
                                  sb.branchName, sb.branchCode,
                                  sb.address AS sbuAddress,
                                  sb.TIN AS sbuTIN,
                                  sb.RDOCODE,
                                  sb.Zipcode,
                                  ROUND((CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200)) - IFNULL(p.totalAmountLate,0) - IFNULL(p.totalAmountUndertime,0) - IFNULL(p.absentAmount,0)),2) AS basicLessTardiness,
                                  ROUND(IFNULL((SELECT SUM(CAST(AES_DECRYPT(mo.basicPaySemi,'portalkeisan') AS CHAR(200)) - mo.totalAmountLate - mo.totalAmountUndertime - mo.absentAmount)
                                      FROM p_biometrics mo 
                                      WHERE mo.statusName='Posted' AND mo.dateFrom BETWEEN CONCAT((@dtYear - 1),'/12/11') AND CONCAT(@dtYear,'/12/10') 
                                      AND mo.isActive = 1 AND mo.employeeNo = p.employeeNo ),0)
                                    ,2) AS basic13THMONTH,
                                  IFNULL(T1.SSSDIFFERENTIAL, 0) AS SSSDIFFERENTIAL,
                                  IFNULL(T1.RETROACTIVEPAY, 0) AS RETROACTIVEPAY,
                                  IFNULL(T1.RETROACTIVEPAYBASIC, 0) AS RETROACTIVEPAYBASIC,
                                  IFNULL(T1.OVERTIMEMEAL, 0) AS OVERTIMEMEAL,
                                  IFNULL(T1.MEALADJ, 0) AS MEALADJ,
                                  IFNULL(T1.OTHERINCENTIVES, 0) AS OTHERINCENTIVES,
                                  IFNULL(T1.TAXABLE, 0) AS TAXABLE,
                                  IFNULL(T1.Prev_13thMonth, 0) AS Prev_13thMonth,
                                  IFNULL(T1.Prev_Deminimis, 0) AS Prev_Deminimis,
                                  IFNULL(T1.Prev_Statutory, 0) AS Prev_Statutory,
                                  IFNULL(T1.Prev_TaxableBasic, 0) AS Prev_TaxableBasic,
                                  IFNULL(T1.Prev_TaxWithheld, 0) AS Prev_TaxWithheld,
                                  IFNULL(T1.SL, 0) AS SL,
                                  IFNULL(p.nonBasicPay,0) AS OVERTIMEPAY,				  
                                  0 AdjustmentToBasic,
                                  IFNULL(taxableOtherDeduction.otherDeduction,0) AS TAXABLEOTHERDEDUCTION,
                                  IFNULL(ROUND(p.withHeldTax,2),0) AS withHeldTax,
                                  0 AS DEMINIMIS,
                                  IFNULL(p.allowanceTaxable,0) allowanceTaxable,
                                  IFNULL(p.deductionSSSemployee,0) deductionSSSemployee,
                                  IFNULL(p.deductionPHIemployee,0) deductionPHIemployee,
                                  IFNULL(p.deductionPIFemployee,0) deductionPIFemployee,
                                  '01 / 01' AS `periodFrom`,
                                    CASE WHEN IFNULL(b.isActive,0) = 0 THEN DATE_FORMAT(b.dateOfEmpTermInitial, '%m / %d') ELSE '12 / 31' END AS `periodTo`,
                                    DATE_FORMAT(per.dateOfBirth, '%m/%d/%Y') AS birthDate, DATE_FORMAT(b.dateOfEmpTermInitial, '%m/%d/%Y') AS dateResigned,
                                    per.mobileNo, per.presentAddress, per.permanentAddress
                                FROM e_basicinfo b
                                LEFT JOIN e_personalinfo per ON per.employeeNo = b.employeeNo
                                LEFT JOIN p_biometrics p ON b.employeeNo = p.employeeNo AND p.isActive = 1
                                LEFT JOIN e_payrolldetails pay ON pay.employeeNo = b.employeeNo
                                LEFT JOIN s_branch sb ON b.branchCode = sb.branchCode
                                LEFT JOIN 
                                (
                                    SELECT 
                                        c.employeeNo,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'SSSDIFFERENTIAL' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS SSSDIFFERENTIAL,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'RETROACTIVEPAY'  THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS RETROACTIVEPAY,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'RETROACTIVEPAY'  THEN IFNULL(c.retroBasic, 0) ELSE 0 END), 2) AS RETROACTIVEPAYBASIC,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'OVERTIMEMEAL' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS OVERTIMEMEAL,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'MEAL ALLOWANCE ADJ' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS MEALADJ,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'UNUTILIZED SL'	 THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS SL,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'OTHERINCENTIVES' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS OTHERINCENTIVES,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'TAXABLE' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS TAXABLE,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'Prev_13thMonth' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS Prev_13thMonth,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'Prev_Deminimis' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS Prev_Deminimis,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'Prev_Statutory' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS Prev_Statutory,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'Prev_TaxableBasic' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS Prev_TaxableBasic,
                                        ROUND(SUM(CASE WHEN c.adjustmentCode = 'Prev_TaxWithheld' THEN IFNULL(c.approvedAmount, 0) ELSE 0 END), 2) AS Prev_TaxWithheld
                                    FROM c_payable c
                                    WHERE 
                                        c.statusName = 'Closed'
                                        AND c.isActive = 1
                                        AND c.adjustmentCode IN ('SSSDIFFERENTIAL','RETROACTIVEPAY','UNUTILIZED SL','OVERTIMEMEAL','OTHERINCENTIVES', 'TAXABLE','MEAL ALLOWANCE ADJ',
                                                                    'Prev_13thMonth','Prev_Deminimis','Prev_Statutory','Prev_TaxableBasic','Prev_TaxWithheld')
                                        AND c.dateFrom BETWEEN STR_TO_DATE(CONCAT(@dtYear - 1, '-11-26'), '%Y-%m-%d') 
                                         AND STR_TO_DATE(CONCAT(@dtYear, '-12-10'), '%Y-%m-%d') 
                                    GROUP BY c.employeeNo
                                ) AS T1 ON T1.employeeNo = p.employeeNo 
                                LEFT JOIN 
                                (
                                    SELECT o.employeeNo, SUM(o.amount) otherDeduction FROM c_receivable o JOIN s_otherdeduction od ON od.otherDeductionCode = o.otherDeductionCode
                                    WHERE od.isActive = 1 AND od.isTaxable = 1 AND o.dateYear = @dtYear GROUP BY o.employeeNo
                                ) AS taxableOtherDeduction ON taxableOtherDeduction.employeeNo = p.employeeNo
                                WHERE 
                                      p.statusName = 'Posted' 
                                      AND p.isActive = 1
                                      AND p.dateFrom BETWEEN CONCAT((@dtYear - 1),'/12/26') AND CONCAT(@dtYear,'/12/10') 
                                    AND (@brcode='' OR @brcode='ALL' OR b.branchCode = @brcode)		
                            ) T2
                            GROUP BY T2.employeeNo
                        ) T3
                    ) T4
                )T5");

            // Apply sorting
            if (!string.IsNullOrWhiteSpace(sortColumn))
            {
                var dbColumn = GetDatabaseColumnName(sortColumn);
                var direction = sortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";
                query.Append($" ORDER BY {dbColumn} {direction}");
            }
            else
            {
                query.Append(" ORDER BY employeeNo");
            }

            // Apply pagination
            if (limit > 0)
                query.Append($" LIMIT {limit} OFFSET {offset}");

            var parameters = new DynamicParameters();
            parameters.Add("@brcode", branch ?? "");
            parameters.Add("@dtYear", dateYear ?? DateTime.Now.Year.ToString());

            var result = _db.Query(query.ToString(), parameters);
            var dataList = new List<Dictionary<string, object>>();

            foreach (var row in result)
            {
                var rowDict = (IDictionary<string, object>)row;
                dataList.Add(rowDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty));
            }

            return dataList;
        }

        // Generates an Excel file from the provided data with formatted headers and borders
        private byte[] GenerateExcelFile(List<Dictionary<string, object>> data, string? dateYear, (string EmployeeNo, string EmployeeName) employeeInfo)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add($"Alphalist {dateYear}");

            if (data.Count == 0) return package.GetAsByteArray();

            // Define column headers in order
            var columnHeaders = new List<string>
            {
                "Employee No",
                "Employee Name",
                "Basic Salary",
                "13th Month Pay",
                "De Minimis",
                "SSS",
                "PHIC",
                "HDMF",
                "Total Mandatory",
                "Salary & Other Compensation",
                "Total Non-Taxable Income",
                "Total Gross Income",
                "Taxable Basic",
                "Taxable 13th Month",
                "Net Taxable Basic",
                "Prev Employer Basic",
                "Prev 13th Month",
                "Prev De Minimis",
                "Prev Statutory",
                "Prev Other Non-Tax",
                "Prev Taxable Basic",
                "Prev Tax Withheld",
                "Prev Non-Tax 13th Adjustment",
                "Prev Tax 13th Adjustment",
                "Prev Net Taxable Basic",
                "Total Non-Tax 13th Month",
                "Net Taxable Compensation",
                "Tax Due",
                "Total Withholding Tax",
                "Tax Due Refund",
                "Issued Tax Due Refund",
                "Total Tax Adjusted"
            };

            // Map data keys to display headers
            var columnMapping = new Dictionary<string, string>
            {
                ["employeeNo"] = "Employee No",
                ["employeeName"] = "Employee Name",
                ["totalBasicSalary"] = "Basic Salary",
                ["b13thMonthPay"] = "13th Month Pay",
                ["totalDeminimis"] = "De Minimis",
                ["totalSSS"] = "SSS",
                ["totalPHI"] = "PHIC",
                ["totalHDMF"] = "HDMF",
                ["totalMandatory"] = "Total Mandatory",
                ["salaryAndOtherCompensation"] = "Salary & Other Compensation",
                ["totalNonTaxableCompensationIncome"] = "Total Non-Taxable Income",
                ["totalGrossCompensationIncome"] = "Total Gross Income",
                ["taxableBasic"] = "Taxable Basic",
                ["totalTaxable13monthbonus"] = "Taxable 13th Month",
                ["netTaxableBasicSalary"] = "Net Taxable Basic",
                ["prevEmployerBasicsalary"] = "Prev Employer Basic",
                ["prevEmployerBenefitsand13thmonth"] = "Prev 13th Month",
                ["prevEmployerDeminimis"] = "Prev De Minimis",
                ["prevEmployerTotalMandatory"] = "Prev Statutory",
                ["prevEmployerOtherNonTax"] = "Prev Other Non-Tax",
                ["prevEmployerTaxableBasic"] = "Prev Taxable Basic",
                ["prevEmployerTaxWithHeld"] = "Prev Tax Withheld",
                ["prevEmployerNonTaxable13monthAdjustment"] = "Prev Non-Tax 13th Adjustment",
                ["prevEmployerTaxable13monthAdjustment"] = "Prev Tax 13th Adjustment",
                ["prevEmployerNetTaxableBasic"] = "Prev Net Taxable Basic",
                ["totalNontaxable13monthBenefitsPrevPresentEmployer"] = "Total Non-Tax 13th Month",
                ["netTaxableCompensationPrevPresentEmployer"] = "Net Taxable Compensation",
                ["taxDue"] = "Tax Due",
                ["TotalWithHoldingTax"] = "Total Withholding Tax",
                ["taxDueRefund"] = "Tax Due Refund",
                ["issuedTaxDueRefund"] = "Issued Tax Due Refund",
                ["totalTaxWithHeldAdjusted"] = "Total Tax Adjusted"
            };

            var rowCount = data.Count;

            // Add main title (Row 1)
            ws.Cells[1, 1].Value = $"Alphalist - Year {dateYear}";
            ws.Cells[1, 1, 1, columnHeaders.Count].Merge = true;
            ws.Cells[1, 1].Style.Font.Size = 16;
            ws.Cells[1, 1].Style.Font.Bold = true;
            ws.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Add employee info and timestamp (Row 2)
            var timestamp = DateTime.Now.ToString("h:mmtt - M/d/yyyy").ToLower();
            var exportInfo = $"Generated By: ({employeeInfo.EmployeeNo}) {employeeInfo.EmployeeName}     Timestamp: {timestamp}";
            ws.Cells[2, 1].Value = exportInfo;
            ws.Cells[2, 1, 2, columnHeaders.Count].Merge = true;
            ws.Cells[2, 1].Style.Font.Size = 11;
            ws.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            ws.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            // Add headers (Row 4, leaving Row 3 blank for spacing)
            for (int col = 0; col < columnHeaders.Count; col++)
            {
                var cell = ws.Cells[4, col + 1];
                cell.Value = columnHeaders[col];
                StyleHeader(cell);
            }

            // Calculate totals for numeric columns
            var totals = new Dictionary<string, decimal>();
            foreach (var mapping in columnMapping)
            {
                if (mapping.Value != "Employee No" && mapping.Value != "Employee Name")
                {
                    decimal sum = 0;
                    foreach (var row in data)
                    {
                        if (row.ContainsKey(mapping.Key) &&
                            decimal.TryParse(row[mapping.Key]?.ToString(), out decimal value))
                        {
                            sum += value;
                        }
                    }
                    totals[mapping.Key] = sum;
                }
            }

            // Add data rows (starting from Row 5)
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columnHeaders.Count; col++)
                {
                    var cell = ws.Cells[row + 5, col + 1];
                    var headerName = columnHeaders[col];
                    var dataKey = columnMapping.FirstOrDefault(x => x.Value == headerName).Key;

                    if (!string.IsNullOrEmpty(dataKey) && data[row].ContainsKey(dataKey))
                    {
                        var cellValue = data[row][dataKey];

                        // Format numeric columns
                        if (col >= 2) // All columns after Employee Name are numeric
                        {
                            if (decimal.TryParse(cellValue?.ToString(), out decimal numValue))
                            {
                                cell.Value = numValue;
                                cell.Style.Numberformat.Format = "#,##0.00";
                                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                            }
                            else
                            {
                                cell.Value = cellValue?.ToString() ?? "0.00";
                            }
                        }
                        else
                        {
                            cell.Value = cellValue?.ToString() ?? string.Empty;
                        }
                    }
                }
            }

            // Add TOTALS ROW (after all data rows)
            int totalRowIndex = rowCount + 5;

            // Label for totals row
            var totalLabelCell = ws.Cells[totalRowIndex, 1];
            totalLabelCell.Value = "TOTAL";
            totalLabelCell.Style.Font.Bold = true;
            totalLabelCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            totalLabelCell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 217, 217));
            totalLabelCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Employee Name column (leave blank)
            var emptyCell = ws.Cells[totalRowIndex, 2];
            emptyCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            emptyCell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 217, 217));

            // Add totals for each numeric column
            for (int col = 2; col < columnHeaders.Count; col++) // Start from column 3 (index 2)
            {
                var cell = ws.Cells[totalRowIndex, col + 1];
                var headerName = columnHeaders[col];
                var dataKey = columnMapping.FirstOrDefault(x => x.Value == headerName).Key;

                if (!string.IsNullOrEmpty(dataKey) && totals.ContainsKey(dataKey))
                {
                    cell.Value = totals[dataKey];
                    cell.Style.Numberformat.Format = "#,##0.00";
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 217, 217));
                }
            }

            // Format table (update range to include totals row, starting from Row 4)
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            var range = ws.Cells[4, 1, totalRowIndex, columnHeaders.Count];
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;

            // Add thicker border above totals row for visual separation
            var totalRowTopBorder = ws.Cells[totalRowIndex, 1, totalRowIndex, columnHeaders.Count];
            totalRowTopBorder.Style.Border.Top.Style = ExcelBorderStyle.Medium;

            return package.GetAsByteArray();
        }

        // Applies bold blue header styling with white text and center alignment to the specified cell
        private void StyleHeader(ExcelRange cell)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.WrapText = true;
        }
    }
}