using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.TimeKeeping
{
    public class LeaveRequestPdfService
    {
        private readonly string _logoPath;

        public LeaveRequestPdfService(IWebHostEnvironment env)
        {
            _logoPath = Path.Combine(env.WebRootPath, "Fillow", "images", "your_logo_1.png");
        }

        // Main entry point: Creates and generates the complete PDF document as byte array
        public byte[] GenerateLeaveRequestPdf(LeaveRequestPdfData data)
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
        private void ComposeHeader(IContainer container, LeaveRequestPdfData data)
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
                        col.Item().AlignCenter().Text("LEAVE REQUEST FORM")
                            .FontSize(16).Bold().FontColor("#1e40af");

                        col.Item().AlignCenter().PaddingTop(2).Text("Employee Leave Application")
                            .FontSize(8).FontColor("#64748b");
                    });

                    // Control Number (right)
                    row.ConstantItem(80).AlignRight().Column(col =>
                    {
                        col.Item().Border(1).BorderColor("#e2e8f0")
                            .Background("#f8fafc").Padding(5).Column(innerCol =>
                            {
                                innerCol.Item().Text("Control No.").FontSize(7).FontColor("#64748b");
                                innerCol.Item().Text($"LV-{data.Id:D5}").FontSize(11).Bold().FontColor("#1e40af");
                            });
                    });
                });

                // Divider line
                column.Item().PaddingTop(8).PaddingBottom(5)
                    .BorderBottom(2).BorderColor("#3b82f6");
            });
        }

        // Composes the main content sections
        private void ComposeContent(IContainer container, LeaveRequestPdfData data)
        {
            container.PaddingVertical(10).Column(column =>
            {
                column.Spacing(8);

                // Employee Information Section
                column.Item().Element(c => ComposeEmployeeSection(c, data));

                // Leave Details Section
                column.Item().Element(c => ComposeLeaveDetailsSection(c, data));

                // Leave Type and Credits Section
                column.Item().Element(c => ComposeLeaveTypeSection(c, data));

                // Reason Section
                column.Item().Element(c => ComposeReasonSection(c, data));

                // Status and Request Info
                column.Item().Element(c => ComposeStatusSection(c, data));

                // Signature Section
                column.Item().PaddingTop(15).Element(ComposeSignatureSection);
            });
        }

        // Employee Information Section with enhanced styling
        private void ComposeEmployeeSection(IContainer container, LeaveRequestPdfData data)
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

        // Leave Details Section
        private void ComposeLeaveDetailsSection(IContainer container, LeaveRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("LEAVE PERIOD")
                        .FontSize(9).Bold().FontColor("#1e40af");
                });

                // Content in Grid Layout
                column.Item().Border(1).BorderColor("#e2e8f0").Padding(10).Column(innerCol =>
                {
                    innerCol.Spacing(8);

                    // Date Range
                    innerCol.Item().Row(row =>
                    {
                        // Leave Date From
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Leave Date From").FontSize(8).SemiBold()
                                .FontColor("#64748b");
                            col.Item().PaddingTop(3).Border(1).BorderColor("#e2e8f0")
                                .Background("#f8fafc").Padding(8)
                                .AlignCenter()
                                .Text(data.DisplayDateFrom ?? "N/A")
                                .FontSize(11).Bold().FontColor("#0f172a");
                        });

                        row.ConstantItem(20);

                        // Leave Date To
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Leave Date To").FontSize(8).SemiBold()
                                .FontColor("#64748b");
                            col.Item().PaddingTop(3).Border(1).BorderColor("#e2e8f0")
                                .Background("#f8fafc").Padding(8)
                                .AlignCenter()
                                .Text(data.DisplayDateTo ?? "N/A")
                                .FontSize(11).Bold().FontColor("#0f172a");
                        });
                    });

                    // Leave Type and Count
                    innerCol.Item().PaddingTop(4).Row(row =>
                    {
                        // Leave Type (Whole/Half Day)
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Leave Type").FontSize(8).SemiBold()
                                .FontColor("#64748b");
                            col.Item().PaddingTop(3).Border(1).BorderColor("#e2e8f0")
                                .Background("#f1f5f9").Padding(8)
                                .AlignCenter()
                                .Text(GetLeaveTypeDisplay(data.LeaveType))
                                .FontSize(10).SemiBold().FontColor("#1e40af");
                        });

                        row.ConstantItem(20);

                        // Number of Days
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Number of Days").FontSize(8).SemiBold()
                                .FontColor("#64748b");
                            col.Item().PaddingTop(3).Border(1).BorderColor("#e2e8f0")
                                .Background("#ecfdf5").Padding(8)
                                .AlignCenter()
                                .Text($"{data.LeaveCountDays:0.0}")
                                .FontSize(14).Bold().FontColor("#059669");
                        });
                    });
                });
            });
        }

        // Leave Type and Credits Section
        private void ComposeLeaveTypeSection(IContainer container, LeaveRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("LEAVE TYPE & CREDITS")
                        .FontSize(9).Bold().FontColor("#1e40af");
                });

                // Content
                column.Item().Border(1).BorderColor("#e2e8f0").Padding(10).Column(innerCol =>
                {
                    innerCol.Spacing(6);

                    // Leave Type Name
                    innerCol.Item().Row(row =>
                    {
                        row.ConstantItem(120).Text("Leave Type:")
                            .FontSize(8).SemiBold().FontColor("#475569");
                        row.RelativeItem().Text(data.LeaveName ?? "N/A")
                            .FontSize(9).FontColor("#0f172a");
                    });

                    // Credit Deduction Status
                    if (data.CreditDeductionOnly)
                    {
                        innerCol.Item().Border(1).BorderColor("#fbbf24")
                            .Background("#fef3c7").Padding(8)
                            .Row(row =>
                            {
                                row.RelativeItem().Text("⚠ Credit Deduction Only - No Actual Leave Taken")
                                    .FontSize(8).SemiBold().FontColor("#92400e");
                            });
                    }
                });
            });
        }

        // Reason Section with reason and remarks
        private void ComposeReasonSection(IContainer container, LeaveRequestPdfData data)
        {
            container.Column(column =>
            {
                // Section Header
                column.Item().Background("#eff6ff").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("REASON FOR LEAVE")
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
                            .Text(data.LeaveReason ?? "N/A")
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
        private void ComposeStatusSection(IContainer container, LeaveRequestPdfData data)
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

        // Helper method to display leave type in readable format
        private string GetLeaveTypeDisplay(string leaveType)
        {
            return leaveType?.ToLower() switch
            {
                "whole" => "Whole Day",
                "firsthalf" => "First Half",
                "secondhalf" => "Second Half",
                _ => leaveType ?? "Whole Day"
            };
        }
    }

    // Data model containing all Leave Request information needed for PDF generation
    public class LeaveRequestPdfData
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string LeaveCode { get; set; } = string.Empty;
        public string LeaveName { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public string DisplayDateFrom { get; set; } = string.Empty;
        public string DisplayDateTo { get; set; } = string.Empty;
        public decimal LeaveCountDays { get; set; }
        public decimal LeaveCountHours { get; set; }
        public string LeaveReason { get; set; } = string.Empty;
        public bool CreditDeductionOnly { get; set; }
        public string Remarks { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public string RequestedByUser { get; set; } = string.Empty;
        public string DateRequested { get; set; } = string.Empty;
        public string? LastModified { get; set; }
    }
}