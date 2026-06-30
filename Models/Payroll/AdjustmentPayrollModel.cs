namespace KEISAN_HRIS_v2.Models.Payroll
{
    public class AdjustmentPayrollModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public string adjustmentCode { get; set; } = string.Empty;
        public double amount { get; set; } = 0;
        public string statusName { get; set; } = string.Empty;
        public DateTime? dtStatus { get; set; }
        public string statusByUser { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public string requestedByUser { get; set; } = string.Empty;
        public double approvedAmount { get; set; } = 0;
        public DateTime? dateToAdjustment { get; set; }
        public string reason { get; set; } = string.Empty;
        public DateTime? dtRecorded { get; set; }
        public string methodType { get; set; } = string.Empty;
        public string cutOffType { get; set; } = string.Empty;
        public string dateMonth { get; set; } = string.Empty;
        public double dateYear { get; set; } = 0;
        public DateTime? dateFrom { get; set; }
        public DateTime? dateTo { get; set; }
        public double noOfDays { get; set; } = 0;
        public string eventName { get; set; } = string.Empty;
        public double retroBasic { get; set; } = 0;
        public string DayType { get; set; } = string.Empty;
        public string HourType { get; set; } = string.Empty;
        public double Value { get; set; } = 0;
        public string Units { get; set; } = string.Empty;
    }

    public class AdjustmentListModel
    {
        public int id { get; set; }
        public string adjustmentCode { get; set; } = string.Empty;
        public string adjustmentName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public bool isTaxable { get; set; } = false;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class AdjustmentSalaryRatesModel
    {
        public int id { get; set; }
        public DateTime? effectivityDate { get; set; }
        public double RegularDuty { get; set; } = 0;
        public double RegularOT { get; set; } = 0;
        public double RegularND { get; set; } = 0;
        public double RegularOTND { get; set; } = 0;
        public double RD { get; set; } = 0;
        public double RDMonthly { get; set; } = 0;
        public double RDOT { get; set; } = 0;
        public double RDND { get; set; } = 0;
        public double RDOTND { get; set; } = 0;
        public double SH { get; set; } = 0;
        public double SHMonthly { get; set; } = 0;
        public double SHOT { get; set; } = 0;
        public double SHND { get; set; } = 0;
        public double SHOTND { get; set; } = 0;
        public double RDSH { get; set; } = 0;
        public double RDSHOT { get; set; } = 0;
        public double RDSHND { get; set; } = 0;
        public double RDSHOTND { get; set; } = 0;
        public double RH { get; set; } = 0;
        public double RHOT { get; set; } = 0;
        public double RHND { get; set; } = 0;
        public double RHOTND { get; set; } = 0;
        public double RDRH { get; set; } = 0;
        public double RDRHOT { get; set; } = 0;
        public double RDRHND { get; set; } = 0;
        public double RDRHOTND { get; set; } = 0;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class AdjustmentPayrollDetailsModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public bool isMinimumWageEarner { get; set; } = false;
        public double fixedNetPay { get; set; } = 0;
        public double meritServicePay { get; set; } = 0;
        public double basicSalary { get; set; } = 0;
        public double basicMonthlyPay { get; set; } = 0;
        public double dailyRate { get; set; } = 0;
        public double hourlyRate { get; set; } = 0;
        public DateTime? effectivityDate { get; set; }
        public string payrollBasis { get; set; } = string.Empty;
        public string payrollType { get; set; } = string.Empty;
        public double mp2 { get; set; } = 0;
        public double contrPIFadditional { get; set; } = 0;
        public string tinNo { get; set; } = string.Empty;
        public string sssNo { get; set; } = string.Empty;
        public string philHealthNo { get; set; } = string.Empty;
        public string hdmfNo { get; set; } = string.Empty;
        public string bankType { get; set; } = string.Empty;
        public string bankCode { get; set; } = string.Empty;
        public string accountNo { get; set; } = string.Empty;
        public bool isNoLate { get; set; } = false;
        public bool isNoOTPremium { get; set; } = false;
        public string payrollGroup { get; set; } = string.Empty;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }
}
