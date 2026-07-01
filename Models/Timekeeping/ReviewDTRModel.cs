using System;
using KEISAN_HRIS_v2.Helpers;

namespace KEISAN_HRIS_v2.Models.Timekeeping
{
    public class ReviewDTRModel
    {
        public string employeeNo { get; set; }
        public string branchCode { get; set; }
        public string departmentCode { get; set; }
        public DateTime? workDate { get; set; }
        public string weekDayName { get; set; }

        public string firstName { get; set; }
        public string middleName { get; set; }
        public string lastName { get; set; }

        public DateTime? scheduleTimeIn { get; set; }
        public DateTime? scheduleTimeOut { get; set; }
        public bool isRestDay { get; set; }

        public DateTime? biometricsDateIn { get; set; }
        public DateTime? biometricsDateOut { get; set; }

        public DateTime? overTimeDateIN { get; set; }
        public DateTime? overTimeDateOUT { get; set; }
        public TimeSpan? overTimeIN { get; set; }
        public TimeSpan? overTimeOUT { get; set; }
        public string overTimeReason { get; set; }
        public string leaveCode { get; set; }

        public string leaveName { get; set; }
        public double leaveCountDays { get; set; }
        public string leaveReason { get; set; }
        public string leaveType { get; set; }

        public string obReason { get; set; }
        public string wfhReason { get; set; }

        public string payrollType { get; set; }
        public string payrollBasis { get; set; }

        public string holidayType { get; set; }
        public string holidayName { get; set; }

        public string tremarks { get; set; }

        // ===== RANK INFORMATION =====
        public string rankCode { get; set; }
        public string rankName { get; set; }

        // ===== SCHEDULE TYPE =====
        public string scheduleTypeCode { get; set; }
        public string scheduleTypeName { get; set; }
        public double? totalRenderHour { get; set; }
        public double? totalBreaktimeMinute { get; set; }

        // ===== SPL HOLIDAY =====
        public double SPLHolidayHours { get; set; }
        public double SPLHolidayOTHours { get; set; }
        public double SPLHolidayNDHours { get; set; }

        // ===== REG HOLIDAY =====
        public double REGHolidayHours { get; set; }
        public double REGHolidayOTHours { get; set; }
        public double REGHolidayNDHours { get; set; }

        // ===== CHANGE SCHEDULE =====
        public int? changeScheduleID { get; set; }
        public TimeSpan? changeScheduleTimeIn { get; set; }
        public TimeSpan? changeScheduleTimeOut { get; set; }
        public string changeScheduleReason { get; set; }
        public string changeScheduleTypeCode { get; set; }

        // ===== UNDERTIME =====
        public int? undertimeID { get; set; }
        public DateTime? undertimeDateIN { get; set; }
        public DateTime? underTimeDateOUT { get; set; }
        public TimeSpan? undertimeTimeOUT { get; set; }
        public string undertimeReason { get; set; }

        // ===== MANUAL EDIT FLAGS =====
        public int isTimeInManuallyEdited { get; set; }
        public int isTimeOutManuallyEdited { get; set; }

        public int? tid { get; set; }
        public string tstatusName { get; set; }
        public TimeSpan? tTimeIn { get; set; }
        public TimeSpan? tTimeOut { get; set; }
        public DateTime? tDateOut { get; set; }
    }

    public class ReviewDTRViewModel
    {
        public string employeeNo { get; set; }
        public string branchCode { get; set; }
        public string departmentCode { get; set; }
        public string workDate { get; set; }
        public string weekDayName { get; set; }

