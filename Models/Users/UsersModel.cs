namespace KEISAN_HRIS_v2.Models.Users
{
    public class usersLogin
    {
        public required string UserCode { get; set; }
        public required string Password { get; set; }
    }

    public class userlist
    {
        public int Id { get; set; }
        public string employeeNo { get; set; }
        public string employeeName { get; set; }
        public string positionName { get; set; }
        public string rankName { get; set; }
        public string branchName { get; set; }

    }

    public class EmployeeBasicInfoModel
    {
        // Primary Key
        public int Id { get; set; }

        // Employee Identification
        public string employeeNo { get; set; }
        public string seriesNo { get; set; }

        // Name Fields
        public string firstName { get; set; }
        public string middleName { get; set; }
        public string lastName { get; set; }
        public string honorific { get; set; }
        public string suffix { get; set; }

        // Employment Dates
        public string dateHired { get; set; }
        public string dateRehired { get; set; }

        // Employment Status and Probationary Period
        public string employmentStatus { get; set; }
        public string probationaryStartDate { get; set; }
        public string probationaryEndDate { get; set; }

        // Job Information
        public string jobGrade { get; set; }
        public string rankCode { get; set; }
        public string unitCode { get; set; }
        public string positionCode { get; set; }
        public string branchCode { get; set; }
        public string departmentCode { get; set; }

        // Instructor Flags
        public bool isGAInstructor { get; set; }
        public bool isFlightInstructor { get; set; }

        // Status Flags
        public bool isRetired { get; set; }
        public int isActive { get; set; }

        // Termination Information - Initial Employment
        public string dateOfEmpTermInitial { get; set; }
        public string reason4TermInitial { get; set; }
        public string remarksInitial { get; set; }

        // Termination Information - Rehired Employment
        public string dateOfEmpTermRehired { get; set; }
        public string reason4TermRehired { get; set; }
        public string remarksRehired { get; set; }

        // Appointment Dates
        public string dateOfProApp { get; set; }
        public string dateOfRegApp { get; set; }

        // Category
        public string categoryCode { get; set; }

        // Audit Fields
        public string dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; }
        public string dtDeleted { get; set; }
        public string deletedByUser { get; set; }

        // Supervisor Information
        public string supervisorNo { get; set; }

        // Overtime Offset Flag
        public bool isOvertimeOffset { get; set; }

        // Payroll Information
        public string payrollGroup { get; set; }
        public string qrCode { get; set; }

        // Contract Dates
        public string dtContractStart { get; set; }
        public string dtContractEnd { get; set; }

        // Fingerprint Data (BLOB fields)
        public byte[] LF1 { get; set; }
        public byte[] LF2 { get; set; }
        public byte[] RF1 { get; set; }
        public byte[] RF2 { get; set; }

        // Additional Display Properties (from joins with other tables)
        public string supervisorName { get; set; }
        public string RFID { get; set; }
        public string contractStartDate { get; set; }
        public string contractEndDate { get; set; }
        public string employmentStatusName { get; set; }
        public string positionName { get; set; }
        public string rankName { get; set; }
        public string branchName { get; set; }
        public string departmentName { get; set; }
        public string unitName { get; set; }
    }
    public class EmployeeProfileModel
    {
        public int id { get; set; }
        public string? employeeNo { get; set; }
        public string? profilePicturePath { get; set; }
        public string? profileCoverPath { get; set; }
        public bool? isActive { get; set; }
        public DateTime? dtAdded { get; set; }
        public string? addedByUser { get; set; }
        public DateTime? dtLastModified { get; set; }
        public string? lastModifiedByUser { get; set; }
        public DateTime? dtDeleted { get; set; }
        public string? deletedByUser { get; set; }
    }


    public class userPersonalInfo
    {
        public int id { get; set; }
        public String employeeNo { get; set; }
        public String gender { get; set; }
        public String weight { get; set; }
        public String height { get; set; }
        public String bmi { get; set; }
        public String dateOfBirth { get; set; }
        public String citizenshipCode { get; set; }
        public String citizenshipName { get; set; }
        public String nationality { get; set; }
        public String homePhoneNo { get; set; }
        public String mobileNo { get; set; }
        public String emailAddress { get; set; }
        public String presentAddress { get; set; }
        public String permanentAddress { get; set; }
        public String fatherName { get; set; }
        public String motherMaidenName { get; set; }
        public String personToNotify { get; set; }
        public String relationship { get; set; }
        public String contactNo { get; set; }
        public String civilStatus { get; set; }
        public String nameOfSpouse { get; set; }
        public String spouseDateOfBirth { get; set; }
        public String occupation { get; set; }
        public String isActive { get; set; }
        public String dtAdded { get; set; }
        public String addedByUser { get; set; }
        public String birthPlace { get; set; }
        public String religion { get; set; }
        public String zipCode { get; set; }
    }
    public class SiblingList
    {
        public int id { get; set; }
        public String employeeNo { get; set; }
        public String nameOfSibling { get; set; }
        public String dateOfBirth { get; set; }
        public String gender { get; set; }
        public String relationship { get; set; }
        public String dependent { get; set; }
        public String isActive { get; set; }
        public String dtAdded { get; set; }
        public String addedByUser { get; set; }

    }

    // DTO for data transfer (used in API calls)
    public class EducationalBackgroundDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string NameOfSchool { get; set; }
        public string SchoolType { get; set; }
        public string Course { get; set; }
        public string YearGraduated { get; set; }
        public double? UnitsEarned { get; set; }
        public string SchoolAddress { get; set; }
        public string Attain { get; set; }
        public bool IsActive { get; set; }
    }

    // Full model matching database schema (used for queries)
    public class EducationalBackgroundInfo
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; }
        public string NameOfSchool { get; set; }
        public string YearGraduated { get; set; }
        public string SchoolAddress { get; set; }
        public string SchoolType { get; set; }
        public double UnitsEarned { get; set; }
        public bool IsActive { get; set; }
        public DateTime? DtAdded { get; set; }
        public string AddedByUser { get; set; }
        public DateTime? DtLastModified { get; set; }
        public string LastModifiedByUser { get; set; }
        public DateTime? DtDeleted { get; set; }
        public string DeletedByUser { get; set; }
        public string Course { get; set; }
        public string Attain { get; set; }
    }

    // Legacy model for backward compatibility (if needed elsewhere in the codebase)
    public class userEducationalBackground
    {
        public int Id { get; set; }
        public string employeeNo { get; set; }
        public string nameOfSchool { get; set; }
        public string yearGraduated { get; set; }
        public string course { get; set; }
        public double unitsEarned { get; set; }
        public string schoolType { get; set; }
        public string schoolAddress { get; set; }
        public string attain { get; set; }
    }

    public class userPayrollDetails
    {
        public int id { get; set; }
        public string employeeNo { get; set; }
        public bool? isActive { get; set; }
        public bool? isMinimumWageEarner { get; set; }

        public double? fixedNetPay { get; set; }
        public double? meritServicePay { get; set; }
        public double? basicSalary { get; set; }
        public double? basicMonthlyPay { get; set; }
        public double? dailyRate { get; set; }
        public double? hourlyRate { get; set; }

        public string effectivityDate { get; set; }

        public string payrollBasis { get; set; }
        public string payrollType { get; set; }

        public double? mp2 { get; set; }
        public double? contriPIFadditional { get; set; }

        public string tinNo { get; set; }
        public string sssNo { get; set; }
        public string philhealthNo { get; set; }
        public string hdmfNo { get; set; }

        public string bankType { get; set; }
        public string bankName { get; set; }
        public string bankCode { get; set; }
        public string accountNo { get; set; }

        public bool? isNoLate { get; set; }
        public bool? isNoOTPremium { get; set; }

        public string payrollGroup { get; set; }

        public string? dtAdded { get; set; }
        public string addedByUser { get; set; }
        public int toInsertHistory { get; set; }
    }

    public class userAllowances
    {
        public int id { get; set; }
        public string? employeeNo { get; set; }
        public string? allowanceCode { get; set; }
        public string? allowanceName { get; set; }
        public string? basis { get; set; }
        public string? effectivityDate { get; set; }
        public string? taxType { get; set; }
        public double? allowanceAmount { get; set; }
        public bool? isActive { get; set; }
        public string? dtAdded { get; set; }
        public string? addedByUser { get; set; }
    }

    // DTO Model for incoming requests
    public class AllowanceDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string AllowanceCode { get; set; }
        public decimal AllowanceAmount { get; set; }
        public string EffectivityDate { get; set; }
        public bool IsActive { get; set; }
    }

    // Model for e_loan table (Employee Loans)
    public class UsersLoansModel
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; }
        public string LoanCode { get; set; }
        public string LoanName { get; set; }
        public string DeductionSchedule { get; set; }
        public double PrincipalAmount { get; set; }
        public double InterestAmount { get; set; }
        public double TotalLoanAmount { get; set; }
        public double DeductionPerCutoff { get; set; }
        public double AmortizationAmount { get; set; } // Keep for backward compatibility with reports
        public int MonthsToPay { get; set; }
        public double LoanPayments { get; set; }
        public double OutstandingBalance { get; set; }
        public double LoanBalance { get; set; } // For reports
        public string DateGranted { get; set; }
        public string DeductionStartDate { get; set; }
        public bool IsActive { get; set; }
        public int LoanIsActive { get; set; }
        public string DtAdded { get; set; }
        public string AddedByUser { get; set; }
        public string DtLastModified { get; set; }
        public string LastModifiedByUser { get; set; }
        public string DtDeleted { get; set; }
        public string DeletedByUser { get; set; }
        public string StatusName { get; set; }
        public string LoanStatus { get; set; }
        public string DtStatus { get; set; }
        public string StatusByUser { get; set; }
        public string Remarks { get; set; }

        // Additional properties for reports
        public string BranchName { get; set; }
        public string DepartmentName { get; set; }
        public string FullName { get; set; }
    }

    // DTO for creating/editing loans
    public class UsersLoansDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string LoanCode { get; set; }
        public string DeductionSchedule { get; set; }
        public double PrincipalAmount { get; set; }
        public double InterestAmount { get; set; }
        public double TotalLoanAmount { get; set; }
        public double DeductionPerCutoff { get; set; }
        public int MonthsToPay { get; set; }
        public string DateGranted { get; set; }
        public string DeductionStartDate { get; set; }
        public string Remarks { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // DTO for loan status updates (Complete/Inactive)
    public class LoanStatusUpdateDto
    {
        public int Id { get; set; }
        public string Remarks { get; set; }
    }

    // Model for s_loan table (Loan Types)
    public class LoanTypeModel
    {
        public int Id { get; set; }
        public string LoanCode { get; set; }
        public string LoanName { get; set; }
        public bool IsActive { get; set; }
    }

    // Model for m_loan table (Loan Payments)
    public class LoanPaymentModel
    {
        public int Id { get; set; }
        public int E_LoanID { get; set; }
        public double LoanPayments { get; set; }
        public string DtAdded { get; set; }
        public string Remarks { get; set; }
        public string AddedByUser { get; set; }
    }

    public class userFixedDeduction
    {
        public int id { get; set; }
        public string? employeeNo { get; set; }
        public string? employeeName { get; set; }
        public string? fixedDeductionCode { get; set; }
        public string? datePosted { get; set; }
        public double remainingBalance { get; set; }
        public double debit { get; set; }
        public double credit { get; set; }
        public string? statusByUser { get; set; }
        public int fixedDeductionID { get; set; }
        public string? fixedDeductionName { get; set; }
        public string? fixedDeductionType { get; set; }
        public string? fixedDeductionIsActive { get; set; }
        public string? deductionSchedule { get; set; }
        public double fixedDeductionAmount { get; set; }
        public string? fixedDeductionDateStart { get; set; }
        public string? remarks { get; set; }
        public double totalPaidBalance { get; set; }
        

    }

    public class userLeaveLedger
    {
        // Primary Key
        public int id { get; set; }

        // Employee Information
        public string employeeNo { get; set; }
        public string? employeeName { get; set; }

        // Leave Information
        public string? leaveCode { get; set; }
        public string? leaveName { get; set; }
        public string? statusName { get; set; }

        // Balance Information
        public double beginningBalance { get; set; }
        public double accrual { get; set; }
        public double usedCredits { get; set; }
        public double availableBalance { get; set; }

        // Reference and Date Information
        public int referenceID { get; set; }  // Changed from int? to int with default 0
        public string? dateMonth { get; set; }  // YYYY-MM format
        public int dateYear { get; set; }   // Changed from double? to int

        // Audit Fields
        public string? dtAdded { get; set; }
        public string? addedByUser { get; set; }
        public string? dtLastModified { get; set; }
        public string? lastModifiedByUser { get; set; }
        public DateTime? dtDeleted { get; set; }
    }

    public class BiometricsModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public string accessNo { get; set; } = string.Empty;
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string addedByUser { get; set; } = string.Empty;
        public DateTime? dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; } = string.Empty;
        public DateTime? dtDeleted { get; set; }
        public string deletedByUser { get; set; } = string.Empty;

        // Additional property for display purposes
        public string employeeName { get; set; } = string.Empty;
    }

    public class EmployeeBiometricsModel
    {
        public string employeeNo { get; set; } = string.Empty;
        public string employeeName { get; set; } = string.Empty;
    }

    // Model for Leave Setup display/listing
    public class LeaveSetupModel
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; }
        public string LeaveCode { get; set; }
        public string LeaveName { get; set; }
        public string DateEntitled { get; set; }
        public double RemainingBalance { get; set; }
        public double BeginningBalance { get; set; }
        public double UsedCredits { get; set; }
        public double Accrual { get; set; }
        public double AvailableBalance { get; set; }
        public bool IsActive { get; set; }
        public DateTime DtAdded { get; set; }
        public string AddedByUser { get; set; }
        public DateTime? DtLastModified { get; set; }
        public string LastModifiedByUser { get; set; }
        public DateTime? DtDeleted { get; set; }
        public string DeletedByUser { get; set; }
        public string DateFrom { get; set; }
        public string DateTo { get; set; }
        public double remainingLeaveDays { get; set; }
    }

    // Model for s_leave table (Leave Types)
    public class LeaveTypeModel
    {
        public int Id { get; set; }
        public string LeaveCode { get; set; }
        public string LeaveName { get; set; }
        public double? LeaveCredits { get; set; }

        public bool IsActive { get; set; }
        public DateTime DtAdded { get; set; }
        public string AddedByUser { get; set; }
        public DateTime? DtLastModified { get; set; }
        public string LastModifiedByUser { get; set; }
        public DateTime? DtDeleted { get; set; }
        public string DeletedByUser { get; set; }
        public bool Annual { get; set; }
        public string RequestType { get; set; }
       
        
    }

    // Model for e_leave table (Employee Leave Entitlement)
    public class EmployeeLeaveModel
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; }
        public int IsLeave { get; set; }
        public string LeaveCode { get; set; }
        public string LeaveName { get; set; }
        public string DateEntitled { get; set; }
        public int LeaveDays { get; set; }
        public int IsAccumulated { get; set; }
        public bool IsActive { get; set; }
        public DateTime DtAdded { get; set; }
        public string AddedByUser { get; set; }
        public DateTime? DtLastModified { get; set; }
        public string LastModifiedByUser { get; set; }
        public DateTime? DtDeleted { get; set; }
        public string DeletedByUser { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public string remainingLeaveDays { get; set; }
    }

    // Model for m_leave table (Leave Balance Management)
    public class LeaveBalanceModel
    {
        public int Id { get; set; }
        public string employeeNo { get; set; }
        public string branchName { get; set; }
        public string departmentName { get; set; }
        public string fullName { get; set; }
        public double sl { get; set; }
        public double vl { get; set; }
        public double leaveConversion { get; set; }
        public double dailyRate { get; set; }
        public string cto { get; set; }
        public int RqLeaveID { get; set; }
        public string LeaveCode { get; set; }
        public string LeaveName { get; set; }
        
        public string StatusName { get; set; }
        public double BeginningBalance { get; set; }
        public double Accrual { get; set; }
        public double UsedCredits { get; set; }
        public double AvailableBalance { get; set; }
        public string DateMonth { get; set; }
        public double DateYear { get; set; }
    }

    // DTO for creating/editing leave setup
    public class LeaveSetupDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string LeaveCode { get; set; }
        public string DateEntitled { get; set; }
        public double RemainingBalance { get; set; }
        public bool IsActive { get; set; } = true;
        public string DateFrom { get; set; }
        public string DateTo { get; set; }
    }

    // DTO for updating leave balance
    public class LeaveBalanceUpdateDto
    {
        public int Id { get; set; }
        public double BeginningBalance { get; set; }
        public double UsedCredits { get; set; }
        public double AvailableBalance { get; set; }
    }

    // Model for Medical Availment display/listing
    public class MedicalAvailmentModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public string availeeNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AvaileeType { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
        public double AvailableInsurance { get; set; }
        public double InPatient { get; set; } = 0;
        public double OutPatient { get; set; } = 0;
        public double Dental { get; set; } = 0;
        public double Balance { get; set; }
        public DateTime dtAdded { get; set; }
        public string addedBy { get; set; } = string.Empty;
        public DateTime? dtModified { get; set; }
        public string modifiedBy { get; set; } = string.Empty;
        public bool isActive { get; set; }
    }

    // Model for e_availee table (Employee Availee)
    public class EmployeeAvaileeModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; } = string.Empty;
        public string availeeNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AvaileeType { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
        public double AvailableInsurance { get; set; }
        public DateTime dtAdded { get; set; }
        public string addedBy { get; set; } = string.Empty;
        public DateTime? dtModified { get; set; }
        public string modifiedBy { get; set; } = string.Empty;
        public bool isActive { get; set; }
    }

    // DTO for creating/editing medical availment
    public class MedicalAvailmentDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AvaileeType { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
        public double AvailableInsurance { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // Model for Availee Type dropdown
    public class AvaileeTypeModel
    {
        public string Value { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public class userSchedule
    {
        public int Id { get; set; }
        public string employeeNo { get; set; }
        public string schedCode { get; set; }
        public string effectivityDate { get; set; }
        public string effectivityDateTo { get; set; }
        public List<string> weekdays { get; set; } 
        public string weekdayName { get; set; }     
        public string timeIn { get; set; }
        public string timeOut { get; set; }
        public double totalRenderHour { get; set; }
        public double totalBreaktimeMinute { get; set; }
        public bool isActive { get; set; }
        public string dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; }
        public string dtDeleted { get; set; }
        public string deletedByUser { get; set; }
        public bool isRestDay { get; set; }
        public string employeeName { get; set; }
        public string eventName { get; set; }
        public string scheduleTypeCode { get; set; }
    }

    public class EmploymentHistoryInfo
    {
        public int id { get; set; }
        public string employeeNo { get; set; }
        public string companyName { get; set; }
        public string position { get; set; }
        public string fromDate { get; set; }
        public string toDate { get; set; }
        public string address { get; set; }
        public bool isActive { get; set; }
        public string dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; }
        public string dtDeleted { get; set; }
        public string deletedByUser { get; set; }
        public string JOBDESC { get; set; }
        public string REMARKS { get; set; }
    }

    // DTO for creating/editing employment history
    public class EmploymentHistoryDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string CompanyName { get; set; }
        public string Position { get; set; }
        public string Address { get; set; }
        public string FromDate { get; set; }
        public string ToDate { get; set; }
        public string JobDesc { get; set; }
        public string Remarks { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class EmployeeID
    {
        public string id { get; set; }
    }

    public class LicensesAndCertificationInfo
    {
        public int id { get; set; }
        public string employeeNo { get; set; }
        public string licenseAndCertificateNo { get; set; }
        public string licenseAndCertificateDescription { get; set; }
        public string registrationDate { get; set; }
        public string issueDate { get; set; }
        public string validUntil { get; set; }
        public string licenseRemarks { get; set; }
        public bool isActive { get; set; }
        public string dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; }
        public string dtDeleted { get; set; }
        public string deletedByUser { get; set; }
    }

    // DTO for creating/editing licenses and certification
    public class LicensesAndCertificationDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string LicenseAndCertificateNo { get; set; }
        public string LicenseAndCertificateDescription { get; set; }
        public string RegistrationDate { get; set; }
        public string IssueDate { get; set; }
        public string ValidUntil { get; set; }
        public string LicenseRemarks { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // Updated Model for Attachments display/listing
    public class AttachmentsInfo
    {
        public int id { get; set; }
        public string employeeNo { get; set; }
        public string attachmentDescription { get; set; }
        public string attachmentTypeCode { get; set; }
        public string attachmentTypeName { get; set; }
        public string attachmentPath { get; set; }
        public bool isActive { get; set; }
        public string dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string dtLastModified { get; set; }
        public string lastModifiedByUser { get; set; }
        public string dtDeleted { get; set; }
        public string deletedByUser { get; set; }
    }

    // DTO for creating/editing attachments
    public class AttachmentsDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string AttachmentDescription { get; set; }
        public string AttachmentTypeCode { get; set; }
        public IFormFile AttachmentFile { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // Model for retrieving data from database
    public class TrainingsInfo
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; }
        public string TrainingTitle { get; set; }
        public string TrainingProvider { get; set; }
        public string TrainingVenue { get; set; }
        public string DateFrom { get; set; }
        public string DateTo { get; set; }
        public string Remarks { get; set; }
        public int IsActive { get; set; }
        public string DtAdded { get; set; }
        public string AddedByUser { get; set; }
        public DateTime? DtStatus { get; set; }
        public string StatusByUser { get; set; }
    }

    // DTO for receiving data from client
    public class TrainingsDto
    {
        public int? Id { get; set; }
        public string EmployeeNo { get; set; }
        public string TrainingTitle { get; set; }
        public string TrainingProvider { get; set; }
        public string TrainingVenue { get; set; }
        public string DateFrom { get; set; }
        public string DateTo { get; set; }
        public string Remarks { get; set; }
    }

    public class ApproverModel
    {
        public int id { get; set; }
        public string employeeNo { get; set; }
        public string approverLevel { get; set; }
        public string approverNo { get; set; }
        public int? typeList { get; set; }
        public string approver1Email { get; set; }
        public string approver2 { get; set; }
        public string approver2Email { get; set; }
        public string approver3 { get; set; }
        public string approver3Email { get; set; }
        public string approver4 { get; set; }
        public string approver4Email { get; set; }
        public string oic { get; set; }
        public string oicEmail { get; set; }
        public string oicExpirationDate { get; set; }
        public int? isActive { get; set; }
        public string dtAdded { get; set; }
        public string addedByUser { get; set; }
        public string dtModified { get; set; }
        public string lastModifiedByUser { get; set; }
        public string dtDeleted { get; set; }
        public string deletedByUser { get; set; }
    }

    // DTO for creating/editing approver
    public class ApproverDto
    {
        public int? id { get; set; }
        public string employeeNo { get; set; }
        public string approverLevel { get; set; }
        public string approverNo { get; set; }
        public int? typeList { get; set; }
        public string approver1Email { get; set; }
        public string approver2 { get; set; }
        public string approver2Email { get; set; }
        public string approver3 { get; set; }
        public string approver3Email { get; set; }
        public string approver4 { get; set; }
        public string approver4Email { get; set; }
        public string oic { get; set; }
        public string oicEmail { get; set; }
        public string oicExpirationDate { get; set; }
        public bool isActive { get; set; } = true;
    }
}

