using System;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Represents a table in the restaurant
    /// </summary>
    public class RestaurantTable
    {
        public int Id { get; set; }
        public string TableNumber { get; set; } = string.Empty;
        public int FloorId { get; set; }
        public int Capacity { get; set; }
        public TableShape Shape { get; set; } = TableShape.Square;
        public TableStatus Status { get; set; } = TableStatus.Available;
        public string TableDesignIcon { get; set; } = "table_1.png"; // Default table design
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
        public bool IsActive { get; set; } = true;
        
        // Visual Layout Position (for drag-and-drop in Visual Table Layout)
        public int PositionX { get; set; } = 0;
        public int PositionY { get; set; } = 0;
        
        // Session tracking properties
        public int? CurrentSessionId { get; set; }
        public DateTime? LastOccupied { get; set; }
        public int TotalSessionsToday { get; set; } = 0;
        
        // Navigation properties
        public TableSession? CurrentSession { get; set; }
        
        // Additional properties for UI display (not in database)
        public string FloorName { get; set; } = string.Empty;
        public string ShapeIcon => Shape == TableShape.Square ? "" : "▭";
        public string ShapeDisplay => Shape.ToString();
        
        // Color for table card based on floor
        public string FloorColor
        {
            get
            {
                // Assign very light blue shades based on floor - subtle and elegant
                var colors = new[]
                {
                    "#F1F3FB", // Very light blue - Floor 1
                    "#E4E9F9", // Light blue - Floor 2
                    "#D3DBFA", // Medium light blue - Floor 3
                    "#F1F3FB", // Very light blue - Floor 4 (repeat)
                    "#E4E9F9", // Light blue - Floor 5 (repeat)
                    "#D3DBFA", // Medium light blue - Floor 6 (repeat)
                };
                
                // Use FloorId to determine color (cycle through colors)
                return colors[(FloorId - 1) % colors.Length];
            }
        }
        
        // Enhanced status display with session information
        public string StatusDisplay
        {
            get
            {
                if (CurrentSession != null && Status == TableStatus.Occupied)
                {
                    return CurrentSession.StatusDisplay;
                }
                return Status switch
                {
                    TableStatus.Available => " Available",
                    TableStatus.Occupied => " Occupied",
                    TableStatus.Reserved => " Cleaning",
                    _ => Status.ToString()
                };
            }
        }
        
        // Session info for display on table
        public string SessionInfo
        {
            get
            {
                if (CurrentSession == null) return "";
                return $"{CurrentSession.PartySize} guests • {CurrentSession.TimeDisplay}";
            }
        }
        
        // Special occasion indicator
        public string OccasionIndicator
        {
            get
            {
                if (CurrentSession?.SpecialOccasion == null) return "";
                return CurrentSession.SpecialOccasion.ToLower() switch
                {
                    "birthday" => "",
                    "anniversary" => "",
                    "date" => "",
                    "business" => "",
                    _ => "⭐"
                };
            }
        }
        
        // Color for table display based on status
        // Green  = no active session/order
        // Amber  = active session/order (editable)
        // Red    = stuck/problem attention state
        public string TableColor
        {
            get
            {
                var hasActiveSession = CurrentSession != null;
                var hasProblemState = CurrentSession?.LinkedOrderIsStaleDraft == true
                    || CurrentSession?.Status == TableSessionStatus.Cleaning
                    || Status == TableStatus.Reserved;

                if (hasProblemState)
                {
                    return "#FEE2E2"; // Red - stuck/problem attention
                }

                if (hasActiveSession)
                {
                    return "#FEF3C7"; // Amber - active/editable table session
                }

                return "#D1FAE5"; // Green - idle / ready for next order
            }
        }
        
        // Overtime warning if session is running long
        public bool IsOvertime => CurrentSession?.IsOvertime ?? false;
        public string OvertimeWarning => CurrentSession?.OvertimeWarning ?? "";
    }

    /// <summary>
    /// Table shape enumeration
    /// </summary>
    public enum TableShape
    {
        Square,
        Rectangle
    }

    /// <summary>
    /// Table status enumeration
    /// </summary>
    public enum TableStatus
    {
        Available,
        Occupied,
        Reserved
    }
}
