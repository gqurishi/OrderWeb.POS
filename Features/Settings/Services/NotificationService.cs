using System;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    public enum NotificationType
    {
        Success,
        Error,
        Info,
        Warning
    }

    public class NotificationService
    {
        public event EventHandler<NotificationEventArgs>? NotificationRequested;

        private static NotificationService? _instance;
        public static NotificationService Instance => _instance ??= new NotificationService();

        private NotificationService() { }

        /// <summary>
        /// Show a success notification (auto-dismiss in 2 seconds)
        /// </summary>
        public void ShowSuccess(string message, string? title = null)
        {
            ShowNotification(title ?? "Success", message, NotificationType.Success, 2000);
        }

        /// <summary>
        /// Show an error notification (auto-dismiss in 4 seconds)
        /// </summary>
        public void ShowError(string message, string? title = null)
        {
            ShowNotification(title ?? "Error", message, NotificationType.Error, 4000);
        }

        /// <summary>
        /// Show an info notification (auto-dismiss in 3 seconds)
        /// </summary>
        public void ShowInfo(string message, string? title = null)
        {
            ShowNotification(title ?? "Info", message, NotificationType.Info, 3000);
        }

        /// <summary>
        /// Show a warning notification (auto-dismiss in 3 seconds)
        /// </summary>
        public void ShowWarning(string message, string? title = null)
        {
            ShowNotification(title ?? "Warning", message, NotificationType.Warning, 3000);
        }

        /// <summary>
        /// Show a custom notification with manual duration
        /// </summary>
        public void ShowNotification(string title, string message, NotificationType type, int durationMs = 2000)
        {
            NotificationRequested?.Invoke(this, new NotificationEventArgs
            {
                Title = title,
                Message = message,
                Type = type,
                DurationMs = durationMs
            });
        }
    }

    public class NotificationEventArgs : EventArgs
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; }
        public int DurationMs { get; set; }
    }
}
