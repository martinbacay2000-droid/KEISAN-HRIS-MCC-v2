using Dapper;
using KEISAN_HRIS_v2.Security;
using KEISAN_HRIS_v2.Services.EmployeeProfile;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace KEISAN_HRIS_v2.Controllers.Users
{
    /// <summary>
    /// Generates Employee 201 PDF.
    ///   GET /Employee201/Generate?employeeNo=XX
    ///
    /// Access: FemployeeM (any non-NO_ACCESS level) — same gate as the employee list.
    /// </summary>
    [ModuleAuthorize("FemployeeM")]
    public class Employee201Controller : BaseController
    {
        private readonly IDbConnection _db;
        private readonly Employee201PdfService _pdfService;

        // Adjust this to match your actual logo path
        private const string LogoPath = "wwwroot/Fillow/images/your_logo_1.png";

        // Profile pictures root — matches UsersController / e_profile.profilePicturePath storage
        private const string ProfilePictureRoot = "wwwroot/uploads/profile-pictures";

        public Employee201Controller(IDbConnection db, Employee201PdfService pdfService)
        {
            _db = db;
            _pdfService = pdfService;
        }

        [HttpGet]
        public IActionResult Generate(string employeeNo)
        {
            if (string.IsNullOrWhiteSpace(employeeNo))
                return BadRequest("Employee number is required.");

            var data = BuildData(employeeNo);
            if (data == null)
                return NotFound($"Employee '{employeeNo}' not found.");

            var pdfBytes = _pdfService.Generate(data);
            var fileName = $"201_{SanitizeFileName(data.Basic.FullName)}_{DateTime.Now:yyyyMMdd}.pdf";

            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(pdfBytes, "application/pdf");
        }

        // ── Data builder ─────────────────────────────────────────────────────

        private Employee201Data? BuildData(string employeeNo)
        {
            // 1. Basic info
            var emp = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT
                    e.employeeNo,
                    e.firstName,
                    e.middleName,
                    e.lastName,
                    e.suffix,
                    DATE_FORMAT(e.dateHired,                '%Y/%m/%d') AS dateHired,
                    DATE_FORMAT(e.probationaryStartDate,    '%Y/%m/%d') AS probationaryStartDate,
                    DATE_FORMAT(e.dateOfRegApp,             '%Y/%m/%d') AS dateOfRegApp,
                    DATE_FORMAT(e.dateOfEmpTermInitial,     '%Y/%m/%d') AS dateOfEmpTermInitial,
                    DATE_FORMAT(e.dateOfEmpTermRehired,     '%Y/%m/%d') AS dateOfEmpTermRehired,
                    e.reason4TermInitial,
                    e.remarksInitial,
                    e.isRetired,
                    e.isActive,
                    ses.employmentStatusName,
                    sp.positionName,
                    sb.branchName,
                    sd.departmentName,
                    sr.rankName,
                    su.unitName
                FROM e_basicinfo e
                LEFT JOIN s_employmentstatus ses ON ses.employmentStatusCode = e.employmentStatus
                LEFT JOIN s_position         sp  ON sp.positionCode          = e.positionCode
                LEFT JOIN s_branch           sb  ON sb.branchCode            = e.branchCode
                LEFT JOIN s_department       sd  ON sd.departmentCode        = e.departmentCode
                LEFT JOIN s_rank             sr  ON sr.rankCode              = e.rankCode
                LEFT JOIN s_unit             su  ON su.unitCode              = e.unitCode
                WHERE e.employeeNo = @employeeNo
                  AND (e.dtDeleted IS NULL OR e.dtDeleted = '0000-00-00 00:00:00')
                LIMIT 1",
                new { employeeNo });

            if (emp == null) return null;

            // Build full name
            string first = ((string?)emp.firstName ?? "").Trim();
            string mid = ((string?)emp.middleName ?? "").Trim();
            string last = ((string?)emp.lastName ?? "").Trim();
            string fullName = string.IsNullOrWhiteSpace(mid)
                ? $"{last}, {first}".Trim()
                : $"{last}, {first} {mid}".Trim();

            // Profile picture path
            var profileRow = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT profilePicturePath
                FROM e_profile
                WHERE employeeNo = @employeeNo AND isActive = 1
                LIMIT 1",
                new { employeeNo });

            string? picPath = null;
            if (profileRow != null && !string.IsNullOrWhiteSpace((string?)profileRow.profilePicturePath))
            {
                // profilePicturePath is stored as a relative web path, e.g. "/uploads/profile-pictures/xxx.jpg"
                // Convert to physical path for QuestPDF
                var relativePath = ((string)profileRow.profilePicturePath).TrimStart('/');
                var physical = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);
                if (System.IO.File.Exists(physical))
                    picPath = physical;
            }

            var basic = new Employee201BasicInfo
            {
                EmployeeNo = (string?)emp.employeeNo,
                FullName = fullName,
                Suffix = (string?)emp.suffix,
                DateHired = (string?)emp.dateHired,
                EmploymentStatus = (string?)emp.employmentStatusName,
                ProbationaryStartDate = (string?)emp.probationaryStartDate,
                DateOfRegApp = (string?)emp.dateOfRegApp,
                Department = (string?)emp.departmentName,
                Position = (string?)emp.positionName,
                Branch = (string?)emp.branchName,
                Rank = (string?)emp.rankName,
                Location = (string?)emp.unitName,
                IsRetired = ((int?)emp.isRetired ?? 0) == 1,
                IsActive = ((int?)emp.isActive ?? 1) == 1,

                SepDateInitial = (string?)emp.dateOfEmpTermInitial,
                SepReasonInitial = (string?)emp.reason4TermInitial,
                SepRemarksInitial = (string?)emp.remarksInitial,
                SepDateRehired = (string?)emp.dateOfEmpTermRehired,

                ProfilePicturePath = picPath
            };

            // 2. Personal info
            var pi = _db.QueryFirstOrDefault<dynamic>(@"
                SELECT
                    p.gender,
                    CAST(p.weight  AS CHAR) AS weight,
                    CAST(p.height  AS CHAR) AS height,
                    DATE_FORMAT(p.dateOfBirth, '%Y/%m/%d') AS dateOfBirth,
                    p.birthPlace,
                    p.homePhoneNo,
                    p.mobileNo,
                    p.emailAddress,
                    p.religion,
                    CAST(p.zipCode AS CHAR) AS zipCode,
                    p.presentAddress,
                    p.permanentAddress,
                    p.fatherName,
                    p.motherMaidenName,
                    p.personToNotify,
                    p.relationship,
                    p.contactNo,
                    p.civilStatus,
                    p.nameOfSpouse,
                    DATE_FORMAT(p.spouseDateOfBirth, '%Y/%m/%d') AS spouseDateOfBirth,
                    p.occupation,
                    c.citizenshipName
                FROM e_personalinfo p
                LEFT JOIN s_citizenship c ON c.citizenshipCode = p.citizenshipCode AND c.isActive = 1
                WHERE p.employeeNo = @employeeNo
                LIMIT 1",
                new { employeeNo });

            Employee201PersonalInfo? personal = null;
            if (pi != null)
            {
                personal = new Employee201PersonalInfo
                {
                    Gender = (string?)pi.gender,
                    Weight = (string?)pi.weight,
                    Height = (string?)pi.height,
                    DateOfBirth = (string?)pi.dateOfBirth,
                    BirthPlace = (string?)pi.birthPlace,
                    HomePhoneNo = (string?)pi.homePhoneNo,
                    MobileNo = (string?)pi.mobileNo,
                    EmailAddress = (string?)pi.emailAddress,
                    Religion = (string?)pi.religion,
                    ZipCode = (string?)pi.zipCode,
                    PresentAddress = (string?)pi.presentAddress,
                    PermanentAddress = (string?)pi.permanentAddress,
                    FatherName = (string?)pi.fatherName,
                    MotherMaidenName = (string?)pi.motherMaidenName,
                    PersonToNotify = (string?)pi.personToNotify,
                    Relationship = (string?)pi.relationship,
                    ContactNo = (string?)pi.contactNo,
                    CivilStatus = (string?)pi.civilStatus,
                    NameOfSpouse = (string?)pi.nameOfSpouse,
                    SpouseDateOfBirth = (string?)pi.spouseDateOfBirth,
                    Occupation = (string?)pi.occupation,
                    CitizenshipName = (string?)pi.citizenshipName
                };
            }

            // 3. Siblings / relatives
            var siblings = _db.Query<dynamic>(@"
                SELECT
                    nameOfSibling AS name,
                    DATE_FORMAT(dateOfBirth, '%Y/%m/%d') AS dateOfBirth,
                    relationship,
                    gender
                FROM e_siblings
                WHERE employeeNo = @employeeNo
                  AND isActive = 1
                  AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                ORDER BY dateOfBirth DESC",
                new { employeeNo })
            .Select(r => new Employee201Sibling
            {
                Name = (string?)r.name,
                DateOfBirth = (string?)r.dateOfBirth,
                Relationship = (string?)r.relationship,
                Gender = (string?)r.gender
            }).ToList();

            // 4. Educational background
            var schools = _db.Query<dynamic>(@"
                SELECT
                    nameOfSchool,
                    schoolType,
                    course,
                    CAST(yearGraduated AS CHAR) AS yearGraduated,
                    attain
                FROM e_school
                WHERE employeeNo = @employeeNo
                  AND isActive = 1
                  AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                ORDER BY yearGraduated DESC",
                new { employeeNo })
            .Select(r => new Employee201School
            {
                NameOfSchool = (string?)r.nameOfSchool,
                SchoolType = (string?)r.schoolType,
                Course = (string?)r.course,
                YearGraduated = (string?)r.yearGraduated,
                Attain = (string?)r.attain
            }).ToList();

            // 5. Licenses & certifications
            var licenses = _db.Query<dynamic>(@"
                SELECT
                    licenseAndCertificateNo          AS licenseNo,
                    licenseAndCertificateDescription AS description,
                    DATE_FORMAT(registrationDate, '%Y/%m/%d') AS registrationDate,
                    DATE_FORMAT(issueDate,        '%Y/%m/%d') AS issueDate,
                    DATE_FORMAT(validUntil,       '%Y/%m/%d') AS validUntil
                FROM e_licenseandcertificate
                WHERE employeeNo = @employeeNo
                  AND isActive = 1
                  AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                ORDER BY id DESC",
                new { employeeNo })
            .Select(r => new Employee201License
            {
                LicenseNo = (string?)r.licenseNo,
                Description = (string?)r.description,
                RegistrationDate = (string?)r.registrationDate,
                IssueDate = (string?)r.issueDate,
                ValidUntil = (string?)r.validUntil
            }).ToList();

            // 6. Employment history
            var employments = _db.Query<dynamic>(@"
                SELECT
                    companyName,
                    position,
                    address,
                    DATE_FORMAT(fromDate, '%Y/%m/%d') AS fromDate,
                    DATE_FORMAT(toDate,   '%Y/%m/%d') AS toDate
                FROM e_employmenthistory
                WHERE employeeNo = @employeeNo
                  AND isActive = 1
                  AND (dtDeleted IS NULL OR dtDeleted = '0000-00-00 00:00:00')
                ORDER BY fromDate DESC",
                new { employeeNo })
            .Select(r => new Employee201Employment
            {
                CompanyName = (string?)r.companyName,
                Position = (string?)r.position,
                Address = (string?)r.address,
                FromDate = (string?)r.fromDate,
                ToDate = (string?)r.toDate
            }).ToList();

            // 7. Trainings
            var trainings = _db.Query<dynamic>(@"
                SELECT
                    trainingTitle,
                    trainingProvider,
                    trainingVenue,
                    DATE_FORMAT(dateFrom, '%Y/%m/%d') AS dateFrom,
                    DATE_FORMAT(dateTo,   '%Y/%m/%d') AS dateTo
                FROM e_training
                WHERE employeeNo = @employeeNo
                  AND isActive = 1
                ORDER BY dateFrom DESC",
                new { employeeNo })
            .Select(r => new Employee201Training
            {
                TrainingTitle = (string?)r.trainingTitle,
                TrainingProvider = (string?)r.trainingProvider,
                TrainingVenue = (string?)r.trainingVenue,
                DateFrom = (string?)r.dateFrom,
                DateTo = (string?)r.dateTo
            }).ToList();

            return new Employee201Data
            {
                Basic = basic,
                Personal = personal,
                Siblings = siblings,
                Schools = schools,
                Licenses = licenses,
                Employments = employments,
                Trainings = trainings,
                PrintedDate = DateTime.Now.ToString("M/d/yyyy H:mm"),
                CompanyLogoPath = System.IO.File.Exists(LogoPath) ? LogoPath : null
            };
        }

        // ── Utility ──────────────────────────────────────────────────────────

        private static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Employee";
            return string.Concat(name.Split(Path.GetInvalidFileNameChars()))
                         .Replace(" ", "_")
                         .Replace(",", "");
        }
    }
}