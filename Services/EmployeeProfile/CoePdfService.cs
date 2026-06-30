using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.EmployeeProfile
{
    public class CoeData
    {
        public string? EmployeeNo { get; set; }
        public string? FullName { get; set; }
        public string? GenderPrefix { get; set; }
        public string? Position { get; set; }
        public string? EmploymentStatus { get; set; }
        public string? DateHired { get; set; }
        public string? Branch { get; set; }

        public decimal? BasicMonthlyPay { get; set; }
        public decimal? BasicAllowanceAmount { get; set; }
        public string? BasicAllowanceName { get; set; }

        public bool IsActive { get; set; } = true;
        public string? DateTerminated { get; set; }

        public string? Purpose { get; set; }
        public string? IssuedDate { get; set; }
        public string? IssuedCity { get; set; }

        public string? LastName { get; set; }
        public string? SignatoryName { get; set; }
        public string? SignatoryTitle { get; set; }
        public string? CompanyName { get; set; }
        public decimal? MonthlyIncentiveAmount { get; set; }
        public bool WithCompensation { get; set; }
    }

    public class CoePdfService
    {
        // ── Shared constants ─────────────────────────────────────────────────────
        private const string FontName = "Arial";
        private const string CompanyFull = "BGISIS Development Corporation (Luxent Hotel)";

        public byte[] GenerateCoe(CoeData data)
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
                              .Text("51 Timog Avenue, South Triangle, Quezon City 1103 Philippines     Tel: (63)(2) 8863-7777     www.luxenthotel.com")
                              .FontFamily(FontName)
                              .FontSize(8)
                              .FontColor(Colors.Grey.Darken2);
                    });

                    page.Content().Column(col =>
                    {
                        // ── Logo — left-aligned, fixed width ─────────────────
                        col.Item().Width(100)
                            .Image("wwwroot/Fillow/images/luxent_logo.png");

                        col.Item().PaddingTop(30).Text("");

                        // ── Title ─────────────────────────────────────────────
                        col.Item().AlignCenter()
                            .Text("CERTIFICATE OF EMPLOYMENT")
                            .FontFamily(FontName).FontSize(13).Bold().Underline();

                        col.Item().PaddingTop(30).Text("");

                        // ── Body ──────────────────────────────────────────────
                        if (data.WithCompensation)
                            RenderWithCompensation(col, data);
                        else
                            RenderWithoutCompensation(col, data);

                        col.Item().PaddingTop(30).Text("");

                        // ── Date / Place ──────────────────────────────────────
                        col.Item().Text($"Given this {data.IssuedDate} at {data.IssuedCity ?? "Quezon City"}, Philippines.");

                        col.Item().PaddingTop(40).Text("");

                        // ── Company name ──────────────────────────────────────
                        col.Item().Text("Luxent Hotel").Bold();

                        col.Item().PaddingTop(40).Text("");

                        // ── Signatory ─────────────────────────────────────────
                        col.Item().Text(txt =>
                        {
                            txt.Line(data.SignatoryName ?? "EDITHA M. CARREON").Bold();
                            string signatoryTitle = ToTitleCaseSmart(
                                (data.SignatoryTitle ?? "DIRECTOR OF HUMAN RESOURCES").ToLower());
                            txt.Line(signatoryTitle).FontSize(12);
                        });

                        // ── Footer ────────────────────────────────────────────
                        int footerPadding = data.WithCompensation ? 20 : 80;
                        col.Item().PaddingTop(footerPadding).AlignRight()
                            .Text("Note: not valid without company seal")
                            .FontFamily(FontName).FontSize(8);
                    });
                });
            });

            return document.GeneratePdf();
        }

        // ─── Template: WITHOUT Compensation ─────────────────────────────────────

        private static void RenderWithoutCompensation(ColumnDescriptor col, CoeData data)
        {
            // Paragraph 1 — employment statement
            col.Item().Text(txt =>
            {
                txt.DefaultTextStyle(s => s
                    .FontFamily(FontName)
                    .LineHeight(1.5f));
                txt.Justify();

                if (data.IsActive)
                {
                    txt.Span("This is to certify that ");
                    txt.Span($"{data.GenderPrefix ?? "MR./MS."} {data.FullName}").Bold();
                    txt.Span(" is a bonafide employee of ");
                    txt.Span(CompanyFull).Bold();
                    txt.Span($" since {data.DateHired} up to present holding the position of ");
                    txt.Span($"{data.Position ?? "N/A"}").Bold();
                    txt.Span(".");
                }
                else
                {
                    txt.Span("This is to certify that ");
                    txt.Span($"{data.GenderPrefix ?? "MR./MS."} {data.FullName}").Bold();
                    txt.Span(" has been employed with ");
                    txt.Span(CompanyFull).Bold();
                    txt.Span(" holding the position of ");
                    txt.Span($"{data.Position ?? "N/A"}").Bold();
                    txt.Span($" from {data.DateHired} up to ");
                    txt.Span(data.DateTerminated ?? "N/A").Bold();
                    txt.Span(".");
                }
            });

            col.Item().PaddingTop(20).Text("");

            // Paragraph 2 — purpose
            col.Item().Text(txt =>
            {
                txt.DefaultTextStyle(s => s
                    .FontFamily(FontName)
                    .LineHeight(1.5f));
                txt.Justify();
                string surname = data.LastName ?? data.FullName?.Split(' ').LastOrDefault() ?? "";
                string genderSurname = $"{data.GenderPrefix ?? ""} {surname}".Trim();
                string hisHer = data.GenderPrefix?.ToUpper() == "MS." ? "her" : "his";
                txt.Span("This certification is issued upon the request of ");
                txt.Span(genderSurname).Bold();
                txt.Span($" for {hisHer} ");
                txt.Span(data.Purpose ?? "[purpose]").Bold();
                txt.Span(".");
            });
        }

        // ─── Template: WITH Compensation ────────────────────────────────────────

        private static void RenderWithCompensation(ColumnDescriptor col, CoeData data)
        {
            decimal basic = data.BasicMonthlyPay ?? 0m;
            decimal allowance = data.BasicAllowanceAmount ?? 0m;
            decimal incentive = data.MonthlyIncentiveAmount ?? 0m;
            decimal total = basic + allowance;          // incentive intentionally excluded from "total" per Image 1

            // Paragraph 1 — employment statement
            col.Item().Text(txt =>
            {
                txt.DefaultTextStyle(s => s.FontFamily(FontName).LineHeight(1.5f));
                txt.Justify();

                if (data.IsActive)
                {
                    txt.Span("This is to certify that ");
                    txt.Span($"{data.GenderPrefix ?? "MR./MS."} {data.FullName}").Bold();
                    txt.Span(" is a bonafide employee of ");
                    txt.Span(CompanyFull).Bold();
                    txt.Span($" since {data.DateHired} up to present holding the position of ");
                    txt.Span($"{data.Position ?? "N/A"}").Bold();
                    txt.Span(".");
                }
                else
                {
                    txt.Span("This is to certify that ");
                    txt.Span($"{data.GenderPrefix ?? "MR./MS."} {data.FullName}").Bold();
                    txt.Span(" has been employed with ");
                    txt.Span(CompanyFull).Bold();
                    txt.Span(" holding the position of ");
                    txt.Span($"{data.Position ?? "N/A"}").Bold();
                    txt.Span($" from {data.DateHired} up to ");
                    txt.Span(data.DateTerminated ?? "N/A").Bold();
                    txt.Span(".");
                }
            });

            col.Item().PaddingTop(20).Text("");

            // Paragraph 2 — compensation intro (wording switches when incentive is present)
            col.Item().Text(txt =>
            {
                txt.DefaultTextStyle(s => s.FontFamily(FontName));
                string pronoun = data.GenderPrefix?.ToUpper() == "MS." ? "she" : "he";
                txt.Span($"We further certify that {pronoun} is currently receiving the monthly compensation of");
            });

            col.Item().PaddingTop(4).Text(txt =>
            {
                txt.DefaultTextStyle(s => s.FontFamily(FontName));
                string totalInWords = ToPhilippinePesoWords(total);
                if (incentive > 0)
                    txt.Span($"{totalInWords} (Php {total:N2}) plus incentive with the following breakdown:");
                else
                    txt.Span($"{totalInWords} (Php {total:N2}) with the following breakdown:");
            });

            // Breakdown — indented rows
            col.Item().PaddingTop(16).PaddingLeft(80).Column(breakdown =>
            {
                // Monthly Basic
                breakdown.Item().Row(row =>
                {
                    row.ConstantItem(160).Text("Monthly Basic").FontFamily(FontName).FontSize(11);
                    row.ConstantItem(20).Text("-").FontFamily(FontName).FontSize(11);
                    row.ConstantItem(30).Text("Php").FontFamily(FontName).FontSize(11);
                    row.ConstantItem(80).Text($"{basic:N2}").FontFamily(FontName).FontSize(11);
                });

                if (allowance > 0)
                {
                    breakdown.Item().PaddingTop(6).Row(row =>
                    {
                        row.ConstantItem(160).Text(data.BasicAllowanceName ?? "Monthly Allowance").FontFamily(FontName).FontSize(11);
                        row.ConstantItem(20).Text("-").FontFamily(FontName).FontSize(11);
                        row.ConstantItem(30).Text("Php").FontFamily(FontName).FontSize(11);
                        row.ConstantItem(80).Text($"{allowance:N2}").FontFamily(FontName).FontSize(11);
                    });
                }

                if (incentive > 0)
                {
                    breakdown.Item().PaddingTop(6).Row(row =>
                    {
                        row.ConstantItem(160).Text("Monthly Incentive").FontFamily(FontName).FontSize(11);
                        row.ConstantItem(20).Text("-").FontFamily(FontName).FontSize(11);
                        row.ConstantItem(30).Text("Php").FontFamily(FontName).FontSize(11);
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span($"{incentive:N2}  ").FontFamily(FontName).FontSize(11);
                            txt.Span("(not fixed amount, average)").FontFamily(FontName).FontSize(9).Italic();
                        });
                    });
                }
            });

            col.Item().PaddingTop(20).Text("");

            // Paragraph 3 — purpose
            col.Item().Text(txt =>
            {
                txt.DefaultTextStyle(s => s.FontFamily(FontName).LineHeight(1.5f));
                txt.Justify();
                string surname = data.LastName ?? data.FullName?.Split(' ').LastOrDefault() ?? "";
                string genderSurname = $"{data.GenderPrefix ?? ""} {surname}".Trim();
                string hisHer = data.GenderPrefix?.ToUpper() == "MS." ? "her" : "his";
                txt.Span("This certification is issued upon the request of ");
                txt.Span(genderSurname).Bold();
                txt.Span($" for {hisHer} ");
                txt.Span(data.Purpose ?? "[purpose]").Bold();
                txt.Span(".");
            });
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

        private static string ToPhilippinePesoWords(decimal amount)
        {
            string[] ones = ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
                     "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
                     "Seventeen", "Eighteen", "Nineteen"];
            string[] tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

            long intPart = (long)Math.Floor(amount);

            string ConvertBelow1000(long n)
            {
                if (n == 0) return "";
                if (n < 20) return ones[n];
                if (n < 100) return tens[n / 10] + (n % 10 > 0 ? " " + ones[n % 10] : "");
                return ones[n / 100] + " Hundred" + (n % 100 > 0 ? " " + ConvertBelow1000(n % 100) : "");
            }

            if (intPart == 0) return "Zero Pesos";

            string result = "";
            if (intPart >= 1_000_000) { result += ConvertBelow1000(intPart / 1_000_000) + " Million "; intPart %= 1_000_000; }
            if (intPart >= 1_000) { result += ConvertBelow1000(intPart / 1_000) + " Thousand "; intPart %= 1_000; }
            if (intPart > 0) result += ConvertBelow1000(intPart);

            return result.Trim() + " Pesos";
        }
    }
}