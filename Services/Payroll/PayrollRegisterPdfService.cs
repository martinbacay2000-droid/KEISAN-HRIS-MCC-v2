using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KEISAN_HRIS_v2.Services.Payroll
{
    public class PayrollRegisterPdfService
    {
        public byte[] GeneratePayrollRegisterPdf(PayrollRegisterPdfData data)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Vertical half-letter: 4.25" wide × 5.5" tall (portrait)
                    page.Size(new PageSize(4.25f, 5.5f, Unit.Inch));
                    page.Margin(12); // tight margin to maximize usable area
                    page.PageColor(Colors.White);

                    // .ScaleToFit() proportionally shrinks content if rows overflow the page
                    page.Content().ScaleToFit().Column(column =>
                    {
                        column.Spacing(2);

                        // ── Header: Logo (left) + PAYSLIP info (right) ──────────────────
                        column.Item().Row(row =>
                        {
                            row.AutoItem()
                               .Height(22)
                               .Width(80)
                               .Image("wwwroot/Fillow/images/your_logo_1.png");

                            row.RelativeItem().AlignRight().Column(headerCol =>
                            {
                                headerCol.Item().AlignRight()
                                    .Text("PAYSLIP")
                                    .FontSize(10)
                                    .Bold();

                                headerCol.Item().AlignRight()
                                    .Text($"Pay Period: {data.PayPeriod ?? "[Period Here]"}")
                                    .FontSize(5);

                                headerCol.Item().AlignRight()
                                    .Text($"Department: {data.Department ?? "[Department Here]"}")
                                    .FontSize(5);
                            });
                        });

                        column.Item().PaddingVertical(2);

                        // ── Employee Info ─────────────────────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Padding(4)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .Text($"({data.EmployeeNo}) {data.FullName}")
                                    .FontSize(6)
                                    .Bold();

                                //row.AutoItem()
                                //    .Text($"Rate: {data.DailyRate:N2}")
                                //    .FontSize(6)
                                //    .Bold();
                                row.AutoItem()
                                    .Text($"{data.PositionName ?? "N/A"}")
                                    .FontSize(6)
                                    .Bold();
                            });

                        column.Item().PaddingVertical(1);

                        // ── Government IDs ────────────────────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Padding(4)
                            .Column(govCol =>
                            {
                                govCol.Item().Row(row =>
                                {
                                    row.RelativeItem()
                                        .Text($"SSS: {data.SSSNumber ?? "N/A"}")
                                        .FontSize(5);
                                    row.RelativeItem().AlignCenter()
                                        .Text($"PhilHealth: {data.PhilHealthNumber ?? "N/A"}")
                                        .FontSize(5);
                                    row.RelativeItem().AlignRight()
                                        .Text($"Acct: {data.AccountNo ?? "N/A"}")
                                        .FontSize(5);
                                });

                                govCol.Item().PaddingTop(1).Row(row =>
                                {
                                    row.RelativeItem()
                                        .Text($"Pag-IBIG: {data.PagIbigNumber ?? "N/A"}")
                                        .FontSize(5);
                                    row.RelativeItem().AlignCenter()
                                        .Text($"TIN: {data.TINNumber ?? "N/A"}")
                                        .FontSize(5);
                                    row.RelativeItem().Text("").FontSize(5);
                                });
                            });

                        column.Item().PaddingVertical(2);

                        // ── Earnings / Deductions Table ───────────────────────────────
                        var earnings = GetEarningsList(data);
                        var deductions = GetDeductionsList(data);
                        var maxRows = Math.Max(earnings.Count, deductions.Count);

                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(2.5f); // Earnings label
                                    cols.RelativeColumn(1.5f); // Earnings amount
                                    cols.RelativeColumn(2.5f); // Deductions label
                                    cols.RelativeColumn(1.5f); // Deductions amount
                                });

                                // Header row
                                static IContainer HeaderCell(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000")
                                     .Background("#d0d0d0")
                                     .Padding(2);

                                table.Cell().Element(HeaderCell).AlignCenter()
                                    .Text("EARNINGS").FontSize(5.5f).Bold();
                                table.Cell().Element(HeaderCell).AlignCenter()
                                    .Text("AMOUNT").FontSize(5.5f).Bold();
                                table.Cell().Element(HeaderCell).AlignCenter()
                                    .Text("DEDUCTIONS").FontSize(5.5f).Bold();
                                table.Cell().Element(HeaderCell).AlignCenter()
                                    .Text("AMOUNT").FontSize(5.5f).Bold();

                                // Data rows
                                static IContainer DataCell(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000").Padding(2);

                                for (int i = 0; i < maxRows; i++)
                                {
                                    if (i < earnings.Count)
                                    {
                                        table.Cell().Element(DataCell)
                                            .Text(earnings[i].Description).FontSize(5);
                                        table.Cell().Element(DataCell).AlignRight()
                                            .Text(earnings[i].Amount).FontSize(5);
                                    }
                                    else
                                    {
                                        table.Cell().Element(DataCell).Text("");
                                        table.Cell().Element(DataCell).Text("");
                                    }

                                    if (i < deductions.Count)
                                    {
                                        table.Cell().Element(DataCell)
                                            .Text(deductions[i].Description).FontSize(5);
                                        table.Cell().Element(DataCell).AlignRight()
                                            .Text(deductions[i].Amount).FontSize(5);
                                    }
                                    else
                                    {
                                        table.Cell().Element(DataCell).Text("");
                                        table.Cell().Element(DataCell).Text("");
                                    }
                                }

                                // Totals row
                                static IContainer TotalCell(IContainer c) =>
                                    c.Border(0.5f).BorderColor("#000000")
                                     .Background("#e8e8e8")
                                     .Padding(2);

                                table.Cell().Element(TotalCell)
                                    .Text("Gross Earnings:").FontSize(6).Bold();
                                table.Cell().Element(TotalCell).AlignRight()
                                    .Text($"{data.GrossIncome:N2}").FontSize(6).Bold();
                                table.Cell().Element(TotalCell)
                                    .Text("Total Deductions:").FontSize(6).Bold();
                                table.Cell().Element(TotalCell).AlignRight()
                                    .Text($"- {data.TotalDeduction:N2}").FontSize(6).Bold();
                            });

                        column.Item().PaddingVertical(2);

                        // ── Net Pay ───────────────────────────────────────────────────
                        column.Item()
                            .Border(0.5f).BorderColor("#000000")
                            .Background("#d4edda")
                            .Padding(4)
                            .Row(row =>
                            {
                                row.RelativeItem()
                                    .Text("NET PAY:")
                                    .FontSize(7).Bold();
                                row.AutoItem()
                                    .Text($"{data.TotalNetPay:N2}")
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

        // ── Earnings list ─────────────────────────────────────────────────────────────
        private List<PayslipLineItem> GetEarningsList(PayrollRegisterPdfData data)
        {
            var earnings = new List<PayslipLineItem>();

            if (data.BasicPaySemi > 0)
                earnings.Add(new PayslipLineItem("Basic Pay:", $"{data.BasicPaySemi:N2}"));

            //if (data.nonBasicPay > 0)
            //    earnings.Add(new PayslipLineItem("Total OT Pay:", $"{data.nonBasicPay:N2}"));

            if (data.amountOT > 0)
                earnings.Add(new PayslipLineItem("Regular OT:", $"{data.amountOT:N2}"));
            if (data.amountNSD > 0)
                earnings.Add(new PayslipLineItem("Regular ND:", $"{data.amountNSD:N2}"));
            if (data.amountNSDOT > 0)
                earnings.Add(new PayslipLineItem("Regular NDOT:", $"{data.amountNSDOT:N2}"));

            if (data.amountREST > 0)
                earnings.Add(new PayslipLineItem("RD:", $"{data.amountREST:N2}"));
            if (data.amountRESTOT > 0)
                earnings.Add(new PayslipLineItem("RD OT:", $"{data.amountRESTOT:N2}"));
            if (data.amountNSDREST > 0)
                earnings.Add(new PayslipLineItem("RD ND:", $"{data.amountNSDREST:N2}"));
            if (data.amountNSDRESTOT > 0)
                earnings.Add(new PayslipLineItem("RD ND OT:", $"{data.amountNSDRESTOT:N2}"));

            if (data.amountRESTS > 0)
                earnings.Add(new PayslipLineItem("RD Special Hol:", $"{data.amountRESTS:N2}"));
            if (data.amountRESTOTS > 0)
                earnings.Add(new PayslipLineItem("RD Special OT:", $"{data.amountRESTOTS:N2}"));
            if (data.amountNSDRESTS > 0)
                earnings.Add(new PayslipLineItem("RD Special ND:", $"{data.amountNSDRESTS:N2}"));
            if (data.amountNSDRESTOTS > 0)
                earnings.Add(new PayslipLineItem("RD Special ND OT:", $"{data.amountNSDRESTOTS:N2}"));


            if (data.amountRESTL > 0)
                earnings.Add(new PayslipLineItem("RD Legal:", $"{data.amountRESTL:N2}"));
            if (data.amountRESTOTL > 0)
                earnings.Add(new PayslipLineItem("RD Legal OT:", $"{data.amountRESTOTL:N2}"));
            if (data.amountNSDRESTL > 0)
                earnings.Add(new PayslipLineItem("RD Legal ND:", $"{data.amountNSDRESTL:N2}"));
            if (data.amountNSDRESTOTL > 0)
                earnings.Add(new PayslipLineItem("RD Legal ND OT:", $"{data.amountNSDRESTOTL:N2}"));

            if (data.amountL > 0)
                earnings.Add(new PayslipLineItem("Legal Holiday:", $"{data.amountL:N2}"));
            if (data.amountOTL > 0)
                earnings.Add(new PayslipLineItem("Legal Holiday OT:", $"{data.amountOTL:N2}"));
            if (data.amountNSDL > 0)
                earnings.Add(new PayslipLineItem("Legal Holiday ND:", $"{data.amountNSDL:N2}"));
            if (data.amountNSDOTL > 0)
                earnings.Add(new PayslipLineItem("Legal Holiday ND OT:", $"{data.amountNSDOTL:N2}"));

            if (data.amountS > 0)
                earnings.Add(new PayslipLineItem("Special Holiday:", $"{data.amountS:N2}"));
            if (data.amountOTS > 0)
                earnings.Add(new PayslipLineItem("Special Holiday OT:", $"{data.amountOTS:N2}"));
            if (data.amountNSDS > 0)
                earnings.Add(new PayslipLineItem("Special Holiday ND:", $"{data.amountNSDS:N2}"));
            if (data.amountNSDOTS > 0)
                earnings.Add(new PayslipLineItem("Special Holiday ND OT:", $"{data.amountNSDOTS:N2}"));

            if (data.TotalAllowance > 0)
                earnings.Add(new PayslipLineItem("Total Allowance:", $"{data.TotalAllowance:N2}"));

            if (data.OtherIncome > 0)
                earnings.Add(new PayslipLineItem("Adj. Taxable:", $"{data.OtherIncome:N2}"));

            if (data.OtherEmployeePayable > 0)
                earnings.Add(new PayslipLineItem("Adj. Non-Tax:", $"{data.OtherEmployeePayable:N2}"));

            return earnings;
        }

        // ── Deductions list ───────────────────────────────────────────────────────────
        private List<PayslipLineItem> GetDeductionsList(PayrollRegisterPdfData data)
        {
            var deductions = new List<PayslipLineItem>();

            if (data.RenderLate > 0 || data.AmountLate > 0)
                deductions.Add(new PayslipLineItem($"Late ({data.RenderLate:N0} mins):", $"({data.AmountLate:N2})"));

            if (data.RenderUndertime > 0 || data.AmountUndertime > 0)
                deductions.Add(new PayslipLineItem($"Undertime ({data.RenderUndertime:N0} mins):", $"({data.AmountUndertime:N2})"));

            if (data.AbsentCount > 0 || data.AbsentAmount > 0)
                deductions.Add(new PayslipLineItem($"Absent ({data.AbsentCount:N1} days):", $"({data.AbsentAmount:N2})"));

            if (data.otherEmployeeReceivable != 0)
                deductions.Add(new PayslipLineItem($"Other Deduction:", $"({data.otherEmployeeReceivable:N2})"));

            if (data.allowanceDeduction > 0 || data.allowanceDeduction > 0)
                deductions.Add(new PayslipLineItem($"Allowance Deduction:", $"({data.allowanceDeduction:N2})"));

            if (data.DeductionSSSemployee > 0)
                deductions.Add(new PayslipLineItem("SSS:", $"({data.DeductionSSSemployee:N2})"));

            if (data.DeductionWISPemployee > 0)
                deductions.Add(new PayslipLineItem("SSS WISP:", $"({data.DeductionWISPemployee:N2})"));

            if (data.DeductionPHIemployee > 0)

                if (data.DeductionPHIemployee > 0)
                    deductions.Add(new PayslipLineItem("PhilHealth:", $"({data.DeductionPHIemployee:N2})"));

            if (data.DeductionPIFemployee > 0)
                deductions.Add(new PayslipLineItem("Pag-IBIG:", $"({data.DeductionPIFemployee:N2})"));

            if (data.WithHeldTax > 0)
                deductions.Add(new PayslipLineItem("Withheld Tax:", $"({data.WithHeldTax:N2})"));

            // Intentional: condition uses Cashadvance, label shows Employee Ledger
            if (data.employeeLedger > 0)
                deductions.Add(new PayslipLineItem("Employee Ledger:", $"({data.employeeLedger:N2})"));

            if (data.sssLoan > 0)
                deductions.Add(new PayslipLineItem("SSS Salary Loan:", $"({data.sssLoan:N2})"));

            if (data.sssCalamity > 0)
                deductions.Add(new PayslipLineItem("SSS Calamity:", $"({data.sssCalamity:N2})"));

            if (data.hdmfLoan > 0)
                deductions.Add(new PayslipLineItem("HDMF Salary:", $"({data.hdmfLoan:N2})"));

            if (data.hdmfCalamity > 0)
                deductions.Add(new PayslipLineItem("HDMF Calamity:", $"({data.hdmfCalamity:N2})"));

            if (data.csbLoan > 0)
                deductions.Add(new PayslipLineItem("China Bank Savings:", $"({data.csbLoan:N2})"));

            if (data.hmoLoan > 0)
                deductions.Add(new PayslipLineItem("HMO Dependent:", $"({data.hmoLoan:N2})"));

            if (data.otherLoan1 > 0)
                deductions.Add(new PayslipLineItem("Other Loan 1:", $"({data.otherLoan1:N2})"));

            if (data.otherLoan2 > 0)
                deductions.Add(new PayslipLineItem("Other Loan 2:", $"({data.otherLoan2:N2})"));

            if (data.otherLoan3 > 0)
                deductions.Add(new PayslipLineItem("Other Loan 3:", $"({data.otherLoan3:N2})"));

            if (data.otherLoan4 > 0)
                deductions.Add(new PayslipLineItem("Other Loan 4:", $"({data.otherLoan4:N2})"));

            return deductions;
        }
    }

    // ── Helper classes / models  ──────────────────────────────────────────

    public class PayslipLineItem
    {
        public string Description { get; set; }
        public string Amount { get; set; }

        public PayslipLineItem(string description, string amount)
        {
            Description = description;
            Amount = amount;
        }
    }

    public class PayrollRegisterPdfData
    {
        public string? EmployeeNo { get; set; }
        public string? FullName { get; set; }
        public double DailyRate { get; set; }
        public string? BasicPayHours { get; set; }
        public double BasicPaySemi { get; set; }
        public double nonBasicPay { get; set; }

        #region Loans / Other Deductions
        public double? amountLoan { get; set; }
        public double? sssLoan { get; set; }
        public double? hdmfLoan { get; set; }
        public double? employeeLedger { get; set; }
        public double? acdiLoan { get; set; }
        public double? prulife { get; set; }
        public double? telephone { get; set; }
        public double? sssCalamity { get; set; }
        public double? hdmfCalamity { get; set; }
        public double? csbLoan { get; set; }
        public double? sbLoan { get; set; }
        public double? hmoLoan { get; set; }
        public double? otherLoan1 { get; set; }
        public double? otherLoan2 { get; set; }
        public double? otherLoan3 { get; set; }
        public double? otherLoan4 { get; set; }
        public double? otherEmployeeReceivable { get; set; }
        public double? allowanceDeduction { get; set; }
        //public double? otherEmployeePayable { get; set; }
        public double? leaveCount { get; set; }
        public double? leaveAmount { get; set; }
        #endregion

        // Deduction amounts
        public double AmountLate { get; set; }
        public double AmountUndertime { get; set; }
        public double AbsentAmount { get; set; }

        // Attendance counts
        public double PresentCount { get; set; }
        public double RenderLate { get; set; }
        public double RenderUndertime { get; set; }
        public double AbsentCount { get; set; }

        // Overtime
        public double RenderOT { get; set; }
        public double AmountOT { get; set; }

        // Income
        public double TotalAllowance { get; set; }
        public double OtherIncome { get; set; }
        public double OtherEmployeePayable { get; set; }

        // Government deductions
        public double DeductionSSSemployee { get; set; }
        public double DeductionWISPemployee { get; set; }
        public double DeductionPHIemployee { get; set; }
        public double DeductionPIFemployee { get; set; }

        //Overtime

        public double? amountOT { get; set; }
        public double? amountNSD { get; set; }
        public double? amountNSDOT { get; set; }

        public double? amountREST { get; set; }
        public double? amountRESTOT { get; set; }
        public double? amountNSDREST { get; set; }
        public double? amountNSDRESTOT { get; set; }


        public double? amountRESTS { get; set; }
        public double? amountRESTOTS { get; set; }
        public double? amountNSDRESTS { get; set; }
        public double? amountNSDRESTOTS { get; set; }


        public double? amountRESTL { get; set; }
        public double? amountRESTOTL { get; set; }
        public double? amountNSDRESTL { get; set; }
        public double? amountNSDRESTOTL { get; set; }

        public double? amountL { get; set; }
        public double? amountOTL { get; set; }
        public double? amountNSDL { get; set; }
        public double? amountNSDOTL { get; set; }

        public double? amountS { get; set; }
        public double? amountOTS { get; set; }
        public double? amountNSDS { get; set; }
        public double? amountNSDOTS { get; set; }

        // Loans
        public double Cashadvance { get; set; }
        public double HdmfLoan { get; set; }
        public double HdmfCalamity { get; set; }
        public double SssLoan { get; set; }
        public double SssCalamity { get; set; }
        public double OtherLoan { get; set; }

        public double WithHeldTax { get; set; }

        // Totals
        public double GrossIncome { get; set; }
        public double TotalDeduction { get; set; }
        public double TotalNetPay { get; set; }

        // Fixed deductions
        public double Healthcard { get; set; }
        public double Parking { get; set; }
        public double Meals { get; set; }
        public double FixedOthers { get; set; }
        public double TotalFixedDeduction { get; set; }
        public double AdditionalMbos { get; set; }
        public double TotalMBOS { get; set; }

        // Bank info
        public string? BankCode { get; set; }
        public string? AccountNo { get; set; }

        // Display fields
        public string? PayPeriod { get; set; }
        public string? Department { get; set; }

        // Government ID Numbers
        public string? SSSNumber { get; set; }
        public string? PhilHealthNumber { get; set; }
        public string? PagIbigNumber { get; set; }
        public string? TINNumber { get; set; }
        public string? PositionName { get; set; }
    }
}