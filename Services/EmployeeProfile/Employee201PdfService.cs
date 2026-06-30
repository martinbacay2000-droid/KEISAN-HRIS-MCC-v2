using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.EmployeeProfile
{
    // ── Data models ──────────────────────────────────────────────────────────

    public class Employee201BasicInfo
    {
        public string? EmployeeNo { get; set; }
        public string? FullName { get; set; }
        public string? Suffix { get; set; }
        public string? DateHired { get; set; }
        public string? EmploymentStatus { get; set; }
        public string? ProbationaryStartDate { get; set; }
        public string? DateOfRegApp { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? Branch { get; set; }
        public string? Rank { get; set; }
        public bool IsRetired { get; set; }
        public string? Location { get; set; }
        public string? SepDateInitial { get; set; }
        public string? SepReasonInitial { get; set; }
        public string? SepRemarksInitial { get; set; }
        public string? SepDateRehired { get; set; }
        public string? SepReasonRehired { get; set; }
        public string? SepRemarksRehired { get; set; }
        public bool IsActive { get; set; } = true;
        public string? ProfilePicturePath { get; set; }
    }

    public class Employee201PersonalInfo
    {
        public string? Gender { get; set; }
        public string? Weight { get; set; }
        public string? Height { get; set; }
        public string? DateOfBirth { get; set; }
        public string? BirthPlace { get; set; }
        public string? HomePhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailAddress { get; set; }
        public string? Religion { get; set; }
        public string? ZipCode { get; set; }
        public string? PresentAddress { get; set; }
        public string? PermanentAddress { get; set; }
        public string? FatherName { get; set; }
        public string? MotherMaidenName { get; set; }
        public string? PersonToNotify { get; set; }
        public string? Relationship { get; set; }
        public string? ContactNo { get; set; }
        public string? CivilStatus { get; set; }
        public string? NameOfSpouse { get; set; }
        public string? SpouseDateOfBirth { get; set; }
        public string? Occupation { get; set; }
        public string? CitizenshipName { get; set; }
    }

    public class Employee201Sibling
    {
        public string? Name { get; set; }
        public string? DateOfBirth { get; set; }
        public string? Relationship { get; set; }
        public string? Gender { get; set; }
    }

    public class Employee201School
    {
        public string? NameOfSchool { get; set; }
        public string? SchoolType { get; set; }
        public string? Course { get; set; }
        public string? YearGraduated { get; set; }
        public string? Attain { get; set; }
    }

    public class Employee201License
    {
        public string? LicenseNo { get; set; }
        public string? Description { get; set; }
        public string? RegistrationDate { get; set; }
        public string? IssueDate { get; set; }
        public string? ValidUntil { get; set; }
    }

    public class Employee201Employment
    {
        public string? CompanyName { get; set; }
        public string? Position { get; set; }
        public string? Address { get; set; }
        public string? FromDate { get; set; }
        public string? ToDate { get; set; }
    }

    public class Employee201Training
    {
        public string? TrainingTitle { get; set; }
        public string? TrainingProvider { get; set; }
        public string? TrainingVenue { get; set; }
        public string? DateFrom { get; set; }
        public string? DateTo { get; set; }
    }

    public class Employee201Data
    {
        public Employee201BasicInfo Basic { get; set; } = new();
        public Employee201PersonalInfo? Personal { get; set; }
        public List<Employee201Sibling> Siblings { get; set; } = new();
        public List<Employee201School> Schools { get; set; } = new();
        public List<Employee201License> Licenses { get; set; } = new();
        public List<Employee201Employment> Employments { get; set; } = new();
        public List<Employee201Training> Trainings { get; set; } = new();
        public string PrintedDate { get; set; } = DateTime.Now.ToString("M/d/yyyy H:mm");
        public string? CompanyLogoPath { get; set; }
    }

    // ── PDF Service ───────────────────────────────────────────────────────────

    public class Employee201PdfService
    {
        private const string Font = "Arial";
        private const float Sz = 9f;    // body
        private const float SzSm = 8f;    // table cells
        private const float SzHdr = 10f;   // section heading

        // Label column width used for single-column LV rows
        private const float LblW = 160f;

        private static readonly string Grey = Colors.Grey.Darken2;
        private static readonly string LineColor = Colors.Grey.Lighten1;
        private static readonly string GreenCol = Colors.Green.Darken2;
        private static readonly string RedCol = Colors.Red.Darken2;

        // ── Entry point ───────────────────────────────────────────────────────

        public byte[] Generate(Employee201Data d)
        {
            var doc = Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.MarginTop(45);
                    page.MarginBottom(45);
                    page.MarginHorizontal(50);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(Sz));

                    page.Footer().Column(footer =>
                    {
                        footer.Item()
                              .BorderTop(1)
                              .BorderColor(Colors.Grey.Medium)
                              .PaddingTop(6)
                              .AlignCenter()
                              .Text("Your Company Address    Tel: Your Company Number    www.yourcompany.com")
                              .FontFamily(Font)
                              .FontSize(8)
                              .FontColor(Colors.Grey.Darken2);
                    });

                    page.Content().Column(col =>
                    {
                        PageHeader(col, d);
                        col.Item().PaddingTop(14).Text("");
                        SectionI(col, d);
                        col.Item().PaddingTop(12).Text("");
                        SectionII(col, d);
                        col.Item().PaddingTop(12).Text("");
                        SectionIII(col, d);
                        col.Item().PaddingTop(12).Text("");
                        SectionIV(col, d);
                        col.Item().PaddingTop(12).Text("");
                        SectionV(col, d);
                        col.Item().PaddingTop(12).Text("");
                        SectionVI(col, d);
                    });
                });
            });

            return doc.GeneratePdf();
        }

        // ── Page header ───────────────────────────────────────────────────────

        private static void PageHeader(ColumnDescriptor col, Employee201Data d)
        {
            col.Item().Row(r =>
            {
                r.ConstantItem(100f).Column(lc =>
                {
                    if (!string.IsNullOrWhiteSpace(d.CompanyLogoPath) &&
                        File.Exists(d.CompanyLogoPath))
                        lc.Item().Width(90).Image(d.CompanyLogoPath);
                });

                r.RelativeItem().AlignCenter().AlignMiddle()
                 .Text("EMPLOYEE PROFILE")
                 .FontFamily(Font).FontSize(14).Bold();

                r.ConstantItem(100f).AlignRight().AlignMiddle()
                 .Text(d.PrintedDate).FontSize(8).FontColor(Grey);
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Section heading with underline
        private static void Heading(ColumnDescriptor col, string title)
        {
            col.Item()
               .BorderBottom(1).BorderColor(LineColor)
               .PaddingBottom(3)
               .Text(title).FontFamily(Font).FontSize(SzHdr).Bold();
        }

        // Single label : value row (label is grey, value is bold)
        private static void LV(ColumnDescriptor col, string label, string? value,
                                float lblW = LblW)
        {
            col.Item().PaddingTop(3).Row(r =>
            {
                r.ConstantItem(lblW).Text(label + " :").FontSize(Sz).FontColor(Grey);
                r.RelativeItem().Text(value ?? "").FontSize(Sz).Bold();
            });
        }

        // Two label:value pairs side-by-side — fixed widths prevent text overflow
        private static void LV2(ColumnDescriptor col,
                                 string lbl1, string? val1,
                                 string lbl2, string? val2,
                                 float lblW = 130f, float val1W = 170f)
        {
            col.Item().PaddingTop(3).Row(r =>
            {
                r.ConstantItem(lblW).Text(lbl1 + " :").FontSize(Sz).FontColor(Grey);
                r.ConstantItem(val1W).Text(val1 ?? "").FontSize(Sz).Bold();
                r.ConstantItem(lblW).Text(string.IsNullOrEmpty(lbl2) ? "" : lbl2 + " :")
                                     .FontSize(Sz).FontColor(Grey);
                r.RelativeItem().Text(val2 ?? "").FontSize(Sz).Bold();
            });
        }

        // Table header cell style
        private static IContainer TH(IContainer c) =>
            c.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingBottom(3);

        // ── I. Basic Information ──────────────────────────────────────────────

        private static void SectionI(ColumnDescriptor col, Employee201Data d)
        {
            var b = d.Basic;

            col.Item().Column(sec =>
            {
                Heading(sec, "I. BASIC INFORMATION :");
                sec.Item().PaddingTop(8).Text("");

                // Outer row: fields (left) + photo (right, fixed 110 pt)
                sec.Item().Row(outer =>
                {
                    outer.RelativeItem().Column(f =>
                    {
                        // Employee No + Suffix on same line
                        f.Item().PaddingTop(2).Row(r =>
                        {
                            r.ConstantItem(LblW).Text("Employee No. :").FontSize(Sz).FontColor(Grey);
                            r.ConstantItem(130f).Text(b.EmployeeNo ?? "").FontSize(Sz).Bold();
                            r.ConstantItem(55f).Text("Suffix :").FontSize(Sz).FontColor(Grey);
                            r.RelativeItem().Text(b.Suffix ?? "").FontSize(Sz).Bold();
                        });

                        LV(f, "Full Name", b.FullName);
                        LV(f, "Date Hired", b.DateHired);
                        LV(f, "Employment Status", b.EmploymentStatus);
                        LV(f, "Date of Prob. Appointment", b.ProbationaryStartDate);
                        LV(f, "Date of Reg. Appointment", b.DateOfRegApp);
                        LV(f, "Department", b.Department);

                        // Position + Branch on same line
                        f.Item().PaddingTop(3).Row(r =>
                        {
                            r.ConstantItem(LblW).Text("Position :").FontSize(Sz).FontColor(Grey);
                            r.RelativeItem().Text(b.Position ?? "").FontSize(Sz).Bold();
                            r.ConstantItem(55f).Text("Branch :").FontSize(Sz).FontColor(Grey);
                            r.ConstantItem(90f).Text(b.Branch ?? "").FontSize(Sz).Bold();
                        });

                        LV(f, "Rank", b.Rank);

                        // System Status with colour
                        f.Item().PaddingTop(3).Row(r =>
                        {
                            r.ConstantItem(LblW).Text("System Status :").FontSize(Sz).FontColor(Grey);
                            r.RelativeItem()
                             .Text(b.IsActive ? "ACTIVE" : "INACTIVE")
                             .FontSize(Sz).Bold()
                             .FontColor(b.IsActive ? GreenCol : RedCol);
                        });

                        LV(f, "Retired", b.IsRetired ? "Yes" : "No");

                        if (!string.IsNullOrWhiteSpace(b.Location))
                            LV(f, "Location", b.Location);
                    });

                    // Photo box — fixed 110 pt wide, does NOT participate in text flow
                    outer.ConstantItem(110f).PaddingLeft(10).Column(pic =>
                    {
                        if (!string.IsNullOrWhiteSpace(b.ProfilePicturePath) &&
                            File.Exists(b.ProfilePicturePath))
                        {
                            pic.Item().Width(95).Height(110)
                               .Image(b.ProfilePicturePath, ImageScaling.FitArea);
                        }
                        else
                        {
                            pic.Item().Width(95).Height(110)
                               .Border(1).BorderColor(LineColor)
                               .AlignCenter().AlignMiddle()
                               .Text("No Photo").FontSize(8).FontColor(Grey);
                        }
                    });
                });

                // Record of Separation
                sec.Item().PaddingTop(16).AlignCenter()
                   .Text("Record of Separation")
                   .FontFamily(Font).FontSize(Sz).Italic();

                sec.Item().PaddingTop(6).Row(sr =>
                {
                    // Initial column
                    sr.RelativeItem().Column(init =>
                    {
                        init.Item().AlignCenter().Text("Initial").FontSize(Sz).Underline();
                        SepRow(init, "Separation Date", b.SepDateInitial);
                        SepRow(init, "Reason", b.SepReasonInitial);
                        SepRow(init, "Remarks", b.SepRemarksInitial);
                    });

                    sr.ConstantItem(20f).Text("");

                    // Rehired column
                    sr.RelativeItem().Column(reh =>
                    {
                        reh.Item().AlignCenter().Text("Rehired").FontSize(Sz).Underline();
                        SepRow(reh, "Separation Date", b.SepDateRehired);
                        SepRow(reh, "Reason", b.SepReasonRehired);
                        SepRow(reh, "Remarks", b.SepRemarksRehired);
                    });
                });
            });
        }

        private static void SepRow(ColumnDescriptor col, string label, string? value)
        {
            col.Item().PaddingTop(3).Row(r =>
            {
                r.ConstantItem(105f).Text(label + " :").FontSize(Sz).FontColor(Grey);
                r.RelativeItem().Text(value ?? "").FontSize(Sz).Bold();
            });
        }

        // ── II. Personal Information ──────────────────────────────────────────

        private static void SectionII(ColumnDescriptor col, Employee201Data d)
        {
            col.Item().Column(sec =>
            {
                Heading(sec, "II. PERSONAL INFORMATION :");

                var p = d.Personal;
                if (p == null)
                {
                    sec.Item().PaddingTop(5)
                       .Text("No personal information on record.")
                       .FontSize(Sz).FontColor(Grey).Italic();
                    return;
                }

                sec.Item().PaddingTop(6).Text("");

                LV2(sec, "Gender", p.Gender,
                         "Height", p.Height != null ? p.Height + " cm" : null);

                LV2(sec, "Weight", p.Weight != null ? p.Weight + " kg" : null,
                         "Birth Place", p.BirthPlace);

                LV2(sec, "Birthday", p.DateOfBirth,
                         "Mobile No.", p.MobileNo);

                LV2(sec, "Homephone No.", p.HomePhoneNo,
                         "Citizenship", p.CitizenshipName);

                LV2(sec, "Email Address", p.EmailAddress,
                         "Name of Spouse", p.NameOfSpouse);

                LV2(sec, "Civil Status", p.CivilStatus,
                         "Spouse Birthdate", p.SpouseDateOfBirth);

                LV2(sec, "Religion", p.Religion,
                         "Occupation", p.Occupation);

                LV(sec, "Zip Code", p.ZipCode, 130f);

                sec.Item().PaddingTop(6).Text("");
                LV(sec, "Present Address", p.PresentAddress);
                LV(sec, "Permanent Address", p.PermanentAddress);

                sec.Item().PaddingTop(4).Text("");
                LV2(sec, "Father's Name", p.FatherName,
                         "Mother's Name", p.MotherMaidenName);

                // Emergency contact — three values on one row
                // Fixed widths: 130 + 110 + 80 + 90 + 65 + RelativeItem = 475pt < 512pt usable
                sec.Item().PaddingTop(3).Row(r =>
                {
                    r.ConstantItem(130f).Text("Emergency Contact :").FontSize(Sz).FontColor(Grey);
                    r.ConstantItem(110f).Text(p.PersonToNotify ?? "").FontSize(Sz).Bold();
                    r.ConstantItem(80f).Text("Relationship :").FontSize(Sz).FontColor(Grey);
                    r.ConstantItem(90f).Text(p.Relationship ?? "").FontSize(Sz).Bold();
                    r.ConstantItem(65f).Text("Contact No. :").FontSize(Sz).FontColor(Grey);
                    r.RelativeItem().Text(p.ContactNo ?? "").FontSize(Sz).Bold();
                });

                // Relatives / children table
                if (d.Siblings.Any())
                {
                    sec.Item().PaddingTop(12).AlignCenter()
                       .Text("Record of Employee Relatives")
                       .FontFamily(Font).FontSize(Sz).Italic();

                    sec.Item().PaddingTop(5).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Element(TH).Text("Name").FontSize(SzSm).Bold();
                            h.Cell().Element(TH).Text("Date of Birth").FontSize(SzSm).Bold();
                            h.Cell().Element(TH).Text("Relationship").FontSize(SzSm).Bold();
                            h.Cell().Element(TH).Text("Gender").FontSize(SzSm).Bold();
                        });

                        foreach (var s in d.Siblings)
                        {
                            t.Cell().PaddingVertical(3).Text(s.Name ?? "").FontSize(SzSm);
                            t.Cell().PaddingVertical(3).Text(s.DateOfBirth ?? "").FontSize(SzSm);
                            t.Cell().PaddingVertical(3).Text(s.Relationship ?? "").FontSize(SzSm);
                            t.Cell().PaddingVertical(3).Text(s.Gender ?? "").FontSize(SzSm);
                        }
                    });
                }
            });
        }

        // ── III. Educational Background ───────────────────────────────────────

        private static void SectionIII(ColumnDescriptor col, Employee201Data d)
        {
            col.Item().Column(sec =>
            {
                Heading(sec, "III. EDUCATIONAL BACKGROUND :");

                if (!d.Schools.Any())
                {
                    sec.Item().PaddingTop(5)
                       .Text("No educational background on record.")
                       .FontSize(Sz).FontColor(Grey).Italic();
                    return;
                }

                sec.Item().PaddingTop(5).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(4);
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(TH).Text("Name of School").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Type").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Course").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Year").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Attainment").FontSize(SzSm).Bold();
                    });

                    foreach (var s in d.Schools)
                    {
                        t.Cell().PaddingVertical(3).Text(s.NameOfSchool ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(s.SchoolType ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(s.Course ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(s.YearGraduated ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(s.Attain ?? "").FontSize(SzSm);
                    }
                });
            });
        }

        // ── IV. Licenses & Certifications ────────────────────────────────────

        private static void SectionIV(ColumnDescriptor col, Employee201Data d)
        {
            col.Item().Column(sec =>
            {
                Heading(sec, "IV. LICENSES AND CERTIFICATIONS :");

                if (!d.Licenses.Any())
                {
                    sec.Item().PaddingTop(5)
                       .Text("No licenses or certifications on record.")
                       .FontSize(Sz).FontColor(Grey).Italic();
                    return;
                }

                sec.Item().PaddingTop(5).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(TH).Text("Description").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Number").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Registration Date").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Issue Date").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Valid Until").FontSize(SzSm).Bold();
                    });

                    foreach (var l in d.Licenses)
                    {
                        t.Cell().PaddingVertical(3).Text(l.Description ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(l.LicenseNo ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(l.RegistrationDate ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(l.IssueDate ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(l.ValidUntil ?? "").FontSize(SzSm);
                    }
                });
            });
        }

        // ── V. Employment History ─────────────────────────────────────────────

        private static void SectionV(ColumnDescriptor col, Employee201Data d)
        {
            col.Item().Column(sec =>
            {
                Heading(sec, "V. EMPLOYMENT HISTORY :");

                if (!d.Employments.Any())
                {
                    sec.Item().PaddingTop(5)
                       .Text("No employment history on record.")
                       .FontSize(Sz).FontColor(Grey).Italic();
                    return;
                }

                sec.Item().PaddingTop(5).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(TH).Text("Company").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Position").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Address").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("From").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("To").FontSize(SzSm).Bold();
                    });

                    foreach (var e in d.Employments)
                    {
                        t.Cell().PaddingVertical(3).Text(e.CompanyName ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(e.Position ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(e.Address ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(e.FromDate ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(e.ToDate ?? "").FontSize(SzSm);
                    }
                });
            });
        }

        // ── VI. Trainings ─────────────────────────────────────────────────────

        private static void SectionVI(ColumnDescriptor col, Employee201Data d)
        {
            col.Item().Column(sec =>
            {
                Heading(sec, "VI. TRAININGS :");

                if (!d.Trainings.Any())
                {
                    sec.Item().PaddingTop(5)
                       .Text("No trainings on record.")
                       .FontSize(Sz).FontColor(Grey).Italic();
                    return;
                }

                sec.Item().PaddingTop(5).Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    t.Header(h =>
                    {
                        h.Cell().Element(TH).Text("Title").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Provider").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("Venue").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("From").FontSize(SzSm).Bold();
                        h.Cell().Element(TH).Text("To").FontSize(SzSm).Bold();
                    });

                    foreach (var tr in d.Trainings)
                    {
                        t.Cell().PaddingVertical(3).Text(tr.TrainingTitle ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(tr.TrainingProvider ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(tr.TrainingVenue ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(tr.DateFrom ?? "").FontSize(SzSm);
                        t.Cell().PaddingVertical(3).Text(tr.DateTo ?? "").FontSize(SzSm);
                    }
                });
            });
        }
    }
}