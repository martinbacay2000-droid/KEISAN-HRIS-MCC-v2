namespace KEISAN_HRIS_v2.Models.DisciplinaryAction
{
    public class DisciplinaryActionModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; }
        public string offense { get; set; }
        public string complainant { get; set; }
        public string section { get; set; }
        public string disciplinaryReason { get; set; }
        public string disciplinaryAction { get; set; }
        public string occurrence { get; set; }
        public string penalty { get; set; }
        public string marMonth { get; set; }
        public string marYear { get; set; }
        public string dateAbsent { get; set; }
        public string dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string dtModified { get; set; }
        public string modifiedByUser { get; set; }
        public string isActive { get; set; }
        public string dtDeleted { get; set; }
        public string deletedByUser { get; set; }
        public string dtReceived { get; set; }
        public string dtReply { get; set; }
        public string reply { get; set; }
        public string suspensionDays { get; set; }
        public string suspensionPeriod { get; set; }
        public string monthOfDeduction { get; set; }
        public string statusName { get; set; }
        public string dtStatus { get; set; }
        public string statusByUser { get; set; }
        public string dateIssued { get; set; }
        public string caseAction { get; set; }
        public string tableName { get; set; }
        public string itemDesc { get; set; }
    }

    public class DisciplinaryActionHistoryModel
    {
        public int id { get; set; }
        public int disciplinaryID { get; set; }
        public string fromAction { get; set; }
        public string toAction { get; set; }
        public string user { get; set; }
        public string dtAdded { get; set; }
        public int isActive { get; set; }
        public string status { get; set; }
    }

}
