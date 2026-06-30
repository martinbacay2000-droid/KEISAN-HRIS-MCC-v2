using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.TimeKeeping
{
    public class OffsetApplicationRequestPdfService
    {
        private readonly string _logoPath;

        public OffsetApplicationRequestPdfService(IWebHostEnvironment env)
        {
            _logoPath = Path.Combine(env.WebRootPath, "Fillow", "images", "your_logo_1.png");
        }

        // Main entry point: Creates and generates the complete PDF document as byte array
        public byte[] GenerateOffsetApplicationRequestPdf(OffsetApplicationRequestPdfData data)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Element(content => ComposeHeader(content, data));
                    page.Content().Element(content => ComposeContent(content, data));
                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf();
        }

        // Renders the PDF header with company logo, title and control number
        private void ComposeHeader(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.Column(column =>
            {
                // Top section with logo and title
                column.Item().Row(row =>
                {
                    // Company Logo (left)
                    row.ConstantItem(60).Column(col =>
                    {
                        if (File.Exists(_logoPath))
                        {
                            col.Item().Width(60).Height(40).Image(_logoPath, ImageScaling.FitArea);
                        }
                    });

                    // Title and Control Number (center and right)
                    row.RelativeItem().PaddingLeft(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text("CTO APPLICATION REQUEST")
                            .FontSize(16).Bold().FontColor("#1e40af");

                        col.Item().AlignCenter().PaddingTop(2).Text("Compensatory Time Off Application Form")
                            .FontSize(8).FontColor("#64748b");
                    });

                    // Control Number (right)
                    row.ConstantItem(80).AlignRight().Column(col =>
                    {
                        col.Item().Border(1).BorderColor("#e2e8f0")
                            .Background("#f8fafc").Padding(5).Column(innerCol =>
                            {
                                innerCol.Item().Text("Control No.").FontSize(7).FontColor("#64748b");
                                innerCol.Item().Text($"CTO-{data.Id:D5}").FontSize(11).Bold().FontColor("#1e40af");
                            });
                    });
                });

                // Divider line
                column.Item().PaddingTop(8).PaddingBottom(5)
                    .BorderBottom(2).BorderColor("#3b82f6");
            });
        }

        // Composes the main content sections
        private void ComposeContent(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.PaddingVertical(10).Column(column =>
            {
                column.Spacing(8);

                // Employee Information Section
                column.Item().Element(c => ComposeEmployeeSection(c, data));

                // CTO Type Section
                column.Item().Element(c => ComposeCTOTypeSection(c, data));

                // Overtime Period Details Section
                column.Item().Element(c => ComposeOvertimeDetailsSection(c, data));

                // Approved Hours Display Section
                column.Item().Element(c => ComposeApprovedHoursSection(c, data));

                // Reason Section
                column.Item().Element(c => ComposeReasonSection(c, data));

                // Status and Request Info
                column.Item().Element(c => ComposeStatusSection(c, data));

                // Signature Section
                column.Item().PaddingTop(15).Element(ComposeSignatureSection);
            });
        }

        // Employee Information Section with enhanced styling
        private void ComposeEmployeeSection(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("EMPLOYEE INFORMATION")
                        .FontSize(9).Bold().FontColor("#1e40af");
                });

                // Content
                column.Item().Border(1).BorderColor("#e2e8f0").Padding(10).Column(innerCol =>
                {
                    innerCol.Spacing(6);

                    // Employee Name
                    innerCol.Item().Row(row =>
                    {
                        row.ConstantItem(100).Text("Employee Name:")
                            .FontSize(8).SemiBold().FontColor("#475569");
                        row.RelativeItem().Text(data.FullName ?? "N/A")
                            .FontSize(9).FontColor("#0f172a");
                    });

                    // Employee Number
                    innerCol.Item().Row(row =>
                    {
                        row.ConstantItem(100).Text("Employee No:")
                            .FontSize(8).SemiBold().FontColor("#475569");
                        row.RelativeItem().Text(data.EmployeeNo ?? "N/A")
                            .FontSize(9).FontColor("#0f172a");
                    });
                });
            });
        }

        // CTO Type Section
        private void ComposeCTOTypeSection(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#fef3c7").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("CTO TYPE")
                        .FontSize(9).Bold().FontColor("#92400e");
                });

                // Content
                column.Item().Border(1).BorderColor("#fbbf24").Padding(10).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("CTO Category:")
                            .FontSize(8).SemiBold().FontColor("#78350f");
                        col.Item().PaddingTop(3).Border(1).BorderColor("#fbbf24")
                            .Background("#fffbeb").Padding(8)
                            .AlignCenter()
                            .Text(data.LeaveName ?? "N/A")
                            .FontSize(11).Bold().FontColor("#92400e");
                    });
                });
            });
        }

        // Overtime Period Details Section
        private void ComposeOvertimeDetailsSection(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("OVERTIME PERIOD RENDERED")
                        .FontSize(9).Bold().FontColor("#1e40af");
                });

                // Content in Grid Layout
                column.Item().Border(1).BorderColor("#e2e8f0").Padding(10).Column(innerCol =>
                {
                    innerCol.Spacing(8);

                    // Start and End Information
                    innerCol.Item().Row(row =>
                    {
                        // Overtime Start
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Overtime Start").FontSize(8).SemiBold()
                                .FontColor("#64748b");
                            col.Item().PaddingTop(3).Row(dateRow =>
                            {
                                dateRow.ConstantItem(60).Text("Date:")
                                    .FontSize(8).FontColor("#475569");
                                dateRow.RelativeItem().Text(data.DisplayDateIn ?? "N/A")
                                    .FontSize(9).FontColor("#0f172a");
                            });
                            col.Item().PaddingTop(2).Row(timeRow =>
                            {
                                timeRow.ConstantItem(60).Text("Time:")
                                    .FontSize(8).FontColor("#475569");
                                timeRow.RelativeItem().Text(data.DisplayTimeIn ?? "N/A")
                                    .FontSize(9).FontColor("#0f172a");
                            });
                        });

                        row.ConstantItem(20);

                        // Overtime End
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Overtime End").FontSize(8).SemiBold()
                                .FontColor("#64748b");
                            col.Item().PaddingTop(3).Row(dateRow =>
                            {
                                dateRow.ConstantItem(60).Text("Date:")
                                    .FontSize(8).FontColor("#475569");
                                dateRow.RelativeItem().Text(data.DisplayDateOut ?? "N/A")
                                    .FontSize(9).FontColor("#0f172a");
                            });
                            col.Item().PaddingTop(2).Row(timeRow =>
                            {
                                timeRow.ConstantItem(60).Text("Time:")
                                    .FontSize(8).FontColor("#475569");
                                timeRow.RelativeItem().Text(data.DisplayTimeOut ?? "N/A")
                                    .FontSize(9).FontColor("#0f172a");
                            });
                        });
                    });
                });
            });
        }

        // Approved Hours Display Section
        private void ComposeApprovedHoursSection(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#ecfdf5").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("APPROVED CTO HOURS")
                        .FontSize(9).Bold().FontColor("#059669");
                });

                // Content
                column.Item().Border(1).BorderColor("#a7f3d0").Padding(10).Row(row =>
                {
                    // Approved Hours Display
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignCenter().Text("Total Approved Overtime Hours")
                            .FontSize(8).SemiBold().FontColor("#064e3b");
                        col.Item().PaddingTop(5).AlignCenter()
                            .Border(1).BorderColor("#34d399")
                            .Background("#d1fae5").Padding(10)
                            .Text($"{data.ApprovedRenderOT:F2} hours")
                            .FontSize(20).Bold().FontColor("#059669");
                    });

                    row.ConstantItem(20);

                    // Converted Days Display
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignCenter().Text("Equivalent CTO Days")
                            .FontSize(8).SemiBold().FontColor("#064e3b");
                        col.Item().PaddingTop(5).AlignCenter()
                            .Border(1).BorderColor("#34d399")
                            .Background("#d1fae5").Padding(10)
                            .Text(FormatHoursToDays(data.ApprovedRenderOT))
                            .FontSize(20).Bold().FontColor("#059669");
                    });
                });

                // Information Note
                column.Item().PaddingTop(5).Border(1).BorderColor("#dbeafe")
                    .Background("#eff6ff").Padding(8)
                    .Row(row =>
                    {
                        row.RelativeItem().Text("Note: Approved CTO hours can be used for future time-off requests. The equivalent days are calculated based on an 8-hour workday. CTO credits are subject to company policy and expiration rules.")
                            .FontSize(7).LineHeight(1.3f).FontColor("#1e40af");
                    });
            });
        }

        // Reason Section with reason and remarks
        private void ComposeReasonSection(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("REASON FOR OVERTIME / CTO REQUEST")
                        .FontSize(9).Bold().FontColor("#1e40af");
                });

                // Content
                column.Item().Border(1).BorderColor("#e2e8f0").Padding(10).Column(innerCol =>
                {
                    innerCol.Spacing(6);

                    // Reason Box
                    innerCol.Item().Column(col =>
                    {
                        col.Item().Text("Reason:").FontSize(8).SemiBold()
                            .FontColor("#475569");
                        col.Item().PaddingTop(3).Border(1).BorderColor("#e2e8f0")
                            .Background("#f8fafc").Padding(8)
                            .MinHeight(35).MaxHeight(70)
                            .Text(data.OvertimeReason ?? "N/A")
                            .FontSize(8).LineHeight(1.3f).FontColor("#0f172a");
                    });

                    // Remarks Box (if exists)
                    if (!string.IsNullOrWhiteSpace(data.Remarks))
                    {
                        innerCol.Item().Column(col =>
                        {
                            col.Item().Text("Remarks:").FontSize(8).SemiBold()
                                .FontColor("#475569");
                            col.Item().PaddingTop(3).Border(1).BorderColor("#e2e8f0")
                                .Background("#fef3c7").Padding(8)
                                .MinHeight(30).MaxHeight(60)
                                .Text(data.Remarks)
                                .FontSize(8).LineHeight(1.3f).FontColor("#0f172a");
                        });
                    }
                });
            });
        }

        // Status and Request Information Section
        private void ComposeStatusSection(IContainer container, OffsetApplicationRequestPdfData data)
        {
            container.Border(1).BorderColor("#e2e8f0").Background("#f8fafc")
                .Padding(8).Row(row =>
                {
                    // Left side - Status
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Status:").FontSize(7).FontColor("#64748b");
                        col.Item().PaddingTop(2).Text(text =>
                        {
                            text.Span(data.StatusName ?? "Pending")
                                .FontSize(9).Bold()
                                .FontColor(GetStatusColor(data.StatusName));
                        });
                    });

                    // Middle - Requested By
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Requested By:").FontSize(7).FontColor("#64748b");
                        col.Item().PaddingTop(2).Text(data.RequestedByUser ?? "N/A")
                            .FontSize(8).FontColor("#0f172a");
                    });

                    // Right - Date Requested
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Date Requested:").FontSize(7).FontColor("#64748b");
                        col.Item().PaddingTop(2).Text(data.DateRequested ?? "N/A")
                            .FontSize(8).FontColor("#0f172a");
                    });
                });
        }

        // Enhanced Signature Section
        private void ComposeSignatureSection(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingBottom(6).Text("AUTHORIZATION")
                    .FontSize(9).Bold().FontColor("#1e40af");

                column.Item().Row(row =>
                {
                    // Employee Signature
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Height(35);
                        col.Item().BorderTop(1).BorderColor("#0f172a")
                            .PaddingTop(3).AlignCenter()
                            .Text("Employee Signature").FontSize(8).FontColor("#475569");
                        col.Item().PaddingTop(2).AlignCenter()
                            .Text("Date: _____________").FontSize(7).FontColor("#64748b");
                    });

                    row.ConstantItem(40);

                    // Immediate Supervisor Signature
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Height(35);
                        col.Item().BorderTop(1).BorderColor("#0f172a")
                            .PaddingTop(3).AlignCenter()
                            .Text("Immediate Supervisor").FontSize(8).FontColor("#475569");
                        col.Item().PaddingTop(2).AlignCenter()
                            .Text("Date: _____________").FontSize(7).FontColor("#64748b");
                    });

                    row.ConstantItem(40);

                    // HR Approval Signature
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Height(35);
                        col.Item().BorderTop(1).BorderColor("#0f172a")
                            .PaddingTop(3).AlignCenter()
                            .Text("HR Approval").FontSize(8).FontColor("#475569");
                        col.Item().PaddingTop(2).AlignCenter()
                            .Text("Date: _____________").FontSize(7).FontColor("#64748b");
                    });
                });
            });
        }

        // Footer with generation timestamp
        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(text =>
            {
                text.Span("Document generated on: ").FontSize(8).FontColor("#94a3b8");
                text.Span(DateTime.Now.ToString("MMMM dd, yyyy hh:mm tt"))
                    .FontSize(8).SemiBold().FontColor("#64748b");
            });
        }

        // Helper method to get status color
        private string GetStatusColor(string status)
        {
            return status switch
            {
                "Approved" => "#16a34a",
                "Declined" => "#dc2626",
                "Cancelled" => "#6b7280",
                "Processed" => "#0891b2",
                _ => "#eab308"
            };
        }

        // Helper method to format hours to days (assuming 8 hours per day)
        private string FormatHoursToDays(double hours)
        {
            if (hours <= 0)
                return "0 days";

            double days = hours / 8.0; // 8 hours per day

            if (days >= 1)
                return $"{days:F2} days";
            else
                return $"{days:F2} day";
        }
    }

    // Data model containing all Offset Application Request information needed for PDF generation
    public class OffsetApplicationRequestPdfData
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string LeaveCode { get; set; } = string.Empty;
        public string LeaveName { get; set; } = string.Empty;
        public string DisplayDateIn { get; set; } = string.Empty;
        public string DisplayDateOut { get; set; } = string.Empty;
        public string DisplayTimeIn { get; set; } = string.Empty;
        public string DisplayTimeOut { get; set; } = string.Empty;
        public double ApprovedRenderOT { get; set; }
        public string OvertimeReason { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public string RequestedByUser { get; set; } = string.Empty;
        public string DateRequested { get; set; } = string.Empty;
        public string? LastModified { get; set; }
    }
}