        // RAW VALUES (keep for calculations)
        public double RenderHours { get; set; }
        public double LateMinutes { get; set; }
        public double UnderTimeMinutes { get; set; }
        public double NDHours { get; set; }
        public double OTNDHours { get; set; }
        public double RDNDHours { get; set; }
        public double SPLNDHours { get; set; }
        public double REGNDHours { get; set; }
        public double OTHours { get; set; }
        public double RDHours { get; set; }
        public double RDOTHours { get; set; }
        public double SPLHolidayHours { get; set; }
        public double SPLHolidayOTHours { get; set; }
        public double SPLHolidayNDHours { get; set; }
        public double SPLHolidayNDOTHours { get; set; }
        public double REGHolidayHours { get; set; }
        public double REGHolidayOTHours { get; set; }
        public double REGHolidayNDHours { get; set; }
        public double REGHolidayNDOTHours { get; set; }
        public double RDNDOTHours { get; set; }
        // Special Holiday Rest Day
        public double SPLHolidayRESTHours { get; set; }
        public double SPLHolidayRESTOTHours { get; set; }
        public double SPLHolidayRESTNDHours { get; set; }
        public double SPLHolidayRESTNDOTHours { get; set; }

        // Legal Holiday Rest Day
        public double REGHolidayRESTNDHours { get; set; }
        public double REGHolidayRESTNDOTHours { get; set; }
        public double REGHolidayRESTHours { get; set; }
        public double REGHolidayRESTOTHours { get; set; }

