using Dapper;
using KEISAN_HRIS_v2.Models.Report;
using KEISAN_HRIS_v2.Models.Users;
using KEISAN_HRIS_v2.Security;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace KEISAN_HRIS_v2.Controllers.Report
{
    [ModuleAuthorize("RPTalphalistM")]
    public class AlphalistController : Controller
    {
        private readonly IDbConnection _db;

        public AlphalistController(IDbConnection db)
        {
            _db = db;
        }

        public IActionResult Index()
        {
            return View("~/Views/Report/Alphalist.cshtml");
        }

        [HttpGet]
        public IActionResult GetReportList(string branch, string dateYear, string employeeNo)
        {   
            
            string query = @"
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
							basic13THMONTH 
							,SSSDIFFERENTIAL
							,RETROACTIVEPAYBASIC,
						13thMonthPay,PERFORMANCEBONUS ,OVERTIMEMEAL, MEALADJ, OTHERINCENTIVES ,TAXABLE, SL, TAXABLEOTHERDEDUCTION,
						ROUND(LEAST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES  +Prev_13thMonth), 90000),2) AS b13thMonthPay,
			
						totalDeminimis,
						totalSSS,
						totalPHI,
						totalHDMF,
						(totalSSS + totalPHI + totalHDMF)  AS totalMandatory,
			
						0 salaryAndOtherCompensation, -- usually, pag meron lang separation pay, sss maternity salary differential M'Agnes
			
						ROUND((LEAST((13thMonthPay +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES), 90000) 
						  + totalDeminimis + totalSSS + totalPHI + totalHDMF + 0 -- 0 for salaryAndOtherCompensation
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
						+ GREATEST((13thMonthPay +Prev_13thMonth +PERFORMANCEBONUS +OVERTIMEMEAL +MEALADJ +OTHERINCENTIVES )-90000, 0) -- +TAXABLE
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
				
							  ROUND((CAST(AES_DECRYPT(p.basicPaySemi,'portalkeisan') AS CHAR(200))  - IFNULL(p.totalAmountLate,0) - IFNULL(p.totalAmountUndertime,0) - IFNULL(p.absentAmount,0)),2) AS basicLessTardiness,
				  
							  ROUND(IFNULL((SELECT SUM(CAST(AES_DECRYPT(mo.basicPaySemi,'portalkeisan') AS CHAR(200))  - mo.totalAmountLate - mo.totalAmountUndertime - mo.absentAmount)
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
				  
							  IFNULL(taxableOtherDeduction.otherDeduction,0) AS TAXABLEOTHERDEDUCTION, -- saved in catering OT 				  
				  
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
									AND
									 c.dateFrom BETWEEN STR_TO_DATE(CONCAT(@dtYear - 1, '-11-26'), '%Y-%m-%d') 
									 AND STR_TO_DATE(CONCAT(@dtYear, '-12-10'), '%Y-%m-%d') 
					                  
								GROUP BY c.employeeNo
					
							) AS T1 ON T1.employeeNo = p.employeeNo 
				
							LEFT JOIN 
							(
								SELECT o.employeeNo, SUM(o.amount) otherDeduction FROM c_receivable o JOIN s_otherdeduction od ON od.otherDeductionCode = o.otherDeductionCode
								WHERE od.isActive = 1 AND od.isTaxable = 1 AND o.dateYear = @dtYear GROUP BY o.employeeNo
							) AS taxableOtherDeduction ON taxableOtherDeduction.employeeNo = p.employeeNo
				
							-- Note: Taxable Other Deduction is saved in CateringOT in p_biometrics
			
				
							WHERE 
				  				p.statusName = 'Posted' 
				  				AND p.isActive = 1
				  				AND p.dateFrom BETWEEN CONCAT((@dtYear - 1),'/12/26') AND CONCAT(@dtYear,'/12/10') 
								AND (@brcode='' OR @brcode='ALL' OR b.branchCode = @brcode)		
							--	AND (@employeeno = '' or b.employeeNo = @employeeno)
						) T2
			
						-- GROUP BY T2.employeeNo

						GROUP BY 
							T2.employeeNo,
							T2.employeeName,
							T2.lastName,
							T2.firstName,
							T2.middleName,
							T2.TIN,
							T2.branchName,
							T2.branchCode,
							T2.sbuAddress,
							T2.sbuTIN,
							T2.RDOCODE,
							T2.Zipcode,
							T2.basic13THMONTH,
							T2.basicLessTardiness,
							T2.SSSDIFFERENTIAL,
							T2.RETROACTIVEPAY,
							T2.RETROACTIVEPAYBASIC,
							T2.OVERTIMEMEAL,
							T2.MEALADJ,
							T2.OTHERINCENTIVES,
							T2.TAXABLE,
							T2.Prev_13thMonth,
							T2.Prev_Deminimis,
							T2.Prev_Statutory,
							T2.Prev_TaxableBasic,
							T2.Prev_TaxWithheld,
							T2.SL,
							T2.TAXABLEOTHERDEDUCTION,
							T2.periodFrom,
							T2.periodTo,
							T2.birthDate,
							T2.dateResigned,
							T2.mobileNo,
							T2.presentAddress,
							T2.permanentAddress
			
					) T3
				) T4
			)T5


			ORDER BY employeeName

            ";

            

            var p = new DynamicParameters();
            p.Add("@brcode", branch);
            p.Add("@dtYear", dateYear);
            p.Add("@employeeno", employeeNo);

            //var contriReport = _db.Query<AlphalistModel>(query.ToString(), p).ToList();

            var list = _db.Query<AlphalistModel>(query, p).ToList();

            return Json(new { data = list });
        }

    }
}