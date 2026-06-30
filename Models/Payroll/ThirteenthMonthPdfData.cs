namespace KEISAN_HRIS_v2.Models.Payroll
{
    public class ThirteenthMonthPdfData
    {
        public string? EmployeeNo { get; set; }
        public string? FullName { get; set; }
        public string? PositionName { get; set; }
        public string? Department { get; set; }
        public string? DateHired { get; set; }
        public string? DateResigned { get; set; }
        public double TotalAmount { get; set; }
    }

    public class ThirteenthMonthLineItem
    {
        public string? DateYear { get; set; }
        public string? DateMonth { get; set; }
        public string? CutoffType { get; set; }
        public double BasicPay { get; set; }
        public double Absent { get; set; }
        public double Late { get; set; }
        public double Undertime { get; set; }
        public double BasicAllowance { get; set; }
        public double AllowanceTardyUndertimeAbsent { get; set; }
        public double Adjustment { get; set; }
        public double ThirteenthMonthPay { get; set; }
    }
}