using System;

namespace KEISAN_HRIS_v2.Models.LERelation
{
    public class CommendationListModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public string commendationCode { get; set; } = string.Empty;
        public string activity { get; set; } = string.Empty;
        public DateTime? dateissued { get; set; }
        public string issuedBy { get; set; } = string.Empty;
        public string remarks { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public DateTime? dtModified { get; set; }
        public string ModifiedBy { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedBy { get; set; } = string.Empty;

        // Additional properties for display purposes
        public string employeeName { get; set; } = string.Empty;
        public string commendationType { get; set; } = string.Empty;
    }

    public class EmployeeModel
    {
        public string employeeNo { get; set; } = string.Empty;
        public string employeeName { get; set; } = string.Empty;
    }

    public class CommendationTypeModel
    {
        public string commendationCode { get; set; } = string.Empty;
        public string commendationName { get; set; } = string.Empty;
    }

    public class AttachmentModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public string attachmentDescription { get; set; } = string.Empty;
        public string attachmentTypeCode { get; set; } = string.Empty;
        public string attachmentPath { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
    }

    public class DisciplinaryActionModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public string offense { get; set; } = string.Empty;
        public string complainant { get; set; } = string.Empty;
        public string section { get; set; } = string.Empty;
        public string disciplinaryReason { get; set; } = string.Empty;
        public string disciplinaryAction { get; set; } = string.Empty;
        public string penalty { get; set; } = string.Empty;
        public DateTime? dateIssued { get; set; }
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtModified { get; set; }
        public string modifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;

        // Display-only
        public string employeeName { get; set; } = string.Empty;
    }
}