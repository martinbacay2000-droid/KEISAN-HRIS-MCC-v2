namespace KEISAN_HRIS_v2.Models.Report
{
    public class AttendanceReportModel
    {
        // ── Common fields (all reports) ──────────────────────────────────────────
        public string? employeeNo { get; set; }
        public string? lastName { get; set; }
        public string? firstName { get; set; }
        public string? middleName { get; set; }
        public string? branchCode { get; set; }
        public string? departmentCode { get; set; }
        public string? employmentStatus { get; set; }
        public string? positionCode { get; set; }

        // ── Perfect Attendance ───────────────────────────────────────────────────
        public int? withOB { get; set; }
        public decimal? paidLeave { get; set; }
        public int? presentDays { get; set; }

        // ── Absent Reports (Detail + Summary) ───────────────────────────────────
        public decimal? absentCount { get; set; }
        public decimal? NoTimeOut { get; set; }
        public decimal? AWOL { get; set; }
        public decimal? AbsentWithLeave { get; set; }

        // ── Absent Detail only ───────────────────────────────────────────────────
        public string? dateAbsent { get; set; }
        public string? scheduleIn { get; set; }
        public string? scheduleOut { get; set; }
        public string? attendanceStatus { get; set; }
        public int? obID { get; set; }

        // ── Tardiness Reports (Detail + Summary) ─────────────────────────────────
        public decimal? renderLate { get; set; }
        public decimal? renderUndertime { get; set; }

        // ── Tardiness Summary only ────────────────────────────────────────────────
        public decimal? totalLate { get; set; }
        public decimal? totalUndertime { get; set; }
        public int? lateFrequency { get; set; }
        public int? undertimeFrequency { get; set; }

        // ── Tardiness Detail only ─────────────────────────────────────────────────
        public string? timeIn { get; set; }
        public string? timeOut { get; set; }
    }
}