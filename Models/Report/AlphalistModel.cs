namespace KEISAN_HRIS_v2.Models.Report
{
    public class AlphalistModel
    {
            public string employeeNo { get; set; }
            public string employeeName { get; set; }

            public string totalBasicSalary { get; set; }
            public string b13thMonthPay { get; set; }
            public string totalDeminimis { get; set; }

            public string totalSSS { get; set; }
            public string totalPHI { get; set; }
            public string totalHDMF { get; set; }
            public string totalMandatory { get; set; }

            public string salaryAndOtherCompensation { get; set; }
            public string totalNonTaxableCompensationIncome { get; set; }
            public string totalGrossCompensationIncome { get; set; }

            public string taxableBasic { get; set; }
            public string totalTaxable13monthbonus { get; set; }
            public string netTaxableBasicSalary { get; set; }

            // Previous Employer
            public string prevEmployerBasicsalary { get; set; }
            public string prevEmployerBenefitsand13thmonth { get; set; }
            public string prevEmployerDeminimis { get; set; }
            public string prevEmployerTotalMandatory { get; set; }
            public string prevEmployerOtherNonTax { get; set; }
            public string prevEmployerTaxableBasic { get; set; }
            public string prevEmployerTaxWithHeld { get; set; }
            public string prevEmployerNonTaxable13monthAdjustment { get; set; }
            public string prevEmployerTaxable13monthAdjustment { get; set; }
            public string prevEmployerNetTaxableBasic { get; set; }

            // Combined Prev + Present
            public string totalNontaxable13monthBenefitsPrevPresentEmployer { get; set; }
            public string netTaxableCompensationPrevPresentEmployer { get; set; }

            // Tax Computation
            public string taxDue { get; set; }
            public string TotalWithHoldingTax { get; set; }
            public string taxDueRefund { get; set; }
            public string issuedTaxDueRefund { get; set; }

            // Derived
            public string totalTaxWithHeldAdjusted { get; set; }
       
    }

}
