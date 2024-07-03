namespace Gitle.Model.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Enum;
    using Gitle.Model.James;
    using NHibernate.Validator.Cfg.Loquacious.Impl;

    public static class CsvHelper
    {
        private static readonly string lineEnd = Environment.NewLine;
        private static readonly string fieldseparator = ";";

        public static string IssuesCsv(Project project, IList<Issue> issues)
        {
            const string rowTemplate =
                "\"{2}\"{0}\"{3}\"{0}\"{4}\"{0}\"{5}\"{0}\"{6}\"{0}\"{7}\"{0}\"{8}\"{0}\"{9}\"{0}\"{10}\"{0}\"{11}\"{0}\"{12}\"{0}\"{13}\"{0}\"{14}\"{0}\"{15}\"{0}{1}";

            var header = string.Format(rowTemplate, fieldseparator, lineEnd,
                                          "Taaknummer",
                                          "Naam",
                                          "Geopend op",
                                          "Geopend door",
                                          "Gesloten op",
                                          "Gesloten door",
                                          "Totale schatting in uren",
                                          "Totaal billable boekingen in uren",
                                          "Totaal unbillable boekingen in uren",
                                          "Totaal billable boekingen gefactureerd in uren",
                                          "Totaal unbillable boekingen gefactureerd in uren",
                                          "Totaal betaald gefactureerd in uren",
                                          "Totaal onbetaald gefactureerd in uren",
                                          "Labels");

            var rows = "";
            foreach (var issue in issues)
            {
                rows += string.Format(rowTemplate, fieldseparator, lineEnd,
                                      issue.Number,
                                      issue.Name,
                                      issue.CreatedAt?.ToString("yyyy-MM-dd"),
                                      issue.CreatedBy?.FullName,
                                      !issue.IsOpen ? issue.ClosedAt?.ToString("yyyy-MM-dd") : "",
                                      !issue.IsOpen ? issue.ClosedBy?.FullName : "",
                                      issue.TotalHours,
                                      issue.BillableBookingHours(),
                                      issue.UnbillableBookingHours(),
                                      issue.BillableBookingHoursInvoiced(),
                                      issue.UnbillableBookingHoursInvoiced(),
                                      issue.TotalBillableHoursInvoiced,
                                      issue.TotalUnbillableHoursInvoiced,
                                      string.Join(", ", issue.Labels.Select(l => l.Name))
                    );
            }

            return $"{header}{rows}";
        }

        public static string OpenIssuesCsv(IList<Issue> issues)
        {
            const string rowTemplate =
                "\"{2}\"{0}\"{3}\"{0}\"{4}\"{0}\"{5}\"{0}\"{6}\"{0}\"{7}\"{0}\"{8}\"{0}\"{9}\"{0}\"{10}\"{0}\"{11}\"{0}\"{12}\"{0}\"{13}\"{0}\"{14}\"{0}\"{15}\"{0}{1}";

            var header = string.Format(rowTemplate, fieldseparator, lineEnd,
                                          "ProjectType",
                                          "Project",
                                          "Taaknummer",
                                          "Naam",
                                          "Geopend op",
                                          "Geopend door",
                                          "Totale schatting in uren",
                                          "Totaal billable boekingen in uren",
                                          "Totaal unbillable boekingen in uren",
                                          "Totaal billable boekingen gefactureerd in uren",
                                          "Totaal unbillable boekingen gefactureerd in uren",
                                          "Totaal betaald gefactureerd in uren",
                                          "Totaal onbetaald gefactureerd in uren",
                                          "Labels");

            var rows = "";
            foreach (var issue in issues.OrderBy(i => i.Project.TypeString).ThenBy(i => i.Project.Name).ThenBy(i => i.Number))
            {
                rows += string.Format(rowTemplate, fieldseparator, lineEnd,
                                      issue.Project.TypeString,
                                      issue.Project.Name,
                                      issue.Number,
                                      issue.Name,
                                      issue.CreatedAt?.ToString("yyyy-MM-dd"),
                                      issue.CreatedBy?.FullName,
                                      issue.TotalHours,
                                      issue.BillableBookingHours(),
                                      issue.UnbillableBookingHours(),
                                      issue.BillableBookingHoursInvoiced(),
                                      issue.UnbillableBookingHoursInvoiced(),
                                      issue.TotalBillableHoursInvoiced,
                                      issue.TotalUnbillableHoursInvoiced,
                                      string.Join(", ", issue.Labels.Select(l => l.Name))
                    );
            }

            return $"{header}{rows}";
        }



        public static string BookingsCsv(IList<Booking> bookings)
        {
            const string rowTemplate =
                "\"{2}\"{0}\"{3}\"{0}\"{4}\"{0}\"{5}\"{0}\"{6}\"{0}\"{7}\"{0}\"{8}\"{0}\"{9}\"{0}{1}";

            var header = string.Format(rowTemplate, fieldseparator, lineEnd,
                                          "Minuten",
                                          "Datum",
                                          "Gebruiker",
                                          "Projectnummer",
                                          "Project",
                                          "Taak",
                                          "Omschrijving",
                                          "Billable",
                                          "Opmerking");

            var rows = "";
            foreach (var booking in bookings)
            {
                string issueNumber;
                if (booking.Issue != null) issueNumber = booking.Issue.Number.ToString();
                else issueNumber = "DUMP";

                rows += string.Format(rowTemplate, fieldseparator, lineEnd,
                                      booking.Minutes,
                                      booking.Date.ToShortDateString(),
                                      booking.User.FullName,
                                      booking.Project.Number,
                                      booking.Project.Name,
                                      issueNumber,
                                      booking.Issue.Name,
                                      booking.Unbillable ? "Nee" : "Ja",
                                      booking.Comment
                    );
            }

            return $"{header}{rows}";
        }

        public static string ExportWeeks(IList<ExportWeeksGitleVsJames> exportWeeksGitleVsJames, IList<User> employees)
        {
            var numbersOfWeeks = exportWeeksGitleVsJames.First().Weeks.Length;

            var header = "" + fieldseparator;

            foreach (var employee in employees)
                header += employee.FullName + fieldseparator;

            header += lineEnd;

            var rows = "";

            for (int i = 0; i < numbersOfWeeks; i++)
            {
                var row = "Week " + (i + 1) + " Gitle" + fieldseparator;

                foreach (var employee in employees)
                    row += exportWeeksGitleVsJames.FirstOrDefault(x => x.JamesEmployeeId == employee.JamesEmployeeId)?.Weeks[i].MinutesGitle / 60.0 + fieldseparator;

                rows += row + lineEnd;

                row = "Week " + (i + 1) + " James" + fieldseparator;

                foreach (var employee in employees)
                    row += exportWeeksGitleVsJames.FirstOrDefault(x => x.JamesEmployeeId == employee.JamesEmployeeId)?.Weeks[i].MinutesJames / 60.0 + fieldseparator;

                rows += row + lineEnd;
            }

            return $"{header}{rows}";
        }

        public static string InvoiceCsv(IList<Project> projects)
        {
            const string rowTemplate =
                "\"{2}\"{0}\"{3}\"{0}\"{4}\"{0}\"{5}\"{0}\"{6}\"{0}\"{7}\"{0}\"{8}\"{0}\"{9}\"{0}\"{10}\"{0}\"{11}\"{0}\"{12}\"{0}\"{13}\"{0}\"{14}\"{0}\"{15}\"{0}\"{16}\"{0}{1}";

            var header = string.Format(rowTemplate, fieldseparator, lineEnd,
                                          "Klant",
                                          "Applicatie",
                                          "Projectnummer",
                                          "Project",
                                          "Project Id",
                                          "Type",
                                          "Uurtarief",
                                          "Budget initieel project",
                                          "Facturabele uren bij serviceproject",
                                          "Totaal gemaakte uren",
                                          "Billable gemaakte uren",
                                          "Unbillable gemaakte uren",
                                          "Totaal gefactureerde uren",
                                          "Blijf nog te factureren",
                                          "Gesloten");

            var rows = "";
            foreach (var project in projects)
            {
                rows += string.Format(rowTemplate, fieldseparator, lineEnd,
                                      project.Application?.Customer.Name,
                                      project.Application?.Name,
                                      project.Number,
                                      project.Name,
                                      project.Id,
                                      project.TypeString,
                                      project.HourPrice,
                                      project.BudgetHours,
                                      project.SumMaxOfEstimateAndBooking(),
                                      project.TotalHours,
                                      project.BillableHours,
                                      project.UnBillableHours,
                                      project.TotalDefinitiveHours(),
                                      project.ToInvoice(),
                                      project.Closed ? "ja" : "nee"
                    );
            }

            return $"{header}{rows}";
        }

        public static string ProjectCsv(IList<Project> projects)
        {
            const string rowTemplate =
                "\"{2}\"{0}\"{3}\"{0}\"{4}\"{0}\"{5}\"{0}\"{6}\"{0}\"{7}\"{0}\"{8}\"{0}\"{9}\"{0}\"{10}\"{0}\"{11}\"{0}\"{12}\"{0}\"{13}\"{0}\"{14}\"{0}\"{15}\"{0}\"{16}\"{0}{1}";

            var header = string.Format(rowTemplate, fieldseparator, lineEnd,
                                          "Klant",
                                          "Applicatie",
                                          "Projectnummer",
                                          "Project",
                                          "Project Id",
                                          "Type",
                                          "Klantcontacten",
                                          "Uurtarief",
                                          "Budget initieel project",
                                          "Facturabele uren bij serviceproject",
                                          "Totaal gemaakte uren",
                                          "Billable gemaakte uren",
                                          "Unbillable gemaakte uren",
                                          "Totaal gefactureerde uren",
                                          "Blijf nog te factureren");

            var rows = "";
            foreach (var project in projects)
            {
                var customers = project.Users.Where(up => !up.User.IsAdmin && !up.User.CanBookHours).ToList();
                var customerString = "";
                var i = 0;
                foreach (var customer in customers)
                {
                    if (i > 0)
                    {
                        customerString = customerString + ", ";
                    }
                    customerString = customerString + customer.User.FullName;
                    i++;
                }
                rows += string.Format(rowTemplate, fieldseparator, lineEnd,
                                      project.Application?.Customer.Name,
                                      project.Application?.Name,
                                      project.Number,
                                      project.Name,
                                      project.Id,
                                      project.TypeString,
                                      customerString,
                                      project.HourPrice,
                                      project.BudgetHours,
                                      project.SumMaxOfEstimateAndBooking(),
                                      project.TotalHours,
                                      project.BillableHours,
                                      project.UnBillableHours,
                                      project.TotalDefinitiveHours(),
                                      project.ToInvoice()
                    );
            }

            return $"{header}{rows}";
        }
    }
}