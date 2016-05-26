﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Gitle.Model
{
    public class InvoiceLine : ModelBase
    {
        public InvoiceLine()
        {
            Hours = 0;
        }

        public virtual Invoice Invoice { get; set; }
        public virtual Issue Issue { get; set; }
        public virtual string Description { get; set; }
        public virtual double Hours { get; set; }
        public virtual decimal Price => Null ? 0 : (decimal)Hours * Invoice.HourPrice;

        public virtual bool Null
        {
            get { return Bookings.All(x => x.Unbillable); }
        }

        public virtual IList<Booking> Bookings { get { return Invoice?.Bookings.Where(x => x.Issue == Issue).ToList() ?? new List<Booking>(); } }

        public virtual IList<Booking> OldBookings
        {
            get
            {
                return Issue?.Bookings.Where(x => !Bookings.Contains(x) && x.IsActive).ToList() ?? new List<Booking>();
            }
        }

        public virtual IList<InvoiceLine> OldInvoiceLines => Issue?.InvoiceLines.Where(x => x.Invoice.IsDefinitive).ToList() ?? new List<InvoiceLine>();

        public virtual double EstimateHours => Issue.Hours - OldInvoiceLines.Sum(x => x.Hours);
        public virtual double FullEstimateHours => Issue.Hours;
        public virtual double BookingHours { get { return Bookings.Where(x => !x.Unbillable).Sum(x => x.Hours); } }
    }

    public class Correction : ModelBase
    {
        public virtual Invoice Invoice { get; set; }
        public virtual string Description { get; set; }
        public virtual decimal Price { get; set; }
    }
}
