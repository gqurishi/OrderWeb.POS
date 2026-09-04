namespace POS_in_NET.Models
{
    /// <summary>
    /// Model for visual floor plan table display
    /// </summary>
    public class FloorPlanTable
    {
        public int Id { get; set; }
        public string TableName { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public string Status { get; set; } = "available"; // available, occupied, printed, reserved
        public string FloorName { get; set; } = string.Empty;
        public int FloorId { get; set; }
        public string TableDesignIcon { get; set; } = "table_1.png"; // Table design icon
        public bool IsAvailable => Status.ToLower() == "available";
        public bool IsOccupied => Status.ToLower() == "occupied";
        public bool HasPrintedReceipt => Status.ToLower() == "printed";
        public bool IsReserved => Status.ToLower() == "reserved";
        
        // For binding to the visual layout
        public RestaurantTable? SourceTable { get; set; }
        public TableSession? ActiveSession { get; set; }
    }
}