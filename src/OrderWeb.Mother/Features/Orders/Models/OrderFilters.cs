using System;

namespace POS_in_NET.Models
{
    public class OrderFilters
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? Status { get; set; }
        public string? CustomerSearch { get; set; }
        public int? MinAmount { get; set; }
        public int? MaxAmount { get; set; }
        public string? OrderType { get; set; } // "Web", "POS", "All"
    }
}