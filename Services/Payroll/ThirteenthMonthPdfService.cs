using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using KEISAN_HRIS_v2.Models.Payroll;

namespace KEISAN_HRIS_v2.Services.Payroll
{
    public class ThirteenthMonthPdfService
    {
        public byte[] GenerateThirteenthMonthPdf(
            ThirteenthMonthPdfData header,
            List<ThirteenthMonthLineItem> lines)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.PageColor(Colors.White);

                    page.Content().ScaleToFit().Column(column =>
                    {
                        column.Spacing(4);

                        // ── Header ────────────────────────────────────────────
                        column.Item().Row(row =>
                        {
                            row.AutoItem()
                               .Height(28).Width(100)
                               .Image("wwwroot/Fillow/images/luxent_logo.png");

                            row.RelativeItem().AlignRight().Column(hCol =>
                            {
                                hCol.Item().AlignRight()
                                    .Text("13TH MONTH PAY DETAILS")
                                    .FontSize(12).Bold();

                                hCol.Item().AlignRight()
                                    .Text($"Date Resigned: {header.DateResigned ?? "N/A"}")
                                    .FontSize(6);
                            });
                        });

                        column.Item().PaddingVertical(2);

                        // ── Employee Info ─────────────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Padding(5)
                            .Column(empCol =>
                            {
                                empCol.Item().Row(row =>
                                {
                                    row.RelativeItem()
                                        .Text($"({header.EmployeeNo}) {header.FullName}")
                                        .FontSize(8).Bold();
                                    row.AutoItem()
                                        .Text(header.PositionName ?? "N/A")
                                        .FontSize(8).Bold();
                                });

                                empCol.Item().PaddingTop(2).Row(row =>
                                {
                                    row.RelativeItem()
                                        .Text($"Department: {header.Department ?? "N/A"}")
                                        .FontSize(6);
                                    row.RelativeItem().AlignRight()
                                        .Text($"Date Hired: {header.DateHired ?? "N/A"}")
                                        .FontSize(6);
                                });
                            });

                        column.Item().PaddingVertical(2);

                        // ── Detail Table ──────────────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(1.2f); // Year
                                    cols.RelativeColumn(1.5f); // Month
                                    cols.RelativeColumn(0.8f); // Cutoff
                                    cols.RelativeColumn(1.8f); // Basic Pay
                                    cols.RelativeColumn(1.5f); // Absent
                                    cols.RelativeColumn(1.5f); // Late
                                    cols.RelativeColumn(1.5f); // Undertime
                                    cols.RelativeColumn(1.8f); // Basic Allowance
                                    cols.RelativeColumn(2.2f); // Allow Tardy/UT/Abs
                                    cols.RelativeColumn(1.5f); // Adjustment
                                    cols.RelativeColumn(2.0f); // 13th Month Pay
                                });

                                // Header row
                                static IContainer HeaderCell(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000")
                                     .Background("#d0d0d0").Padding(3);

                                var headers = new[]
                                {
                                    "Year", "Month", "Cutoff", "Basic Pay",
                                    "Absent", "Late", "Undertime",
                                    "Basic Allowance", "Allow Tardy/UT/Abs",
                                    "Adjustment", "13th Month Pay"
                                };

                                foreach (var h in headers)
                                {
                                    table.Cell().Element(HeaderCell)
                                        .AlignCenter()
                                        .Text(h).FontSize(6).Bold();
                                }

                                // Data rows
                                static IContainer DataCell(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000").Padding(3);

                                static IContainer DataCellShaded(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000")
                                     .Background("#f5f5f5").Padding(3);

                                bool shade = false;
                                foreach (var line in lines)
                                {
                                    var cell = shade
                                        ? (Func<IContainer, IContainer>)DataCellShaded
                                        : DataCell;
                                    shade = !shade;

                                    table.Cell().Element(cell).AlignCenter()
                                        .Text(line.DateYear ?? "").FontSize(6);
                                    table.Cell().Element(cell)
                                        .Text(line.DateMonth ?? "").FontSize(6);
                                    table.Cell().Element(cell).AlignCenter()
                                        .Text(line.CutoffType ?? "").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"{line.BasicPay:N2}").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"({line.Absent:N2})").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"({line.Late:N2})").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"({line.Undertime:N2})").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"{line.BasicAllowance:N2}").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"({line.AllowanceTardyUndertimeAbsent:N2})").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"{line.Adjustment:N2}").FontSize(6);
                                    table.Cell().Element(cell).AlignRight()
                                        .Text($"{line.ThirteenthMonthPay:N2}").FontSize(6);
                                }
                            });

                        column.Item().PaddingVertical(2);

                        // ── Total highlight box ───────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Background("#d4edda").Padding(5)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .Text("13TH MONTH PAY:")
                                    .FontSize(8).Bold();
                                row.AutoItem()
                                    .Text($"{header.TotalAmount:N2}")
                                    .FontSize(8).Bold();
                            });

                        column.Item().PaddingVertical(2);

                        // ── Footer ────────────────────────────────────────────
                        column.Item().Row(row =>
                        {
                            row.RelativeItem()
                                .Text($"Printed: {DateTime.Now:MM/dd/yyyy hh:mm tt}")
                                .FontSize(5);
                            row.RelativeItem().AlignRight()
                                .Text("** System generated. No signature required. **")
                                .FontSize(5).Italic();
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}