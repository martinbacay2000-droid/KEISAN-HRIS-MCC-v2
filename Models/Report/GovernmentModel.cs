namespace KEISAN_HRIS_v2.Models.Report
{
    public class GovernmentModel
    {
        //sss
        public string? employeeNo { get; set; }
        public string? employeeName { get; set; }
        public string? departmentCode { get; set; }
        public string? dateMonth { get; set; }
        public string? dateYear { get; set; }
        public string? sssNo { get; set; }
        public string? deductionSSSemployee { get; set; }
        public string? deductionSSSemployer { get; set; }
        public string? deductionSSSec { get; set; }
        public string? deductionSSSTotal { get; set; }
        public string? sssLoan { get; set; }
        //pagibig
        public string? hdmfNo { get; set; }
        public string? tinNo { get; set; }
        public string? deductionPIFemployee { get; set; }
        public string? deductionPIFemployer { get; set; }
        public string? deductionPIFTotal { get; set; }
        public string? dateOfBirth { get; set; }
        public string? hdmfLoan { get; set; }
        //philhealth
        public string? philHealthNo { get; set; }
        public string? totalPay { get; set; }
        public string? round { get; set; }
        public string? deductionPHIemployee { get; set; }
        public string? deductionPHIemployer { get; set; }
        public string? deductionPHITotal { get; set; }
        //tax
        public string? grossCompensation { get; set; }
        public string? totalMandatory { get; set; }
        public string? taxableIncome { get; set; }
        public string? withHeldTax { get; set; }
        public decimal SssCalamity { get; set; }   // for SSSreport
        public decimal HdmfCalamity { get; set; }  // for PIFreport
    }

    public class PhilhealthORModel
    {
        public int id { get; set; }
        public string? dateMonth { get; set; }
        public string? dateYear { get; set; }
        public string? OR { get; set; }
        public string? dateOfPayment { get; set; }
        public string? branchCode { get; set; }
        public byte? isActive { get; set; }
        public DateTime? dtAdded { get; set; }
        public string? addedByUser { get; set; }
        public DateTime? dtLastModified { get; set; }
        public string? lastModifiedByUser { get; set; }
    }
}
