using POS_in_NET.Views;

namespace POS_in_NET.Services;

public static class AppAlertService
{
    public static async Task ShowAlertAsync(string title, string message)
    {
        var normalizedTitle = title?.Trim() ?? "Info";
        var normalizedMessage = message?.Trim() ?? string.Empty;

        var (icon, iconBg, buttonBg, buttonText) = ResolveStyle(normalizedTitle);

        var dialog = new ModernAlertDialog();
        dialog.SetAlert(normalizedTitle, normalizedMessage, icon, iconBg, buttonBg, buttonText);
        await dialog.ShowAsync();
    }

    private static (string Icon, string IconBg, string ButtonBg, string ButtonText) ResolveStyle(string title)
    {
        var lowered = title.ToLowerInvariant();

        const string brandIconBg = "#2563EB";
        const string brandButtonBg = "#2563EB";
        const string brandButtonText = "White";

        if (lowered.Contains("error") || lowered.Contains("failed") || lowered.Contains("denied") || lowered.Contains("invalid"))
        {
            return ("x", brandIconBg, brandButtonBg, brandButtonText);
        }

        if (lowered.Contains("success") || lowered.Contains("complete") || lowered.Contains("saved") || lowered.Contains("export") || lowered.Contains("download"))
        {
            return ("ok", brandIconBg, brandButtonBg, brandButtonText);
        }

        if (lowered.Contains("warning") || lowered.Contains("required") || lowered.Contains("validation"))
        {
            return ("!", brandIconBg, brandButtonBg, brandButtonText);
        }

        return ("i", brandIconBg, brandButtonBg, brandButtonText);
    }
}
