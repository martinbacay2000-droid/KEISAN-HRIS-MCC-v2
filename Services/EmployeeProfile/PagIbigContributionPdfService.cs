using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.EmployeeProfile
{
    // ── Data models ──────────────────────────────────────────────────────────────

    public class PagIbigContributionRow
    {
        public string? Month { get; set; }  // e.g. "January"
        public string? Year { get; set; }  // e.g. "2026"
        public decimal EEContribution { get; set; }  // deductionPIFemployee
        public decimal ERContribution { get; set; }  // deductionPIFemployer
        public decimal TotalContribution { get; set; }  // deductionPIFTotal
        public decimal HdmfLoan { get; set; }  // hdmfLoan
    }

    public class PagIbigContributionReportData
    {
        public string? EmployeeNo { get; set; }
        public string? EmployeeName { get; set; }  // FIRSTNAME MIDDLENAME LASTNAME
        public string? GenderPrefix { get; set; }  // "MR.", "MS.", or "MR/MS."
        public string? HdmfNo { get; set; }  // hdmfNo from e_payrolldetails
        public string? Purpose { get; set; }  // user-entered purpose (optional)
        public string? IssuedDate { get; set; }  // e.g. "25th day of March 2026"
        public string? IssuedCity { get; set; }

        public string? CompanyName { get; set; }
        public string? SignatoryName { get; set; }  // from session userFullName (as-is)
        public string? SignatoryTitle { get; set; }  // positionName from s_position

        public List<PagIbigContributionRow> Rows { get; set; } = new();
    }

    // ── PDF Service ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates the Pag-IBIG (HDMF) Certificate of Contributions PDF using QuestPDF.
    ///
    /// Changes from v1:
    ///   - MR/MS prefix resolved from employee gender (MALE → MR., FEMALE → MS.)
    ///   - Employee name displayed as FIRSTNAME MIDDLENAME LASTNAME
    ///   - Signatory name/title driven by the logged-in user (from session)
    ///   - Company footer (address, tel, website) added at page bottom with separator
    /// </summary>
    public class PagIbigContributionPdfService
    {
        private const string FontName = "Arial";

        // Footer contact details
        private const string FooterLine =
            "51 Timog Avenue, South Triangle, Quezon City 1103 Philippines" +
            "     Tel: (63)(2) 8863-7777     www.luxenthotel.com";

        public byte[] Generate(PagIbigContributionReportData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.MarginTop(50);
                    page.MarginBottom(50);
                    page.MarginHorizontal(60);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily(FontName).FontSize(11));

                    // ── Page footer (address block) ───────────────────────────
                    page.Footer().Column(footer =>
                    {
                        footer.Item()
                              .BorderTop(1)
                              .BorderColor(Colors.Grey.Medium)
                              .PaddingTop(6)
                              .AlignCenter()
                              .Text(FooterLine)
                              .FontFamily(FontName)
                              .FontSize(8)
                              .FontColor(Colors.Grey.Darken2);
                    });

                    // ── Main content ──────────────────────────────────────────
                    page.Content().Column(col =>
                    {
                        // ── Logo ──────────────────────────────────────────────
                        col.Item().Width(100)
                            .Image("wwwroot/Fillow/images/luxent_logo.png");

                        col.Item().PaddingTop(10).Text("");

                        // ── Title ─────────────────────────────────────────────
                        col.Item().AlignCenter()
                            .Text("CERTIFICATE OF CONTRIBUTIONS")
                            .FontFamily(FontName).FontSize(13).Bold().Underline();

                        col.Item().PaddingTop(15).Text("");

                        // ── Certifying paragraph ──────────────────────────────
                        //    Uses resolved gender prefix (MR. / MS. / MR/MS.)
                        col.Item().Text(txt =>
                        {
                            txt.DefaultTextStyle(s => s.FontFamily(FontName).LineHeight(1.5f));
                            txt.Justify();
                            txt.Span("This is to certify that we have remitted the Pag-IBIG (HDMF) contributions of ");
                            txt.Span($"{data.GenderPrefix ?? "MR/MS."} {data.EmployeeName}")
                               .Bold();
                            txt.Span(" for the following period.");
                        });

                        col.Item().PaddingTop(10).Text("");

                        // ── HDMF No. ──────────────────────────────────────────
                        col.Item().Text(txt =>
                        {
                            txt.DefaultTextStyle(s => s.FontFamily(FontName));
                            txt.Span("HDMF No. : ").FontSize(11);
                            txt.Span(data.HdmfNo ?? "N/A").Bold().FontSize(11);
                        });

                        col.Item().PaddingTop(4).Text("");

                        // ── Contribution table ────────────────────────────────
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2.0f); // Month
                                cols.RelativeColumn(1.5f); // Year
                                cols.RelativeColumn(2.5f); // EE Contribution
                                cols.RelativeColumn(2.5f); // ER Contribution
                                cols.RelativeColumn(2.0f); // Total
                                cols.RelativeColumn(2.0f); // HDMF Loan
                            });

                            static IContainer HeaderCell(IContainer c) =>
                                c.Border(1).BorderColor(Colors.Black)
                                 .Background(Colors.White)
                                 .Padding(4).AlignCenter();

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderCell)
                                    .Text("Month").Bold().FontSize(10).FontFamily(FontName);
                                header.Cell().Element(HeaderCell)
                                    .Text("Year").Bold().FontSize(10).FontFamily(FontName);
                                header.Cell().Element(HeaderCell)
                                    .Text("EE Contribution").Bold().FontSize(10).FontFamily(FontName);
                                header.Cell().Element(HeaderCell)
                                    .Text("ER Contribution").Bold().FontSize(10).FontFamily(FontName);
                                header.Cell().Element(HeaderCell)
                                    .Text("Total").Bold().FontSize(10).FontFamily(FontName);
                                header.Cell().Element(HeaderCell)
                                    .Text("HDMF Loan").Bold().FontSize(10).FontFamily(FontName);
                            });

                            static IContainer DataCell(IContainer c) =>
                                c.Border(1).BorderColor(Colors.Black).Padding(4);

                            static IContainer DataCellRight(IContainer c) =>
                                c.Border(1).BorderColor(Colors.Black).Padding(4).AlignRight();

                            foreach (var row in data.Rows)
                            {
                                table.Cell().Element(DataCell)
                                    .Text(row.Month ?? "").FontSize(10).FontFamily(FontName);
                                table.Cell().Element(DataCell)
                                    .Text(row.Year ?? "").FontSize(10).FontFamily(FontName);
                                table.Cell().Element(DataCellRight)
                                    .Text($"{row.EEContribution:N2}").FontSize(10).FontFamily(FontName);
                                table.Cell().Element(DataCellRight)
                                    .Text($"{row.ERContribution:N2}").FontSize(10).FontFamily(FontName);
                                table.Cell().Element(DataCellRight)
                                    .Text($"{row.TotalContribution:N2}").FontSize(10).FontFamily(FontName);
                                table.Cell().Element(DataCellRight)
                                    .Text($"{row.HdmfLoan:N2}").FontSize(10).FontFamily(FontName);
                            }
                        });

                        col.Item().PaddingTop(12).Text("");

                        // ── Purpose paragraph ─────────────────────────────────
                        col.Item().Text(txt =>
                        {
                            txt.DefaultTextStyle(s => s.FontFamily(FontName).LineHeight(1.5f));
                            txt.Justify();
                            if (!string.IsNullOrWhiteSpace(data.Purpose))
                            {
                                txt.Span("This certification is issued upon the request of the above-mentioned for ");
                                txt.Span(data.Purpose).Bold();
                                txt.Span(".");
                            }
                            else
                            {
                                txt.Span("This certification is issued upon the request of the above-mentioned.");
                            }
                        });

                        col.Item().PaddingTop(12).Text("");

                        // ── Given this … ──────────────────────────────────────
                        col.Item()
                            .Text($"Given this {data.IssuedDate} at {data.IssuedCity ?? "Quezon City"}, Philippines.")
                            .FontFamily(FontName).FontSize(11);

                        col.Item().PaddingTop(15).Text("");

                        // ── Company name ──────────────────────────────────────
                        col.Item()
                            .Text(data.CompanyName ?? "Luxent Hotel")
                            .Bold().FontFamily(FontName).FontSize(11);

                        col.Item().PaddingTop(25).Text("");

                        // ── Signatory block ───────────────────────────────────
                        col.Item().Text(txt =>
                        {
                            txt.Line(data.SignatoryName ?? "EDITHA M. CARREON")
                               .Bold().FontFamily(FontName).FontSize(11);
                            string signatoryTitle = ToTitleCaseSmart(
                                (data.SignatoryTitle ?? "DIRECTOR OF HUMAN RESOURCES").ToLower());
                            txt.Line(signatoryTitle)
                               .FontFamily(FontName).FontSize(11);
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static readonly HashSet<string> _lowercaseWords =
            new(StringComparer.OrdinalIgnoreCase)
            { "of", "the", "and", "in", "at", "for", "to", "a", "an", "de", "del", "la" };

        private static string ToTitleCaseSmart(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var words = input.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (i == 0 || !_lowercaseWords.Contains(words[i]))
                    words[i] = System.Globalization.CultureInfo.CurrentCulture
                                      .TextInfo.ToTitleCase(words[i]);
                else
                    words[i] = words[i].ToLower();
            }
            return string.Join(" ", words);
        }

        // ── Ordinal suffix helper ────────────────────────────────────────────────
        public static string OrdinalSuffix(int day)
        {
            if (day is >= 11 and <= 13) return $"{day}th";
            return (day % 10) switch
            {
                1 => $"{day}st",
                2 => $"{day}nd",
                3 => $"{day}rd",
                _ => $"{day}th"
            };
        }
    }
}