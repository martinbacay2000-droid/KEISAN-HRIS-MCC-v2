using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.TimeKeeping
{
    public class OvertimeRequestPdfService
    {
        private readonly string _logoPath;

        public OvertimeRequestPdfService(IWebHostEnvironment env)
        {
            _logoPath = Path.Combine(env.WebRootPath, "Fillow", "images", "your_logo_1.png");
        }

        // Main entry point: Creates and generates the complete PDF document as byte array
        public byte[] GenerateOvertimeRequestPdf(OvertimeRequestPdfData data)
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
        private void ComposeHeader(IContainer container, OvertimeRequestPdfData data)
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
                        col.Item().AlignCenter().Text("OVERTIME REQUEST FORM")
                            .FontSize(16).Bold().FontColor("#1e40af");

                        col.Item().AlignCenter().PaddingTop(2).Text("Employee Overtime Authorization")
                            .FontSize(8).FontColor("#64748b");
                    });

                    // Control Number (right)
                    row.ConstantItem(80).AlignRight().Column(col =>
                    {
                        col.Item().Border(1).BorderColor("#e2e8f0")
                            .Background("#f8fafc").Padding(5).Column(innerCol =>
                            {
                                innerCol.Item().Text("Control No.").FontSize(7).FontColor("#64748b");
                                innerCol.Item().Text($"OT-{data.Id:D5}").FontSize(11).Bold().FontColor("#1e40af");
                            });
                    });
                });

                // Divider line
                column.Item().PaddingTop(8).PaddingBottom(5)
                    .BorderBottom(2).BorderColor("#3b82f6");
            });
        }

        // Composes the main content sections
        private void ComposeContent(IContainer container, OvertimeRequestPdfData data)
        {
            container.PaddingVertical(10).Column(column =>
            {
                column.Spacing(8);

                // Employee Information Section
                column.Item().Element(c => ComposeEmployeeSection(c, data));

                // Overtime Period Details Section
                column.Item().Element(c => ComposeOvertimeDetailsSection(c, data));

                // Overtime Duration Calculation
                column.Item().Element(c => ComposeOvertimeDurationSection(c, data));

                // Reason Section
                column.Item().Element(c => ComposeReasonSection(c, data));

                // Status and Request Info
                column.Item().Element(c => ComposeStatusSection(c, data));

                // Signature Section
                column.Item().PaddingTop(15).Element(ComposeSignatureSection);
            });
        }

        // Employee Information Section with enhanced styling
        private void ComposeEmployeeSection(IContainer container, OvertimeRequestPdfData data)
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
                        row.RelativeItem().Text(data.EmployeeName ?? "N/A")
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

        // Overtime Period Details Section
        private void ComposeOvertimeDetailsSection(IContainer container, OvertimeRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("OVERTIME PERIOD")
                        .FontSize(9).Bold().FontColor("#1e40af");
                });

                // Content in Grid Layout
                column.Item().Border(1).BorderColor("#e2e8f0").Padding(10).Column(innerCol =>
                {
                    innerCol.Spacing(8);

                    // Start Information
                    innerCol.Item().Row(row =>
                    {
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

                        // End Information
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

        // Overtime Duration Calculation Section
        private void ComposeOvertimeDurationSection(IContainer container, OvertimeRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#ecfdf5").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("OVERTIME DURATION")
                        .FontSize(9).Bold().FontColor("#059669");
                });

                // Content
                column.Item().Border(1).BorderColor("#a7f3d0").Padding(10).Row(row =>
                {
                    // Total Hours Display
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().AlignCenter().Text("Total Overtime Hours")
                            .FontSize(8).SemiBold().FontColor("#064e3b");
                        col.Item().PaddingTop(5).AlignCenter()
                            .Border(1).BorderColor("#34d399")
                            .Background("#d1fae5").Padding(10)
                            .Text(CalculateOvertimeHours(data))
                            .FontSize(20).Bold().FontColor("#059669");
                    });

                    row.ConstantItem(20);

                    // Additional Info
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Note:").FontSize(8).SemiBold()
                            .FontColor("#064e3b");
                        col.Item().PaddingTop(3).Text("Actual overtime hours will be verified by the Time and Attendance system and may differ from the requested duration.")
                            .FontSize(7).LineHeight(1.3f).FontColor("#065f46");
                    });
                });
            });
        }

        // Reason Section with reason and remarks
        private void ComposeReasonSection(IContainer container, OvertimeRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("REASON FOR OVERTIME")
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
        private void ComposeStatusSection(IContainer container, OvertimeRequestPdfData data)
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

                    // Department Head Signature
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Height(35);
                        col.Item().BorderTop(1).BorderColor("#0f172a")
                            .PaddingTop(3).AlignCenter()
                            .Text("Department Head").FontSize(8).FontColor("#475569");
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

        // Helper method to calculate overtime hours
        private string CalculateOvertimeHours(OvertimeRequestPdfData data)
        {
            try
            {
                // Parse dates and times
                if (DateTime.TryParse(data.DisplayDateIn + " " + data.DisplayTimeIn, out DateTime startDateTime) &&
                    DateTime.TryParse(data.DisplayDateOut + " " + data.DisplayTimeOut, out DateTime endDateTime))
                {
                    var duration = endDateTime - startDateTime;
                    var totalHours = duration.TotalHours;

                    if (totalHours < 0)
                        return "Invalid Duration";

                    // Format as hours and minutes
                    int hours = (int)totalHours;
                    int minutes = (int)((totalHours - hours) * 60);

                    if (minutes > 0)
                        return $"{hours}h {minutes}m";
                    else
                        return $"{hours}h";
                }
            }
            catch
            {
                // If parsing fails, return placeholder
            }

            return "To be calculated";
        }
    }

    // Data model containing all Overtime Request information needed for PDF generation
    public class OvertimeRequestPdfData
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string DisplayDateIn { get; set; } = string.Empty;
        public string DisplayDateOut { get; set; } = string.Empty;
        public string DisplayTimeIn { get; set; } = string.Empty;
        public string DisplayTimeOut { get; set; } = string.Empty;
        public string OvertimeReason { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public string RequestedByUser { get; set; } = string.Empty;
        public string DateRequested { get; set; } = string.Empty;
        public string? LastModified { get; set; }
    }
}