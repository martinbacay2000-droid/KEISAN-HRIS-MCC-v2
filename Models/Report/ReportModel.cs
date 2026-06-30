namespace KEISAN_HRIS_v2.Models.Report
{
    public class LeaveBalanceModel
    {
        public string employeeNo { get; set; }
        public string fullName { get; set; }
        public string branchName { get; set; }
        public string departmentName { get; set; }
        public double sl { get; set; }
        public double vl { get; set; }
        public double cto { get; set; }
    }

}
