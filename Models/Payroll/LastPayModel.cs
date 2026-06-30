using System.ComponentModel.DataAnnotations;

namespace KEISAN_HRIS_v2.Models.Payroll
{
    public class LastPayModel
    {
        public string? employeeNo { get; set; }
        public string? employmentStatus { get; set; }
        public string? dateHired { get; set; }
        public string? dateResigned { get; set; }
        public string? cutOffType { get; set; }
        public string? cutOffTypeCode { get; set; }
        public string? dateMonth { get; set; }
        public string? dateYear { get; set; }
        public string? statusName { get; set; }
        public string? remarks { get; set; }
        public double? otherEmployeePayable { get; set; }
        public double? otherEmployeeReceivable { get; set; }
        public double? amount_netpay { get; set; }
        public double? amount_adjustment { get; set; }
        public double? amount_deduction { get; set; }
        public double? amount_13thmonth { get; set; }
        public double? amount_taxRefund { get; set; }
        public double? amount_sl { get; set; }
        public double? amount_vl { get; set; }
        public double? lastPayAmount { get; set; }
    }

}