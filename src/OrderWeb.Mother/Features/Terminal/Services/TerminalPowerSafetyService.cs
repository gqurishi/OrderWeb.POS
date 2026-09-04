namespace POS_in_NET.Services;

public static class TerminalPowerSafetyService
{
    public static bool IsMotherSleepDisabled =>
        TerminalConfigurationService.IsConfigured &&
        TerminalConfigurationService.IsMotherTerminal &&
        DeviceDisplay.Current.KeepScreenOn;

    public static void Apply()
    {
        if (!TerminalConfigurationService.IsConfigured || !TerminalConfigurationService.IsMotherTerminal)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                DeviceDisplay.Current.KeepScreenOn = true;
                System.Diagnostics.Debug.WriteLine("Mother terminal sleep disabled with KeepScreenOn.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not disable mother terminal sleep: {ex.Message}");
            }
        });
    }
}
