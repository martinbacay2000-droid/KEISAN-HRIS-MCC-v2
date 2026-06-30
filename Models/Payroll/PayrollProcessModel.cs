using System.ComponentModel.DataAnnotations;

namespace KEISAN_HRIS_v2.Models.Payroll
{
    public class PayrollProcessModel
    {
        public int id { get; set; }
        public string? fullName { get; set; }
        public string? methodType { get; set; }
        public string? cutOffType { get; set; }
        public string? dateMonth { get; set; }
        public string? dateYear { get; set; }
        public string? payType { get; set; }
        public string? dateFrom { get; set; }
        public string? dateTo { get; set; }

        public string? branchCode { get; set; }
        public string? departmentCode { get; set; }
        public string? departmentName { get; set; }
        public string? employmentStatus { get; set; }
        public string? positionCode { get; set; }
        public string? rankCode { get; set; }
        public string? activeStatus { get; set; }
        public string? employeeNo { get; set; }
        public string? payrollType { get; set; }
        public string? bankCode { get; set; }
        public string? accountNo { get; set; }
        public int? isNoLate { get; set; }
        public int? isMinimumWageEarner { get; set; }
        public double? contriPIFadditional { get; set; }
        public int? tax { get; set; }
        public int? sss { get; set; }
        public int? philhealth { get; set; }
        public int? pagibig { get; set; }
        public int? provident { get; set; }

        public string? payrollBasis { get; set; }
        public double? hourlyRate { get; set; }
        public double? minuteRate { get; set; }
        public double? basicMonthlyPay { get; set; }
        public double? dailyRate { get; set; }
        public double? basicPay { get; set; }
        public double? nonBasicPay { get; set; }
        public double? basicPaySemi { get; set; }

        #region Allowance

        public double allowanceDailyRate { get; set; }
        public double allowanceHourlyRate { get; set; }
        public double reg_basic_al { get; set; }
        public double tardy_al { get; set; }
        public double undertime_al { get; set; }
        public double absent_al { get; set; }
        public double salary_adjustment_al { get; set; }

        public double lh_basic_al { get; set; }
        public double lh_nd_al { get; set; }
        public double lh_ot_al { get; set; }

        public double rd_basic_al { get; set; }

        public double reg_nd_al { get; set; }
        public double reg_ndot_al { get; set; }
        public double reg_ot_al { get; set; }

        public double sh_basic_al { get; set; }
        public double sh_nd_al { get; set; }
        public double sh_ndot_al { get; set; }
        public double sh_ot_al { get; set; }
        #endregion

        #region Render Hours
        public double? render { get; set; }
        public double? renderOT { get; set; }
        public double? renderNSD { get; set; }
        public double? renderNSDOT { get; set; }


        public double? renderREST { get; set; }
        public double? renderRESTOT { get; set; }
        public double? renderNSDREST { get; set; }
        public double? renderNSDRESTOT { get; set; }


        public double? renderS { get; set; }
        public double? renderOTS { get; set; }
        public double? renderNSDS { get; set; }
        public double? renderNSDOTS { get; set; }


        public double? renderL { get; set; }
        public double? renderOTL { get; set; }
        public double? renderNSDL { get; set; }
        public double? renderNSDOTL { get; set; }


        public double? renderRESTS { get; set; }
        public double? renderRESTOTS { get; set; }
        public double? renderNSDRESTS { get; set; }
        public double? renderNSDRESTOTS { get; set; }


        public double? renderRESTL { get; set; }
        public double? renderRESTOTL { get; set; }
        public double? renderNSDRESTL { get; set; }
        public double? renderNSDRESTOTL { get; set; }
        #endregion

        #region Render Amounts
        public double? amount { get; set; }
        public double? amountOT { get; set; }
        public double? amountNSD { get; set; }
        public double? amountNSDOT { get; set; }

        public double? amountREST { get; set; }
        public double? amountRESTOT { get; set; }
        public double? amountNSDREST { get; set; }
        public double? amountNSDRESTOT { get; set; }


