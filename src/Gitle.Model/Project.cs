﻿namespace Gitle.Model
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Enum;
    using Helpers;
    using Interfaces.Model;

    public class Project : ModelBase, IDocumentContainer, ISlugger
    {
        public Project()
        {
            Users = new List<UserProject>();
            Labels = new List<Label>();
            Documents = new List<Document>();
        }

        public virtual string Name { get; set; }

        public virtual string Slug { get; set; }

        public virtual string Repository { get; set; }
        public virtual int MilestoneId { get; set; }
        public virtual string MilestoneName { get; set; }
        public virtual decimal HourPrice { get; set; }
        public virtual int FreckleId { get; set; }
        public virtual string FreckleName { get; set; }
        public virtual string Information { get; set; }
        public virtual string Comments { get; set; }

        public virtual double BudgetMinutes { get; set; }
        public virtual double BudgetHours => BudgetMinutes / 60.0;
        public virtual string BudgetTime => $"{Math.Floor(BudgetHours)}:{BudgetMinutes - (Math.Floor(BudgetHours)*60):00}";

        public virtual ProjectType Type { get; set; }
        public virtual string TypeString => Type.GetDescription();

        public virtual bool TicketRequiredForBooking { get; set; }

        public virtual Customer Customer { get; set; }
        public virtual Application Application { get; set; }

        public virtual IList<UserProject> Users { get; set; }
        public virtual IList<Label> Labels { get; set; }
        public virtual IList<Issue> Issues { get; set; }
        public virtual IList<Invoice> Invoices { get; set; }
        public virtual IList<Document> Documents { get; set; } 
        public virtual IList<Booking> Bookings { get; set; } 

        public virtual int NotifiedUsers => Users.Count(u => u.Notifications);

        public virtual int NewIssueNumber => (Issues.Any() ? Issues.Max(x => x.Number) : 0) + 1;

        public virtual double BillableMinutes => Bookings.Where(x => x.IsActive && !x.Unbillable).Sum(x => x.Minutes);
        public virtual double BillableHours => Bookings.Where(x => x.IsActive && !x.Unbillable).Sum(x => x.Hours);
        public virtual double UnBillableMinutes => Bookings.Where(x => x.IsActive && x.Unbillable).Sum(x => x.Minutes);
        public virtual double UnBillableHours => Bookings.Where(x => x.IsActive && x.Unbillable).Sum(x => x.Hours);
        public virtual double TotalHours => Bookings.Where(x => x.IsActive).Sum(x => x.Hours);

        public virtual string CompleteName => $"{Name} ({Application?.Name}, {Application?.Customer?.Name})";

        public virtual double SumMaxOfEstimateAndBooking()
        {
            if (Type != ProjectType.Service) return 0.0;
            var issueMax = Issues.Where(i => i.IsActive).Sum(i => i.MaxOfBookingAndTotalHours());
            var noIssueBookingsMax = Bookings.Where(b => b.Issue == null && b.IsActive && !b.Unbillable).Sum(b => b.Hours);

            return issueMax + noIssueBookingsMax;
        }

        public virtual double TotalDefinitiveHours()
        {
            return Invoices.Where(i => i.State == InvoiceState.Definitive && i.IsActive).Sum(i => i.TotalHours);
        }

        public virtual double ToInvoice()
        {
            if (Type == ProjectType.Service)
            {
                return SumMaxOfEstimateAndBooking() - TotalDefinitiveHours();
            }
            if (Type == ProjectType.Initial)
            {
                return Math.Max(BudgetHours, BillableHours) - TotalDefinitiveHours();
            }
            return 0.0;
        }
    }
}