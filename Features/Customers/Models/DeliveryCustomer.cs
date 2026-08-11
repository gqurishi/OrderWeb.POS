namespace POS_in_NET.Models
{
    public class DeliveryCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Postcode { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }
}