        // FORMATTED VALUES (for display)
        public string RenderHoursFormatted => TimeFormatHelper.FormatHours(RenderHours);
        public string LateMinutesFormatted => TimeFormatHelper.FormatMinutes(LateMinutes);
        public string UnderTimeMinutesFormatted => TimeFormatHelper.FormatMinutes(UnderTimeMinutes);
        public string NDHoursFormatted => TimeFormatHelper.FormatHours(NDHours);
        public string OTHoursFormatted => TimeFormatHelper.FormatHours(OTHours);
        public string RDHoursFormatted => TimeFormatHelper.FormatHours(RDHours);
        public string RDOTHoursFormatted => TimeFormatHelper.FormatHours(RDOTHours);
        public string RDNDHoursFormatted => TimeFormatHelper.FormatHours(RDNDHours);
        public string SPLHolidayHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayHours);
        public string SPLHolidayOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayOTHours);
        public string SPLHolidayNDHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayNDHours);
        public string SPLHolidayNDOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayNDOTHours);
        public string REGHolidayHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayHours);
        public string REGHolidayOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayOTHours);
        public string REGHolidayNDHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayNDHours);
        public string REGHolidayNDOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayNDOTHours);
        public string RDNDOTHoursFormatted => TimeFormatHelper.FormatHours(RDNDOTHours);
        // Special Holiday Rest Day
        public string SPLHolidayRESTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTHours);
        public string SPLHolidayRESTOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTOTHours);
        public string SPLHolidayRESTNDHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTNDHours);
        public string SPLHolidayRESTNDOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTNDOTHours);

        // Legal Holiday Rest Day
        public string REGHolidayRESTNDHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTNDHours);
        public string REGHolidayRESTNDOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTNDOTHours);
        public string REGHolidayRESTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTHours);
        public string REGHolidayRESTOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTOTHours);

        public DateTime? scheduleTimeIn { get; set; }
        public DateTime? scheduleTimeOut { get; set; }
        public DateTime? biometricsDateIn { get; set; }
        public DateTime? biometricsDateOut { get; set; }

        public string remarks { get; set; }

        public string overTimeReason { get; set; }
        public DateTime? OvertimeDateTimeIn { get; set; }
        public DateTime? OverTimeDateTimeOUT { get; set; }
        public string OTReason { get; set; }

        public string holidayType { get; set; }
        public string holidayName { get; set; }

        public string leaveName { get; set; }
        public double leaveCountDays { get; set; }
        public string leaveReason { get; set; }

        public string obReason { get; set; }
        public string wfhReason { get; set; }

        public string payrollType { get; set; }
        public string payrollBasis { get; set; }

        public DateTime? OTIn { get; set; }
        public DateTime? OTOut { get; set; }

        public bool IsPresent { get; set; }
        public bool IsAbsent { get; set; }
        public int isTimeInManuallyEdited { get; set; }
        public int isTimeOutManuallyEdited { get; set; }

        public int? tid { get; set; }
        public string tstatusName { get; set; }
        public DateTime? tTimeIn { get; set; }
        public DateTime? tTimeOut { get; set; }
    }

    public class ReviewDTREmployeeSummaryViewModel
    {
        public string EmployeeNo { get; set; }
        public string FullName { get; set; }
        public string PayrollType { get; set; }

        public double PaidLeaveDays { get; set; }
        public double NoPayLeaveDays { get; set; }

        // RAW VALUES (keep for calculations)
        public double NDHours { get; set; }
        public double OTHours { get; set; }
        public double OTNDHours { get; set; }
        public double RDHours { get; set; }
        public double RDOTHours { get; set; }
        public double RDNDHours { get; set; }
        public double SPLHours { get; set; }
        public double REGHours { get; set; }
        public double SPLHolidayHours { get; set; }
        public double SPLHolidayOTHours { get; set; }
        public double SPLHolidayNDHours { get; set; }
        public double SPLHolidayNDOTHours { get; set; }
        public double REGHolidayHours { get; set; }
        public double REGHolidayOTHours { get; set; }
        public double REGHolidayNDHours { get; set; }
        public double REGHolidayNDOTHours { get; set; }
        public double RDNDOTHours { get; set; }
        // Special Holiday Rest Day
        public double SPLHolidayRESTHours { get; set; }
        public double SPLHolidayRESTOTHours { get; set; }
        public double SPLHolidayRESTNDHours { get; set; }
        public double SPLHolidayRESTNDOTHours { get; set; }

        // Legal Holiday Rest Day
        public double REGHolidayRESTNDHours { get; set; }
        public double REGHolidayRESTNDOTHours { get; set; }
        public double REGHolidayRESTHours { get; set; }
        public double REGHolidayRESTOTHours { get; set; }
        public double TotalLateMinutes { get; set; }
        public double TotalUndertimeMinutes { get; set; }
        public double TotalPresentDays { get; set; }
        public double TotalAbsentDays { get; set; }

        // FORMATTED VALUES (for display)
        public string NDHoursFormatted => TimeFormatHelper.FormatHours(NDHours);
        public string OTHoursFormatted => TimeFormatHelper.FormatHours(OTHours);
        public string OTNDHoursFormatted => TimeFormatHelper.FormatHours(OTNDHours);
        public string RDHoursFormatted => TimeFormatHelper.FormatHours(RDHours);
        public string RDOTHoursFormatted => TimeFormatHelper.FormatHours(RDOTHours);
        public string RDNDHoursFormatted => TimeFormatHelper.FormatHours(RDNDHours);
        public string SPLHolidayHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayHours);
        public string SPLHolidayOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayOTHours);
        public string SPLHolidayNDHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayNDHours);
        public string SPLHolidayNDOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayNDOTHours);
        public string REGHolidayHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayHours);
        public string REGHolidayOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayOTHours);
        public string REGHolidayNDHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayNDHours);
        public string REGHolidayNDOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayNDOTHours);
        public string RDNDOTHoursFormatted => TimeFormatHelper.FormatHours(RDNDOTHours);
        // Special Holiday Rest Day
        public string SPLHolidayRESTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTHours);
        public string SPLHolidayRESTOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTOTHours);
        public string SPLHolidayRESTNDHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTNDHours);
        public string SPLHolidayRESTNDOTHoursFormatted => TimeFormatHelper.FormatHours(SPLHolidayRESTNDOTHours);

        // Legal Holiday Rest Day
        public string REGHolidayRESTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTHours);
        public string REGHolidayRESTOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTOTHours);
        public string REGHolidayRESTNDHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTNDHours);
        public string REGHolidayRESTNDOTHoursFormatted => TimeFormatHelper.FormatHours(REGHolidayRESTNDOTHours);
        public string TotalLateMinutesFormatted => TimeFormatHelper.FormatMinutes(TotalLateMinutes);
        public string TotalUndertimeMinutesFormatted => TimeFormatHelper.FormatMinutes(TotalUndertimeMinutes);
        public string TotalAbsentDaysFormatted => TotalAbsentDays.ToString("0");
    }

    public class ProcessDTRResult
    {
        public bool Success { get; set; }
        public int ProcessedEmployees { get; set; }
        public string Message { get; set; }
    }
}