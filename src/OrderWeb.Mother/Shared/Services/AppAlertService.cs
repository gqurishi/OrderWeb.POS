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

        if (lowered.Contains("error") || lowered.Contains("failed") || lowered.Contains("denied") || lowered.Contains("invalid") || lowered.Contains("couldn't") || lowered.Contains("could not"))
        {
            return ("x", "#DC2626", "#DC2626", "White");
        }

        if (lowered.Contains("success") || lowered.Contains("complete") || lowered.Contains("saved") || lowered.Contains("export") || lowered.Contains("download"))
        {
            return ("ok", "#059669", "#059669", "White");
        }

        if (lowered.Contains("warning") || lowered.Contains("required") || lowered.Contains("validation"))
        {
            return ("!", "#D97706", "#D97706", "White");
        }

        return ("i", "#2563EB", "#2563EB", "White");
    }
}
