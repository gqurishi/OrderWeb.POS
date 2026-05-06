using System;
using System.ComponentModel.DataAnnotations;

namespace POS_in_NET.Models
{
    /// <summary>
    /// Types of notes that can be added to a table session
    /// </summary>
    public enum SessionNoteType
    {
        General,    // General information
        Allergy,    // Food allergies or dietary restrictions
        Request,    // Special requests (window seat, birthday cake, etc.)
        Complaint,  // Customer complaints or issues
        VIP         // VIP customer notes
    }

    /// <summary>
    /// Notes and comments for a table session
    /// </summary>
    public class SessionNote
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        
        [Required]
        public string Note { get; set; } = string.Empty;
        
        public SessionNoteType NoteType { get; set; } = SessionNoteType.General;
        
        [StringLength(100)]
        public string? CreatedBy { get; set; } // Admin/Staff name
        
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        
        // Navigation properties
        public TableSession? Session { get; set; }
        
        // Display properties
        public string NoteTypeDisplay => NoteType switch
        {
            SessionNoteType.General => "📝 General",
            SessionNoteType.Allergy => "⚠️ Allergy",
            SessionNoteType.Request => "🙋 Request",
            SessionNoteType.Complaint => "😞 Issue",
            SessionNoteType.VIP => "⭐ VIP",
            _ => "📝 Note"
        };
        
        public string NoteTypeColor => NoteType switch
        {
            SessionNoteType.General => "#6C757D",      // Gray
            SessionNoteType.Allergy => "#DC3545",     // Red
            SessionNoteType.Request => "#007BFF",     // Blue
            SessionNoteType.Complaint => "#FD7E14",   // Orange
            SessionNoteType.VIP => "#6F42C1",         // Purple
            _ => "#6C757D"                            // Gray
        };
        
        public string TimeAgo
        {
            get
            {
                var timeSpan = DateTime.Now - CreatedDate;
                if (timeSpan.TotalMinutes < 1)
                    return "Just now";
                else if (timeSpan.TotalMinutes < 60)
                    return $"{(int)timeSpan.TotalMinutes} min ago";
                else if (timeSpan.TotalHours < 24)
                    return $"{(int)timeSpan.TotalHours} hr ago";
                else
                    return CreatedDate.ToString("MMM dd");
            }
        }
    }
}