        public double? amountRESTS { get; set; }
        public double? amountRESTOTS { get; set; }
        public double? amountNSDRESTS { get; set; }
        public double? amountNSDRESTOTS { get; set; }


        public double? amountRESTL { get; set; }
        public double? amountRESTOTL { get; set; }
        public double? amountNSDRESTL { get; set; }
        public double? amountNSDRESTOTL { get; set; }
        #endregion

        #region Leave / Special / Sunday
        public double? amountL { get; set; }
        public double? amountOTL { get; set; }
        public double? amountNSDL { get; set; }
        public double? amountNSDOTL { get; set; }

        public double? amountS { get; set; }
        public double? amountOTS { get; set; }
        public double? amountNSDS { get; set; }
        public double? amountNSDOTS { get; set; }

        #endregion

        #region Attendance
        public double? absentCount { get; set; }
        public double? presentCount { get; set; }
        public double? wfhLeave { get; set; }
        public double? paidHoliday { get; set; }
        public double? absentAmount { get; set; }
        public double? presentAmount { get; set; }
        #endregion

        #region Totals
        public double? renderEarly { get; set; }
        public double? renderLate { get; set; }
        public double? renderUndertime { get; set; }
        public double? renderOvertime { get; set; }
        public double? amountEarly { get; set; }
        public double? amountLate { get; set; }
        public double? amountUndertime { get; set; }
        public double? totalDeductionLateUndertimeAbsent { get; set; }
        public double? otherIncome { get; set; }
        public double? totalGrossPay { get; set; }
        public double? totalGrossPayMandatory { get; set; }
        public double? grossIncome { get; set; }
        public double? taxableIncome { get; set; }
        public double? withHeldTax { get; set; }
        public double? totalDeduction { get; set; }
        public double? totalNetPay { get; set; }
        public double? totalMBOS { get; set; }
        public double? additionalMbos { get; set; }
        public double? healthcard { get; set; }
        public double? parking { get; set; }
        public double? meals { get; set; }
        public double? fixedOthers { get; set; }
        public double? totalFixedDeduction { get; set; }
        #endregion

        #region Allowances
        public double? rataAmount { get; set; }
        public double? dailyAllowance { get; set; }
        public double? communicationAllowance { get; set; }
        public double? travelAllowance { get; set; }
        public double? gasAllowance { get; set; }
        public double? riceAllowanceAmount { get; set; }
        public double? laundryAllowance { get; set; }
        public double? uniformAllowance { get; set; }
        public double? medicineAllowance { get; set; }
        public double? medicalAllowance { get; set; }
        public double? otherAllowance { get; set; }
        public double? totalAllowance { get; set; }
        public double? allowanceNonTaxable { get; set; }
        public double? allowanceTaxable { get; set; }
        #endregion

        #region Mandatory Deductions
        public double? deductionSSSemployee { get; set; }
        public double? deductionWISPemployee { get; set; }
        public double? deductionWISPemployer { get; set; }
        public double? deductionSSSemployer { get; set; }
        public double? deductionSSSec { get; set; }
        public double? deductionPHIemployee { get; set; }
        public double? deductionPHIemployer { get; set; }
        public double? deductionPIFemployee { get; set; }
        public double? deductionPIFemployer { get; set; }
        public double? deductionPFemployee { get; set; }
        public double? deductionPFemployer { get; set; }
        public double? totalMandatory { get; set; }
        #endregion

        #region Loans / Other Deductions
        public double? amountLoan { get; set; }
        public double? sssLoan { get; set; }
        public double? hdmfLoan { get; set; }
        public double? cashadvance { get; set; }
        public double? acdiLoan { get; set; }
        public double? prulife { get; set; }
        public double? telephone { get; set; }
        public double? sssCalamity { get; set; }
        public double? hdmfCalamity { get; set; }
        public double? csbLoan { get; set; }
        public double? sbLoan { get; set; }
        public double? hmoLoan { get; set; }
        public double? employeeLedger { get; set; }
        public double? otherLoan1 { get; set; }
        public double? otherLoan2 { get; set; }
        public double? otherLoan3 { get; set; }
        public double? otherLoan4 { get; set; }
        public double? otherEmployeeReceivable { get; set; }
        public double? otherEmployeePayable { get; set; }
        public double? leaveCount { get; set; }
        public double? leaveAmount { get; set; }
        public double? v13thMonth { get; set; }
        #endregion

