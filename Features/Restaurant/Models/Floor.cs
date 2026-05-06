using System;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Represents a floor in the restaurant
    /// </summary>
    public class Floor
    {
        public Floor()
        {
            Name = string.Empty;
            Description = string.Empty;
            BackgroundImage = string.Empty;
            IsActive = true;
            TableCount = 0;
            CreatedDate = DateTime.Now;
            UpdatedDate = DateTime.Now;
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string BackgroundImage { get; set; } // Path to floor background image (PNG/JPG)
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
        public bool IsActive { get; set; }
        public int TableCount { get; set; }
        
        // Helper to check if floor has a background image
        public bool HasBackgroundImage => !string.IsNullOrEmpty(BackgroundImage);
    }
}
