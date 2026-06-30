namespace KEISAN_HRIS_v2.Models.AuditTrail
{
    public class AuditTrailModel
    {
        public int id { get; set; }
        public string tableName { get; set; } = string.Empty;
        public int referenceID { get; set; }
        public string action { get; set; } = string.Empty;
        public string remarks { get; set; } = string.Empty;
        public string usercode { get; set; } = string.Empty;// make this null for now since i dont track yet the user who logged in (NO SESESSIONS YET)
        public DateTime dtAdded { get; set; }
    }
}