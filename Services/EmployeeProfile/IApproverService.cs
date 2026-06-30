namespace KEISAN_HRIS_v2.Services.EmployeeProfile
{
    public interface IApproverService
    {
        Task<ApproverInfo> GetApproverInfoAsync(string employeeNo);
        Task<ApproverPermission> CanApproveForEmployeeAsync(string approverNo, string targetEmployeeNo);

        // NEW: Returns all distinct approver levels assigned to a specific employee
        // e.g., if employee has Level 2, 3, 4 approvers → returns [2, 3, 4]
        Task<List<int>> GetRequiredApprovalLevelsAsync(string targetEmployeeNo);

        // NEW: Returns the approver level of a specific approver for a specific employee
        // Returns null if this person is not an approver for that employee
        Task<int?> GetApproverLevelForEmployeeAsync(string approverNo, string targetEmployeeNo);
    }

    public class ApproverInfo
    {
        public bool IsApprover { get; set; }
        public HashSet<string> ManagedEmployees { get; set; } = new();
    }

    public class ApproverPermission
    {
        public bool CanApprove { get; set; }
        public int? ApproverLevel { get; set; }
    }

    // NEW: Holds the full multi-level approval state of a request
    public class ApprovalState
    {
        // Which levels are required for this employee (e.g., [2, 3, 4])
        public List<int> RequiredLevels { get; set; } = new();

        // Which levels have already been approved
        public List<int> ApprovedLevels { get; set; } = new();

        // Which levels have been declined
        public List<int> DeclinedLevels { get; set; } = new();

        // The next level that needs to approve (null if all done)
        public int? NextRequiredLevel => RequiredLevels
            .Where(l => !ApprovedLevels.Contains(l) && !DeclinedLevels.Contains(l))
            .OrderBy(l => l)
            .FirstOrDefault() == 0 ? null :
            RequiredLevels
            .Where(l => !ApprovedLevels.Contains(l) && !DeclinedLevels.Contains(l))
            .OrderBy(l => l)
            .Cast<int?>()
            .FirstOrDefault();

        // True if ALL required levels are approved
        public bool IsFullyApproved => RequiredLevels.Count > 0 &&
                                       RequiredLevels.All(l => ApprovedLevels.Contains(l));

        // True if ANY level is declined
        public bool IsDeclined => DeclinedLevels.Count > 0;

        // Highest required level (this is the final gate)
        public int HighestLevel => RequiredLevels.Count > 0 ? RequiredLevels.Max() : 4;

        // Display-friendly status for the requestor
        public string DisplayStatus
        {
            get
            {
                if (IsDeclined) return "Declined";
                if (IsFullyApproved) return "Approved";
                if (ApprovedLevels.Count > 0) return "Partially Approved";
                return "Pending";
            }
        }
    }
}