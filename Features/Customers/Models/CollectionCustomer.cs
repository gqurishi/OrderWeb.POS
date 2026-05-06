namespace POS_in_NET.Models
{
    public class CollectionCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastOrderDate { get; set; }
    }
}
