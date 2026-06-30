using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.Payroll
{
    // ── Data model ────────────────────────────────────────────────────────────────
    public class LastPayPdfData
    {
        public string? EmployeeNo { get; set; }
        public string? EmployeeName { get; set; }
        public string? DateHired { get; set; }
        public string? DateResigned { get; set; }
        public string? EmpStatus { get; set; }

        public double AmountLastCutoff { get; set; }
        public double AmountAdjustment { get; set; }
        public double Amount13thMonth { get; set; }
        public double AmountTaxRefund { get; set; }
        public double AmountSL { get; set; }
        public double AmountVL { get; set; }

        public double LastPayAmount { get; set; }
        public string? LastPayStatus { get; set; }
        public bool IncludeLastCutoff { get; set; }
        public bool IncludeAdjustment { get; set; }
        public bool Include13thMonth { get; set; }
        public bool IncludeTax { get; set; }
        public bool IncludeSL { get; set; }
        public bool IncludeVL { get; set; }

        // Optional: enriched name fields from e_basicinfo join
        public string? PositionName { get; set; }
        public string? Department { get; set; }
    }

    // ── PDF Service ───────────────────────────────────────────────────────────────
    public class LastPayPdfService
    {
        public byte[] GenerateLastPayPdf(LastPayPdfData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Same page size as Payroll Register payslip – vertical half-letter
                    page.Size(new PageSize(4.25f, 5.5f, Unit.Inch));
                    page.Margin(12);
                    page.PageColor(Colors.White);

                    page.Content().ScaleToFit().Column(column =>
                    {
                        column.Spacing(2);

                        // ── Header: Logo (left) + FINAL PAY (right) ──────────────────
                        column.Item().Row(row =>
                        {
                            row.AutoItem()
                               .Height(22)
                               .Width(80)
                               .Image("wwwroot/Fillow/images/luxent_logo.png");

                            row.RelativeItem().AlignRight().Column(headerCol =>
                            {
                                headerCol.Item().AlignRight()
                                    .Text("FINAL PAY")
                                    .FontSize(10)
                                    .Bold();

                                headerCol.Item().AlignRight()
                                    .Text($"Date Resigned: {data.DateResigned ?? "N/A"}")
                                    .FontSize(5);

                                headerCol.Item().AlignRight()
                                    .Text($"Status: {data.LastPayStatus ?? "Open"}")
                                    .FontSize(5);
                            });
                        });

                        column.Item().PaddingVertical(2);

                        // ── Employee Info ─────────────────────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Padding(4)
                            .Column(empCol =>
                            {
                                empCol.Item().Row(row =>
                                {
                                    row.RelativeItem()
                                        .Text($"({data.EmployeeNo}) {data.EmployeeName}")
                                        .FontSize(6)
                                        .Bold();

                                    row.AutoItem()
                                        .Text(data.PositionName ?? "N/A")
                                        .FontSize(6)
                                        .Bold();
                                });

                                if (!string.IsNullOrWhiteSpace(data.Department))
                                {
                                    empCol.Item().PaddingTop(1)
                                        .Text($"Department: {data.Department}")
                                        .FontSize(5);
                                }

                                empCol.Item().PaddingTop(1).Row(row =>
                                {
                                    row.RelativeItem()
                                        .Text($"Date Hired: {data.DateHired ?? "N/A"}")
                                        .FontSize(5);

                                    row.RelativeItem().AlignRight()
                                        .Text($"Employment Status: {data.EmpStatus ?? "N/A"}")
                                        .FontSize(5);
                                });
                            });

                        column.Item().PaddingVertical(2);

                        // ── Final Pay Breakdown Table ─────────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(3f);   // Description
                                    cols.RelativeColumn(2f);   // Amount
                                });

                                // ── Header row ────────────────────────────────────────
                                static IContainer HeaderCell(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000")
                                     .Background("#d0d0d0")
                                     .Padding(3);

                                table.Cell().Element(HeaderCell).AlignCenter()
                                    .Text("DESCRIPTION").FontSize(6).Bold();
                                table.Cell().Element(HeaderCell).AlignCenter()
                                    .Text("AMOUNT").FontSize(6).Bold();

                                // ── Data rows ─────────────────────────────────────────
                                static IContainer DataCell(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000").Padding(3);

                                static IContainer DataCellShaded(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000")
                                     .Background("#f5f5f5").Padding(3);

                                var items = new List<(string Label, double Value, bool Show)>
                                {
                                    ("Last Cutoff Pay", data.AmountLastCutoff, data.IncludeLastCutoff),
                                    ("Adjustment",      data.AmountAdjustment, data.IncludeAdjustment),
                                    ("13th Month Pay",  data.Amount13thMonth,  data.Include13thMonth),
                                    ("Tax Refund",      data.AmountTaxRefund,  data.IncludeTax),
                                    ("SL Conversion",   data.AmountSL,         data.IncludeSL),
                                    ("VL Conversion",   data.AmountVL,         data.IncludeVL),
                                };

                                bool shade = false;
                                foreach (var (label, value, show) in items)
                                {
                                    if (!show) continue;

                                    var cellStyle = shade ? (Func<IContainer, IContainer>)DataCellShaded : DataCell;
                                    shade = !shade;

                                    table.Cell().Element(cellStyle)
                                        .Text(label).FontSize(5.5f);

                                    table.Cell().Element(cellStyle).AlignRight()
                                        .Text($"{value:N2}").FontSize(5.5f);
                                }
                            });

                        column.Item().PaddingVertical(2);

                        // ── Net / Final Pay highlight box ─────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Background("#d4edda")
                            .Padding(4)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .Text("FINAL PAY:")
                                    .FontSize(7).Bold();
                                row.AutoItem()
                                    .Text($"{data.LastPayAmount:N2}")
                                    .FontSize(7).Bold();
                            });

                        column.Item().PaddingVertical(2);

                        // ── Footer ────────────────────────────────────────────────────
                        column.Item().Row(row =>
                        {
                            row.RelativeItem()
                                .Text($"Printed: {DateTime.Now:MM/dd/yyyy hh:mm tt}")
                                .FontSize(4);
                            row.RelativeItem().AlignRight()
                                .Text("** System generated. No signature required. **")
                                .FontSize(4).Italic();
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}