    namespace KEISAN_HRIS_v2.Models.Timekeeping
    {
        public class OvertimeRequestModel
        {
        public string? requestIn { get; set; }
        public string? requestOut { get; set; }
        public string? cutoffStart { get; set; }
        public string? cutoffEnd { get; set; }
        // Primary Key
        public int id { get; set; }

            // Employee Info
            public string? employeeNo { get; set; }
            public string? branchName { get; set; }
            public string? departmentName { get; set; }
            public string? fullName { get; set; }

            // Overtime Date and Time fields (matching DB schema)
            public DateTime? overTimeDateIN { get; set; }
            public TimeSpan? overTimeIN { get; set; }
            public DateTime? overTimeDateOUT { get; set; }
            public TimeSpan? overTimeOUT { get; set; }

            // Reason
            public string? overTimeReason { get; set; }

            // Status and tracking
            public string? statusName { get; set; }
            public DateTime? dtStatus { get; set; }
            public string? statusByUser { get; set; }
            public string? requestedByUser { get; set; }

            // Active flag and timestamps
            public bool isActive { get; set; } = true;
            public DateTime? dtAdded { get; set; }
            public string? addedByUser { get; set; }
            public DateTime? dtLastModified { get; set; }
            public string? lastModifiedByUser { get; set; }
            public DateTime? dtDeleted { get; set; }
            public string? deletedByUser { get; set; }

            // Remarks
            public string? remarks { get; set; }

        // Multi-level status fields
        public string? statusLevel1 { get; set; }
        public DateTime? dtStatusLevel1 { get; set; }
        public string? statusByLevel1 { get; set; }

        public string? statusLevel2 { get; set; }
        public DateTime? dtStatusLevel2 { get; set; }
        public string? statusByLevel2 { get; set; }

        public string? statusLevel3 { get; set; }
            public DateTime? dtStatusLevel3 { get; set; }
            public string? statusByLevel3 { get; set; }

            public string? statusLevel4 { get; set; }
            public DateTime? dtStatusLevel4 { get; set; }
            public string? statusByLevel4 { get; set; }

            // Processing flags
            public bool isProcessed { get; set; }
            public DateTime? dateProcessed { get; set; }
            public string? processedBy { get; set; }

            // Notification flag
            public bool isNotified { get; set; }

            // Display properties (for DataTables)
            public string? displayDateIn { get; set; }
            public string? displayDateOut { get; set; }
            public string? displayTimeIn { get; set; }
            public string? displayTimeOut { get; set; }
            public string? dateRequested { get; set; }

            // Approved render fields (from DB schema)
            public double approvedRenderOT { get; set; }
            public string? justificationLateFile { get; set; }

            // Cutoff information
            public string? methodType { get; set; }
            public string? cutoffType { get; set; }
            public string? dateMonth { get; set; }
            public int? dateYear { get; set; }
            public DateTime? dateFrom { get; set; }
            public DateTime? dateTo { get; set; }

            // Project information
            public string? project { get; set; }
            public string? jobOrder { get; set; }

            // Additional rendering fields for calculations
            public double renderOT { get; set; }
            public double renderREST { get; set; }
            public double renderRESTOT { get; set; }
            public double renderNSD { get; set; }
            public double renderNSDOT { get; set; }
            public double renderNSDREST { get; set; }
            public double renderNSDRESTOT { get; set; }
            public double renderL { get; set; }
            public double renderOTL { get; set; }
            public double renderRESTL { get; set; }
            public double renderRESTOTL { get; set; }
            public double renderNSDL { get; set; }
            public double renderNSDOTL { get; set; }
            public double renderNSDRESTL { get; set; }
            public double renderNSDRESTOTL { get; set; }
            public double renderS { get; set; }
            public double renderOTS { get; set; }
            public double renderRESTS { get; set; }
            public double renderRESTOTS { get; set; }
            public double renderNSDS { get; set; }
            public double renderNSDOTS { get; set; }
            public double renderNSDRESTS { get; set; }
            public double renderNSDRESTOTS { get; set; }

            // Helper properties for binding from form (using consistent naming)
            public DateTime? otDateIn
            {
                get => overTimeDateIN;
                set => overTimeDateIN = value;
            }

            public TimeSpan? otTimeIn
            {
                get => overTimeIN;
                set => overTimeIN = value;
            }

            public DateTime? otDateOut
            {
                get => overTimeDateOUT;
                set => overTimeDateOUT = value;
            }

            public TimeSpan? otTimeOut
            {
                get => overTimeOUT;
                set => overTimeOUT = value;
            }

            public string? reason
            {
                get => overTimeReason;
                set => overTimeReason = value;
            }
        }

        public class OvertimeReasonOption
        {
            public string value { get; set; } = string.Empty;
            public string text { get; set; } = string.Empty;
        }

    public class LeaveRequestModel
    {
        // Primary Key
        public int id { get; set; }

        // Employee Info
        public string? employeeNo { get; set; }
        public string? branchName { get; set; }
        public string? departmentName { get; set; }
        public string? fullName { get; set; }

        // Leave Code and Name
        public string? leaveCode { get; set; }
        public string? leaveName { get; set; }

        // Leave Balance
        public string? leaveBalance { get; set; }

        // Leave Dates
        public DateTime? leaveDateFrom { get; set; }
        public DateTime? leaveDateTo { get; set; }

        // Leave Count
        public double? leaveCountDays { get; set; }
        public double? leaveCountHours { get; set; }

        // Time fields
        public TimeSpan? timeIN { get; set; }
        public TimeSpan? timeOUT { get; set; }

        // Leave Type
        public string? leaveType { get; set; }

        // Reason
        public string? leaveReason { get; set; }

        // Status
        public string? statusName { get; set; }
        public DateTime? dtStatus { get; set; }
        public string? statusByUser { get; set; }
        public string? requestedByUser { get; set; }

        // Remarks
        public string? remarks { get; set; }

        // Active flag and timestamps
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string? addedByUser { get; set; }
        public DateTime? dtLastModified { get; set; }
        public string? lastModifiedByUser { get; set; }
        public DateTime? dtDeleted { get; set; }
        public string? deletedByUser { get; set; }

        // Credit Deduction Flag
        public bool creditDeductionOnly { get; set; }

        // Multi-level status fields
        public string? statusLevel1 { get; set; }
        public DateTime? dtStatusLevel1 { get; set; }
        public string? statusByLevel1 { get; set; }

        public string? statusLevel2 { get; set; }
        public DateTime? dtStatusLevel2 { get; set; }
        public string? statusByLevel2 { get; set; }

        public string? statusLevel3 { get; set; }
        public DateTime? dtStatusLevel3 { get; set; }
        public string? statusByLevel3 { get; set; }

        public string? statusLevel4 { get; set; }
        public DateTime? dtStatusLevel4 { get; set; }
        public string? statusByLevel4 { get; set; }

        // Processing flags
        public bool isProcessed { get; set; }
        public DateTime? dateProcessed { get; set; }
        public string? processedBy { get; set; }

        // Notification flag
        public bool isNotified { get; set; }

        // Balance fields (from schema)
        public double? beginningBalance { get; set; }
        public double? accrual { get; set; }
        public double? usage { get; set; }
        public double? available { get; set; }

        // Monetization flag
        public bool isMonetized { get; set; }

        // Display properties (for DataTables/UI)
        public string? displayDateFrom { get; set; }
        public string? displayDateTo { get; set; }
        public string? dateRequested { get; set; }
        public string? dateApproved { get; set; }

        // Cutoff information (following pattern from other models)
        public string? methodType { get; set; }
        public string? cutoffType { get; set; }
        public string? dateMonth { get; set; }
        public int? dateYear { get; set; }
        public DateTime? dateFrom { get; set; }
        public DateTime? dateTo { get; set; }
    }

    public class LeaveReasonOption
    {
        public string value { get; set; } = string.Empty;
        public string text { get; set; } = string.Empty;
    }

    public class WorkFromHomeRequestModel
        {
            // Primary Key
            public int id { get; set; }

            // Employee Info
            public string? employeeNo { get; set; }

            // Date and Time
            public DateTime? wfhDateIn { get; set; }
            public TimeSpan? wfhTimeIn { get; set; }
            public DateTime? wfhDateOut { get; set; }
            public TimeSpan? wfhTimeOut { get; set; }

            // Reason
            public string? wfhReason { get; set; }

            // Status and tracking
            public string? statusName { get; set; }
            public DateTime? dtStatus { get; set; }
            public string? statusByUser { get; set; }
            public string? requestedByUser { get; set; }

            // Active flag and timestamps
            public bool isActive { get; set; }
            public DateTime? dtAdded { get; set; }
            public string? addedByUser { get; set; }
            public DateTime? dtLastModified { get; set; }
            public string? lastModifiedByUser { get; set; }
            public DateTime? dtDeleted { get; set; }
            public string? deletedByUser { get; set; }

            // Remarks
            public string? remarks { get; set; }

            // Processing
            public bool isProcessed { get; set; }
            public DateTime? dateProcessed { get; set; }
            public string? processedBy { get; set; }

            // Multi-level status
            public string? statusLevel1 { get; set; }
            public DateTime? dtStatusLevel1 { get; set; }
            public string? statusByLevel1 { get; set; }

            public string? statusLevel2 { get; set; }
            public DateTime? dtStatusLevel2 { get; set; }
            public string? statusByLevel2 { get; set; }

            public string? statusLevel3 { get; set; }
            public DateTime? dtStatusLevel3 { get; set; }
            public string? statusByLevel3 { get; set; }

            public string? statusLevel4 { get; set; }
            public DateTime? dtStatusLevel4 { get; set; }
            public string? statusByLevel4 { get; set; }

            // Notification
            public bool isNotified { get; set; }

            // Display helpers (optional for UI)
            public string? displayDateIn { get; set; }
            public string? displayDateOut { get; set; }
            public string? displayTimeIn { get; set; }
            public string? displayTimeOut { get; set; }
        }

        public class OBRequestModel
        {
            // Primary Key
            public int id { get; set; }

            // Employee Info
            public string employeeNo { get; set; } = string.Empty;
            public string employeeName { get; set; } = string.Empty;

            // Date and Time fields
            public DateTime? obDateIn { get; set; }
            public TimeSpan? obTimeIn { get; set; }
            public DateTime? obDateOut { get; set; }
            public TimeSpan? obTimeOut { get; set; }

            // Reason fields
            public string obReason { get; set; } = string.Empty; // Full reason text
            public string selectReason { get; set; } = string.Empty; // Selected category

            // Status and tracking
            public string statusName { get; set; } = string.Empty;
            public DateTime? dtStatus { get; set; }
            public string statusByUser { get; set; } = string.Empty;
            public string requestedByUser { get; set; } = string.Empty;

            // Active flag and timestamps
            public bool isActive { get; set; } = true;
            public DateTime? dtAdded { get; set; }
            public string addedByUser { get; set; } = string.Empty;
            public DateTime? dtLastModified { get; set; }
            public string lastModifiedByUser { get; set; } = string.Empty;
            public DateTime? dtDeleted { get; set; }
            public string deletedByUser { get; set; } = string.Empty;

            // Remarks and additional info
            public string remarks { get; set; } = string.Empty;

        // Multi-level status fields
        public string statusLevel1 { get; set; } = string.Empty;
        public DateTime? dtStatusLevel1 { get; set; }
        public string statusByLevel1 { get; set; } = string.Empty;

        public string statusLevel2 { get; set; } = string.Empty;
        public DateTime? dtStatusLevel2 { get; set; }
        public string statusByLevel2 { get; set; } = string.Empty;

        public string statusLevel3 { get; set; } = string.Empty;
            public DateTime? dtStatusLevel3 { get; set; }
            public string statusByLevel3 { get; set; } = string.Empty;

            public string statusLevel4 { get; set; } = string.Empty;
            public DateTime? dtStatusLevel4 { get; set; }
            public string statusByLevel4 { get; set; } = string.Empty;

            // Processing flags
            public bool isProcessed { get; set; }
            public DateTime? dateProcessed { get; set; }
            public string processedBy { get; set; } = string.Empty;

            public bool isNotified { get; set; }

            // Display properties
            public string displayDateIn { get; set; } = string.Empty;
            public string displayDateOut { get; set; } = string.Empty;
            public string displayTimeIn { get; set; } = string.Empty;
            public string displayTimeOut { get; set; } = string.Empty;
        }

        public class OBReasonOption
        {
            public string value { get; set; } = string.Empty;
            public string text { get; set; } = string.Empty;
        }

        public class UndertimeRequestModel
        {
            // Primary Key
            public int id { get; set; }

            // Employee Info
            public string employeeNo { get; set; } = string.Empty;

            // Date and Time fields
            public TimeSpan? undertimeTimeOUT { get; set; }
            public DateTime? undertimeDateIN { get; set; }
            public DateTime? undertimeDateOUT { get; set; }

            // Reason
            public string undertimeReason { get; set; } = string.Empty;

            // Status and tracking
            public string statusName { get; set; } = string.Empty;
            public DateTime? dtStatus { get; set; }
            public string statusByUser { get; set; } = string.Empty;
            public string requestedByUser { get; set; } = string.Empty;

            // Active flag and timestamps
            public bool isActive { get; set; } = true;
            public string addedByUser { get; set; } = string.Empty;
            public DateTime? dtAdded { get; set; }
            public string lastModifiedByUser { get; set; } = string.Empty;
            public DateTime? dtLastModifiedByUser { get; set; }
            public string deletedByUser { get; set; } = string.Empty;
            public DateTime? dtDeleted { get; set; }

            // Remarks
            public string remarks { get; set; } = string.Empty;

        // Multi-level status fields
        public string statusLevel1 { get; set; } = string.Empty;
        public DateTime? dtStatusLevel1 { get; set; }
        public string statusByLevel1 { get; set; } = string.Empty;

        public string statusLevel2 { get; set; } = string.Empty;
        public DateTime? dtStatusLevel2 { get; set; }
        public string statusByLevel2 { get; set; } = string.Empty;

        public string statusLevel3 { get; set; } = string.Empty;
        public DateTime? dtStatusLevel3 { get; set; }
        public string statusByLevel3 { get; set; } = string.Empty;

        public string statusLevel4 { get; set; } = string.Empty;
        public DateTime? dtStatusLevel4 { get; set; }
        public string statusByLevel4 { get; set; } = string.Empty;

        // Notification flag
        public bool isNotified { get; set; }

            // Display properties
            public string employeeName { get; set; } = string.Empty;
            public string displayDateIn { get; set; } = string.Empty;
            public string displayDateOut { get; set; } = string.Empty;
            public string displayTimeOut { get; set; } = string.Empty;
        }


        public class ScheduleType

        {
            public int id { get; set; }

            public string scheduleTypeCode { get; set; }

            public string scheduleTypeName { get; set; }

        }

    public class ChangeScheduleRequestModel
    {
        // Primary Key
        public int id { get; set; }

        // Employee Info
        public string? employeeNo { get; set; }
        public string? weekdayName { get; set; }

        // Schedule Date and Time
        public DateTime? effectivityDate { get; set; }
        public TimeSpan? timeIN { get; set; }
        public TimeSpan? timeOUT { get; set; }

        // Reason
        public string? Reason { get; set; }

        // Break and Render Information
        public int? totalBreaktimeMinute { get; set; }
        public int? totalRenderHour { get; set; }
        public int? isRestDay { get; set; }

        // Status and tracking
        public string? statusName { get; set; }
        public DateTime? dtStatus { get; set; }
        public string? statusByUser { get; set; }
        public string? requestedByUser { get; set; }

        // Active flag and timestamps
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string? addedByUser { get; set; }
        public DateTime? dtLastModified { get; set; }
        public string? lastModifiedByUser { get; set; }
        public DateTime? dtDeleted { get; set; }
        public string? deletedByUser { get; set; }

        // Remarks
        public string? remarks { get; set; }

        // Cutoff information
        public string? methodType { get; set; }
        public string? cutoffType { get; set; }
        public string? dateMonth { get; set; }
        public int? dateYear { get; set; }
        public DateTime? dateFrom { get; set; }
        public DateTime? dateTo { get; set; }
        public DateTime? dateProcessed { get; set; }

        // Multi-level status fields
        public string? statusLevel1 { get; set; }
        public DateTime? dtStatusLevel1 { get; set; }
        public string? statusByLevel1 { get; set; }

        public string? statusLevel2 { get; set; }
        public DateTime? dtStatusLevel2 { get; set; }
        public string? statusByLevel2 { get; set; }

        public string? statusLevel3 { get; set; }
        public DateTime? dtStatusLevel3 { get; set; }
        public string? statusByLevel3 { get; set; }

        public string? statusLevel4 { get; set; }
        public DateTime? dtStatusLevel4 { get; set; }
        public string? statusByLevel4 { get; set; }

        // Notification flag
        public int isNotified { get; set; }

        // Schedule Type
        public string? scheduleTypeCode { get; set; }

        // Display properties (for UI binding)
        public string? displayEffectivityDate { get; set; }
        public string? displayTimeIn { get; set; }
        public string? displayTimeOut { get; set; }
        public string? dateRequested { get; set; }
    }

    public class ChangeScheduleReasonOption
    {
        public string value { get; set; } = string.Empty;
        public string text { get; set; } = string.Empty;
    }

    public class OffsetCreditRequestModel
    {
        // Primary Key
        public int id { get; set; }

        // Employee Info
        public string? employeeNo { get; set; }

        // Overtime/CTO Date and Time (from rq_cto table)
        public DateTime? overTimeDateIN { get; set; }
        public TimeSpan? overTimeIN { get; set; }
        public DateTime? overTimeDateOUT { get; set; }
        public TimeSpan? overTimeOUT { get; set; }

        // Approved Render OT (in HOURS)
        public double? approvedRenderOT { get; set; }

        // Reason
        public string? overTimeReason { get; set; }

        // Justification/Late File
        public string? justificationLateFile { get; set; }

        // Status Fields - Multi-level (like rq_overtime)
        public string? statusName { get; set; }
        public DateTime? dtStatus { get; set; }
        public string? statusByUser { get; set; }
        public string? requestedByUser { get; set; }

        public string? statusLevel1 { get; set; }
        public DateTime? dtStatusLevel1 { get; set; }
        public string? statusByLevel1 { get; set; }

        public string? statusLevel2 { get; set; }
        public DateTime? dtStatusLevel2 { get; set; }
        public string? statusByLevel2 { get; set; }

        public string? statusLevel3 { get; set; }
        public DateTime? dtStatusLevel3 { get; set; }
        public string? statusByLevel3 { get; set; }

        public string? statusLevel4 { get; set; }
        public DateTime? dtStatusLevel4 { get; set; }
        public string? statusByLevel4 { get; set; }

        // Remarks
        public string? remarks { get; set; }

        // Active flag and timestamps
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string? addedByUser { get; set; }
        public DateTime? dtLastModified { get; set; }
        public string? lastModifiedByUser { get; set; }
        public DateTime? dtDeleted { get; set; }
        public string? deletedByUser { get; set; }

        // Additional fields from rq_cto schema
        public string? methodType { get; set; }
        public string? cutOffType { get; set; }
        public string? dateMonth { get; set; }
        public int? dateYear { get; set; }
        public DateTime? dateFrom { get; set; }
        public DateTime? dateTo { get; set; }
        public bool isProcessed { get; set; }
        public DateTime? dateProcessed { get; set; }
        public string? processedBy { get; set; }
        public bool isNotified { get; set; }
        public string? project { get; set; }
        public int? jobOrder { get; set; }

        // Display properties (for DataTables/UI)
        public string? displayDateIn { get; set; }
        public string? displayTimeIn { get; set; }
        public string? displayDateOut { get; set; }
        public string? displayTimeOut { get; set; }
        public string? dateRequested { get; set; }
        public string? employeeName { get; set; }
    }

    public class OffsetApplicationRequestModel
    {
        // Primary Key
        public int id { get; set; }

        // Employee Info
        public string? employeeNo { get; set; }

        // Leave Code - Always 'CTO' for offset applications
        public string? leaveCode { get; set; }

        // Leave Type - Always 'whole' for offset applications
        public string? leaveType { get; set; }

        // Leave Dates (mapped from overTimeDate fields)
        public DateTime? leaveDateFrom { get; set; }
        public DateTime? leaveDateTo { get; set; }

        // Leave Count (mapped from approvedRenderOT)
        public double? leaveCountDays { get; set; }
        public double? leaveCountHours { get; set; }

        // Reason (mapped from overTimeReason)
        public string? leaveReason { get; set; }

        // Remarks
        public string? remarks { get; set; }

        // Status and tracking
        public string? statusName { get; set; }
        public DateTime? dtStatus { get; set; }
        public string? statusByUser { get; set; }
        public string? requestedByUser { get; set; }

        // Active flag and timestamps
        public bool isActive { get; set; } = true;
        public DateTime? dtAdded { get; set; }
        public string? addedByUser { get; set; }
        public DateTime? dtLastModified { get; set; }
        public string? lastModifiedByUser { get; set; }
        public DateTime? dtDeleted { get; set; }
        public string? deletedByUser { get; set; }

        // Credit Deduction Flag
        public bool creditDeductionOnly { get; set; }

        // Multi-level status fields
        public string? statusLevel2 { get; set; }
        public DateTime? dtStatusLevel2 { get; set; }
        public string? statusByLevel2 { get; set; }

        public string? statusLevel3 { get; set; }
        public DateTime? dtStatusLevel3 { get; set; }
        public string? statusByLevel3 { get; set; }

        public string? statusLevel4 { get; set; }
        public DateTime? dtStatusLevel4 { get; set; }
        public string? statusByLevel4 { get; set; }

        // Processing flags
        public bool isProcessed { get; set; }
        public DateTime? dateProcessed { get; set; }
        public string? processedBy { get; set; }

        // Notification flag
        public bool isNotified { get; set; }

        // Balance fields (from rq_leave schema)
        public double? beginningBalance { get; set; }
        public double? accrual { get; set; }
        public double? usage { get; set; }
        public double? available { get; set; }

        // Monetization flag
        public bool isMonetized { get; set; }

        // Display properties (for DataTables/UI)
        public string? displayDateFrom { get; set; }
        public string? displayDateTo { get; set; }
        public string? dateRequested { get; set; }
        public string? dateApproved { get; set; }
        public string? fullName { get; set; }

        // Cutoff information (following pattern from LeaveRequestModel)
        public string? methodType { get; set; }
        public string? cutoffType { get; set; }
        public string? dateMonth { get; set; }
        public int? dateYear { get; set; }
        public DateTime? dateFrom { get; set; }
        public DateTime? dateTo { get; set; }

        // Time fields (nullable, for future use if needed)
        public TimeSpan? timeIN { get; set; }
        public TimeSpan? timeOUT { get; set; }
    }

    public class OffsetApplicationReasonOption
    {
        public string value { get; set; } = string.Empty;
        public string text { get; set; } = string.Empty;
    }
}