        #region Audit Fields
        public string statusName { get; set; }
        public string? dtStatus { get; set; }
        public string statusByUser { get; set; }
        public string payrollBy { get; set; }

        public bool? isActive { get; set; }
        public string? dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; }
        public string? dtDeleted { get; set; }
        public string deletedByUser { get; set; }
        #endregion

        public PayrollProcessModel()
        {
            // Rates / Pay
            hourlyRate = 0;
            minuteRate = 0;
            basicMonthlyPay = 0;
            dailyRate = 0;
            basicPay = 0;
            nonBasicPay = 0;
            basicPaySemi = 0;

            // Render Hours
            render = 0;
            renderOT = 0;
            renderNSD = 0;
            renderNSDOT = 0;
            renderREST = 0;
            renderRESTOT = 0;
            renderNSDREST = 0;
            renderNSDRESTOT = 0;
            renderS = 0;
            renderOTS = 0;
            renderNSDS = 0;
            renderNSDOTS = 0;
            renderL = 0;
            renderOTL = 0;
            renderNSDL = 0;
            renderNSDOTL = 0;
            renderRESTS = 0;
            renderRESTOTS = 0;
            renderNSDRESTS = 0;
            renderNSDRESTOTS = 0;
            renderRESTL = 0;
            renderRESTOTL = 0;
            renderNSDRESTL = 0;
            renderNSDRESTOTL = 0;

            // Render Amounts
            amount = 0;
            amountOT = 0;
            amountNSD = 0;
            amountNSDOT = 0;
            amountREST = 0;
            amountRESTOT = 0;
            amountNSDREST = 0;
            amountNSDRESTOT = 0;
            amountRESTS = 0;
            amountRESTOTS = 0;
            amountNSDRESTS = 0;
            amountNSDRESTOTS = 0;
            amountRESTL = 0;
            amountRESTOTL = 0;
            amountNSDRESTL = 0;
            amountNSDRESTOTL = 0;

            // Leave / Special / Sunday
            amountL = 0;
            amountOTL = 0;
            amountNSDL = 0;
            amountNSDOTL = 0;
            amountS = 0;
            amountOTS = 0;
            amountNSDS = 0;
            amountNSDOTS = 0;

            // Attendance
            absentCount = 0;
            presentCount = 0;
            wfhLeave = 0;
            paidHoliday = 0;
            absentAmount = 0;
            presentAmount = 0;

            // Totals
            renderEarly = 0;
            renderLate = 0;
            renderUndertime = 0;
            renderOvertime = 0;
            amountEarly = 0;
            amountLate = 0;
            amountUndertime = 0;
            totalDeductionLateUndertimeAbsent = 0;
            otherIncome = 0;
            totalGrossPay = 0;
            totalGrossPayMandatory = 0;
            grossIncome = 0;
            taxableIncome = 0;
            withHeldTax = 0;
            totalDeduction = 0;
            totalNetPay = 0;

            // Allowances
            rataAmount = 0;
            dailyAllowance = 0;
            communicationAllowance = 0;
            travelAllowance = 0;
            gasAllowance = 0;
            riceAllowanceAmount = 0;
            laundryAllowance = 0;
            uniformAllowance = 0;
            medicineAllowance = 0;
            medicalAllowance = 0;
            otherAllowance = 0;
            totalAllowance = 0;
            allowanceNonTaxable = 0;
            allowanceTaxable = 0;

            // Mandatory Deductions
            deductionSSSemployee = 0;
            deductionSSSemployer = 0;
            deductionSSSec = 0;
            deductionPHIemployee = 0;
            deductionPHIemployer = 0;
            deductionPIFemployee = 0;
            deductionPIFemployer = 0;
            deductionPFemployee = 0;
            deductionPFemployer = 0;
            totalMandatory = 0;

            // Loans / Other Deductions
            amountLoan = 0;
            sssLoan = 0;
            hdmfLoan = 0;
            cashadvance = 0;
            acdiLoan = 0;
            prulife = 0;
            telephone = 0;
            sssCalamity = 0;
            hdmfCalamity = 0;
            csbLoan = 0;
            sbLoan = 0;
            hmoLoan = 0;
            employeeLedger = 0;
            otherLoan1 = 0;
            otherLoan2 = 0;
            otherLoan3 = 0;
            otherLoan4 = 0;
            otherEmployeeReceivable = 0;
            otherEmployeePayable = 0;
            leaveCount = 0;
            leaveAmount = 0;

            v13thMonth = 0;
        }

    }

    public class AdjustmentList()
    {
        public double? adjustmentAmount { get; set; }
        public int? isTaxableAdj { get; set; }
        public int? payableID { get; set; }
    }
    public class AllowanceList()
    {
        public double? allowanceAmount { get; set; }
        public double? toDeduct { get; set; }
        public double? allowanceDailyRate { get; set; }
        public double? allowanceHourlyRate { get; set; }
        public int? isTaxableAllowance { get; set; }
        public int? allowanceID { get; set; }
        public string? allowanceCode { get; set; }
        public string? basis { get; set; }
    }

    public class LoanList()
    {
        public double? amortizationAmount { get; set; }
        public int? loanID { get; set; }
        public string? loanCode { get; set; }
        public string? deductionSchedule { get; set; }
        public string? dateGranted { get; set; }
        public string? deductionStartDate { get; set; }
        public double? loanBalance { get; set; }
        public double? principalAmount { get; set; }
    }

    public class FixedDeductionList
    {
        public int id { get; set; }
        public string? employeeNo { get; set; }
        public string? fixedDeductionCode { get; set; }
        public double? fixedDeductionAmount { get; set; }
        public string? fixedDeductionDateStart { get; set; }
        public string? deductionSchedule { get; set; }
    }

    public class DeductionList()
    {
        public double? deductionAmount { get; set; }
        public int? isTaxableDed { get; set; }
        public int? receivableID { get; set; }
    }

    public class SalaryRates
    {
        // Regular
        public double RegularDuty { get; set; }
        public double RegularOT { get; set; }
        public double RegularND { get; set; }
        public double RegularOTND { get; set; }

        // Rest Day
        public double RD { get; set; }
        public double RDMonthly { get; set; }
        public double RDOT { get; set; }
        public double RDND { get; set; }
        public double RDOTND { get; set; }

        // Special Holiday
        public double SH { get; set; }
        public double SHOT { get; set; }
        public double SHND { get; set; }
        public double SHOTND { get; set; }

        // Rest Day + Special Holiday
        public double RDSH { get; set; }
        public double RDSHOT { get; set; }
        public double RDSHND { get; set; }
        public double RDSHOTND { get; set; }

        // Regular Holiday
        public double RH { get; set; }
        public double RHOT { get; set; }
        public double RHND { get; set; }
        public double RHOTND { get; set; }

        // Rest Day + Regular Holiday
        public double RDRH { get; set; }
        public double RDRHOT { get; set; }
        public double RDRHND { get; set; }
        public double RDRHOTND { get; set; }

        public void ApplyNoOTPremium()
        {
            RegularDuty = 100;
            RegularOT = 100;
            RegularND = 100;
            RegularOTND = 100;

            RD = 100;
            RDMonthly = 100;
            RDOT = 100;
            RDND = 100;
            RDOTND = 100;

            SH = 100;
            SHOT = 100;
            SHND = 100;
            SHOTND = 100;

            RDSH = 100;
            RDSHOT = 100;
            RDSHND = 100;
            RDSHOTND = 100;

            RH = 100;
            RHOT = 100;
            RHND = 100;
            RHOTND = 100;

            RDRH = 100;
            RDRHOT = 100;
            RDRHND = 100;
            RDRHOTND = 100;
        }
    }


}