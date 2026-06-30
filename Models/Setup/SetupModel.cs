namespace KEISAN_HRIS_v2.Models.Setup
{
    public class employmentStatus
    {
        public int Id { get; set; }
        public string employmentStatusCode { get; set; }
        public string employmentStatusName { get; set; }

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

    public class AllowanceListModel
    {
        public int id { get; set; }
        public string allowanceCode { get; set; } = string.Empty;
        public string allowanceName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public bool isTaxable { get; set; } = false;
        public double amount { get; set; } = 0;
        public string basis { get; set; } = string.Empty;
        public string basisDate { get; set; } = string.Empty;
        public string employmentStatus { get; set; } = string.Empty;
        public string positionCode { get; set; } = string.Empty;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class AttachmentTypeModel
    {
        public int id { get; set; }
        public string attachmentTypeCode { get; set; } = string.Empty;
        public string attachmentTypeName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class BankListModel
    {
        public int id { get; set; }
        public string bankCode { get; set; } = string.Empty;
        public string bankName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class DepartmentListModel
    {
        public int id { get; set; }
        public string departmentCode { get; set; } = string.Empty;
        public string departmentName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public string SAPdeptCode { get; set; } = string.Empty;
        public string SAPdeptName { get; set; } = string.Empty;
    }

    public class EmploymentStatusListModel
    {
        public int id { get; set; }
        public string employmentStatusCode { get; set; } = string.Empty;
        public string employmentStatusName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class LeaveListModel
    {
        public int id { get; set; }
        public string leaveCode { get; set; } = string.Empty;
        public string leaveName { get; set; } = string.Empty;
        public double leaveCredits { get; set; } = 0;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public string annual { get; set; } = string.Empty;
        public string requestType { get; set; } = string.Empty;
    }

    public class OtherDeductionListModel
    {
        public int id { get; set; }
        public string otherDeductionCode { get; set; } = string.Empty;
        public string otherDeductionName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public bool isTaxable { get; set; } = false;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    //table name s_branch
    public class BranchListModel
    {
        public int id { get; set; }
        public string branchCode { get; set; } = string.Empty;
        public string branchName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public string SAPbranchCode { get; set; } = string.Empty;
        public string SAPbranchName { get; set; } = string.Empty;

    }

    public class CommendationModel
    {
        public int id { get; set; }
        public string commendationCode { get; set; } = string.Empty;
        public string commendationName { get; set; } = string.Empty;
        public DateTime? dtAdded { get; set; }
        public DateTime? dtModified { get; set; }
        public string modifiedBy { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
    }

    public class LoanListModel
    {
        public int id { get; set; }
        public string loanCode { get; set; } = string.Empty;
        public string loanName { get; set; } = string.Empty;
        public string loanType { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public int interestPercent { get; set; } = 0;
    }

    public class RankListModel
    {
        public int id { get; set; }
        public string rankCode { get; set; } = string.Empty;
        public string rankName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class ReasonForSeparationListModel
    {
        public int id { get; set; }
        public string reason4TerminationCode { get; set; } = string.Empty;
        public string reason4TerminationName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class PositionListModel
    {
        public int id { get; set; }
        public string positionCode { get; set; } = string.Empty;
        public string positionName { get; set; } = string.Empty;
        public double gracePeriod { get; set; } = 0;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public bool appraisal { get; set; } = false;
    }

    public class COEPurposeListModel
    {
        public int id { get; set; }
        public string coeCode { get; set; } = string.Empty;
        public string coeName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class UnitListModel
    {
        public int id { get; set; }
        public string unitCode { get; set; } = string.Empty;
        public string unitName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class ProjectListModel
    {
        public int id { get; set; }
        public string projectCode { get; set; } = string.Empty;
        public string projectName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dateFrom { get; set; }
        public DateTime? dateTo { get; set; }
        public TimeSpan? projectTimeIN { get; set; }
        public TimeSpan? projectTimeOUT { get; set; }
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }
    //Table name s_holiday
    public class HolidayListModel
    {
        public int id { get; set; }
        public string holidayName { get; set; } = string.Empty;
        public DateTime? holidayDate { get; set; }
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public string holidayType { get; set; } = string.Empty;
        public string branchCode { get; set; } = string.Empty;
        public DateTime? absentDay { get; set; }
    }

    public class UserListModel
    {
        public int id { get; set; }
        public string userCode { get; set; } = string.Empty;
        public string username { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
        public string lastName { get; set; } = string.Empty;
        public string firstName { get; set; } = string.Empty;
        public string middleName { get; set; } = string.Empty;
        public string roleCode { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public string positionName { get; set; } = string.Empty;
        public bool islock { get; set; } = false;
        public int attempt { get; set; } = 0;
        public bool isScheduleUploader { get; set; } = false;
        public string sbuaccess { get; set; } = string.Empty;


        public string employeeNo { get; set; } = string.Empty;
        public string employeeName { get; set; } = string.Empty;
        public string employmentStatus { get; set; } = string.Empty;
        public string branchCode { get; set; } = string.Empty;
        public string departmentCode { get; set; } = string.Empty;
        public string positionCode { get; set; } = string.Empty;
        public string branchName { get; set; } = string.Empty;
        public string departmentName { get; set; } = string.Empty;
        public string employmentStatusName { get; set; } = string.Empty;
    }

    public class UserRoleListModel
    {
        public int id { get; set; }
        public string roleCode { get; set; } = string.Empty;
        public string roleName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class AnnouncementListModel
    {
        public int id { get; set; }
        public string announcementTitle { get; set; } = string.Empty;
        public string announcement { get; set; } = string.Empty;
        public DateTime? dateStart { get; set; }
        public DateTime? dateEnd { get; set; }
        public bool isActive { get; set; } = true;
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtAdded { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public string dtLastModified { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
    }

    public class LocationListModel
    {
        public int id { get; set; }
        public string locationCode { get; set; } = string.Empty;
        public string locationName { get; set; } = string.Empty;
        public double gracePeriod { get; set; } = 0;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public bool appraisal { get; set; } = false;
    }

    public class RoleListModel
    {
        public int id { get; set; }
        public string roleCode { get; set; } = string.Empty;
        public string roleName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;
        public string ModuleCode { get; set; }
        public string AccessLevel { get; set; } // NO_ACCESS | VIEW | EDIT | FULL
    }

    public class RoleDataScopeModel
    {
        public int Id { get; set; }
        public string RoleCode { get; set; }
        public string ScopeType { get; set; }
        public string AllowedRanks { get; set; }
        public string AllowedBranches { get; set; }
        public string AllowedDepartments { get; set; }
        public string AllowedPositions { get; set; }
        public string AllowedEmploymentStatuses { get; set; }
        public bool IsActive { get; set; }
    }

    public class RoleDataScopeSaveModel
    {
        public string RoleCode { get; set; }
        public string ScopeType { get; set; }
        public List<string> AllowedRanks { get; set; }
        public List<string> AllowedBranches { get; set; }
        public List<string> AllowedDepartments { get; set; }
        public List<string> AllowedPositions { get; set; }
        public List<string> AllowedEmploymentStatuses { get; set; }
    }

    public class RoleAccessItem
    {
        public string ModuleCode { get; set; }
        public string ModuleName { get; set; }
        public string ModuleType { get; set; }
        public string AccessLevel { get; set; }
    }

    public class RoleAccessSaveModel
    {
        public string RoleCode { get; set; }
        public List<RoleAccessItem> Items { get; set; }
    }

    public class RoleHiddenEmployeesSaveModel
    {
        public string RoleCode { get; set; }
        public List<string> HiddenEmployees { get; set; }
    }

    public class ScheduleTypeListModel
    {
        public int id { get; set; }
        public string scheduleTypeCode { get; set; } = string.Empty;
        public string scheduleTypeName { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public string? deletedByUser { get; set; }
    }
}