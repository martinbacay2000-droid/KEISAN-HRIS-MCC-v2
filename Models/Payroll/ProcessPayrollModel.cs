namespace KEISAN_HRIS_v2.Models.Payroll
{
    public class ProcessPayrollModel
    {
        // Primary Key
        public int id { get; set; }
        // Employee Info
        public string? employeeNo { get; set; }
        public string? fullName { get; set; }
        public string? requestIn { get; set; }
        public string? requestOut { get; set; }
        public string? reason { get; set; }
        public string? dateRequested { get; set; }
        public string? statusName { get; set; }

    }
    
}
