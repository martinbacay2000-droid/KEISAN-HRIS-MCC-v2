using Dapper;
using System.Data;

namespace KEISAN_HRIS_v2.Services.EmployeeProfile
{
    public class ApproverService : IApproverService
    {
        private readonly IDbConnection _db;

        public ApproverService(IDbConnection db)
        {
            _db = db;
        }

        public async Task<ApproverInfo> GetApproverInfoAsync(string employeeNo)
        {
            var sql = @"
                SELECT DISTINCT b.employeeNo
                FROM e_approver a
                INNER JOIN e_basicinfo b ON (
                    CASE a.typeList
                        WHEN 1 THEN b.employmentStatus = a.employeeNo
                        WHEN 2 THEN b.branchCode = a.employeeNo
                        WHEN 3 THEN b.departmentCode = a.employeeNo
                        WHEN 4 THEN b.positionCode = a.employeeNo
                        WHEN 5 THEN a.employeeNo = 'ALL'
                        WHEN 6 THEN b.employeeNo = a.employeeNo
                        ELSE FALSE
                    END
                )
                WHERE a.approverNo = @employeeNo
                AND a.isActive = 1
                AND b.isActive = 1";

            var managedEmployees = await _db.QueryAsync<string>(sql, new { employeeNo });
            var employeeSet = managedEmployees.ToHashSet();

            return new ApproverInfo
            {
                IsApprover = employeeSet.Count > 0,
                ManagedEmployees = employeeSet
            };
        }

        public async Task<ApproverPermission> CanApproveForEmployeeAsync(
            string approverNo,
            string targetEmployeeNo)
        {
            var sql = @"
                SELECT a.approverLevel
                FROM e_approver a
                INNER JOIN e_basicinfo b ON (
                    CASE a.typeList
                        WHEN 1 THEN b.employmentStatus = a.employeeNo
                        WHEN 2 THEN b.branchCode = a.employeeNo
                        WHEN 3 THEN b.departmentCode = a.employeeNo
                        WHEN 4 THEN b.positionCode = a.employeeNo
                        WHEN 5 THEN a.employeeNo = 'ALL'
                        WHEN 6 THEN b.employeeNo = a.employeeNo
                        ELSE FALSE
                    END
                )
                WHERE a.approverNo = @approverNo
                AND b.employeeNo = @targetEmployeeNo
                AND a.isActive = 1
                LIMIT 1";

            var level = await _db.QueryFirstOrDefaultAsync<int?>(
                sql, new { approverNo, targetEmployeeNo });

            return new ApproverPermission
            {
                CanApprove = level.HasValue,
                ApproverLevel = level
            };
        }

        // NEW: Get all distinct approver levels that have been assigned to a specific employee.
        // Example: If employee Vince has Juan(L2), Jason(L3), John(L4) → returns [2, 3, 4]
        // This tells us which statusLevelX fields matter for this employee's request.
        public async Task<List<int>> GetRequiredApprovalLevelsAsync(string targetEmployeeNo)
        {
            var sql = @"
                SELECT DISTINCT a.approverLevel
                FROM e_approver a
                INNER JOIN e_basicinfo b ON (
                    CASE a.typeList
                        WHEN 1 THEN b.employmentStatus = a.employeeNo
                        WHEN 2 THEN b.branchCode = a.employeeNo
                        WHEN 3 THEN b.departmentCode = a.employeeNo
                        WHEN 4 THEN b.positionCode = a.employeeNo
                        WHEN 5 THEN a.employeeNo = 'ALL'
                        WHEN 6 THEN b.employeeNo = a.employeeNo
                        ELSE FALSE
                    END
                )
                WHERE b.employeeNo = @targetEmployeeNo
                AND a.isActive = 1
                AND b.isActive = 1
                ORDER BY a.approverLevel ASC";

            var levels = await _db.QueryAsync<int>(sql, new { targetEmployeeNo });
            return levels.Where(l => l >= 1 && l <= 4).ToList();
        }

        // NEW: Get the specific approver level for a given approver→employee relationship.
        // Returns null if the approver has no assignment for this employee.
        public async Task<int?> GetApproverLevelForEmployeeAsync(
            string approverNo,
            string targetEmployeeNo)
        {
            var sql = @"
                SELECT a.approverLevel
                FROM e_approver a
                INNER JOIN e_basicinfo b ON (
                    CASE a.typeList
                        WHEN 1 THEN b.employmentStatus = a.employeeNo
                        WHEN 2 THEN b.branchCode = a.employeeNo
                        WHEN 3 THEN b.departmentCode = a.employeeNo
                        WHEN 4 THEN b.positionCode = a.employeeNo
                        WHEN 5 THEN a.employeeNo = 'ALL'
                        WHEN 6 THEN b.employeeNo = a.employeeNo
                        ELSE FALSE
                    END
                )
                WHERE a.approverNo = @approverNo
                AND b.employeeNo = @targetEmployeeNo
                AND a.isActive = 1
                LIMIT 1";

            return await _db.QueryFirstOrDefaultAsync<int?>(
                sql, new { approverNo, targetEmployeeNo });
        }

      


    }
}