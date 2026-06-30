using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace KEISAN_HRIS_v2.Models.Memo
{
    public class MemoModel
    {
        public int Id { get; set; }
        public string SeriesNo { get; set; }
        public string Title { get; set; }
        public string EffectivityDate { get; set; }
        public string Remarks { get; set; }
        public List<IFormFile> Attachments { get; set; }

        // Recipient rule — stored directly on ad_memo
        // RecipientType  : ALL | INDIVIDUAL | EMPLOYMENT_STATUS | BRANCH | DEPARTMENT | RANK
        // RecipientTypeCode:
        //   INDIVIDUAL        → comma-separated employeeNos  e.g. "EMP001,EMP002"
        //   EMPLOYMENT_STATUS → status code                  e.g. "REGULAR"
        //   BRANCH            → branch code                  e.g. "BR-001"
        //   DEPARTMENT        → department code              e.g. "DEPT-HR"
        //   RANK              → position code                e.g. "POS-001"
        //   ALL               → empty / null
        public string RecipientType { get; set; }
        public string RecipientTypeCode { get; set; }
    }

    public class MemoModelList
    {
        public int id { get; set; }
        public string seriesNo { get; set; }
        public string title { get; set; }
        public string effectivityDate { get; set; }
        public string remarks { get; set; }
        public string createdBy { get; set; }
        public string recipientType { get; set; }
        public string recipientTypeCode { get; set; }
    }
}