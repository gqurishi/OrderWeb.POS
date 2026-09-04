using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed record TerminalRoleCheck(bool Allowed, string Reason);

public static class TerminalRoleService
{
    public static bool CanRunMotherJobs =>
        TerminalConfigurationService.IsConfigured &&
        TerminalConfigurationService.IsMotherTerminal;

    public static bool IsLocalPosTerminal =>
        TerminalConfigurationService.IsConfigured;

    public static bool CanGenerateEndOfDayReports => CanRunMotherJobs;

    public static bool CanPrintZReport => CanRunMotherJobs;

    public static bool CanViewFullReports => CanRunMotherJobs;

    public static bool CanViewLimitedReports => IsLocalPosTerminal;

    public static async Task<TerminalRoleCheck> CanRunOnlineOrderMasterJobsAsync(DatabaseService? databaseService = null)
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return new TerminalRoleCheck(false, "Terminal setup is not complete.");
        }

        var terminalConfig = TerminalConfigurationService.GetConfiguration();
        if (terminalConfig.IsChild)
        {
            return new TerminalRoleCheck(false, "Child terminal: online order master jobs are disabled.");
        }

        try
        {
            databaseService ??= new DatabaseService();
            var cloudConfig = await databaseService.GetCloudConfigurationAsync();

            if (cloudConfig == null)
            {
                return new TerminalRoleCheck(true, "No online master configured yet. Mother terminal may run online jobs.");
            }

            if (!cloudConfig.OnlineOrderMasterEnabled)
            {
                return new TerminalRoleCheck(false, "Online Order Master is disabled in settings.");
            }

            if (string.IsNullOrWhiteSpace(cloudConfig.OnlineOrderMasterTerminalName))
            {
                return new TerminalRoleCheck(true, "No online master terminal name set. Mother terminal may run online jobs.");
            }

            var matchesThisTerminal = string.Equals(
                cloudConfig.OnlineOrderMasterTerminalName.Trim(),
                terminalConfig.TerminalName.Trim(),
                StringComparison.OrdinalIgnoreCase);

            return matchesThisTerminal
                ? new TerminalRoleCheck(true, $"This terminal is Online Order Master: {terminalConfig.TerminalName}.")
                : new TerminalRoleCheck(false, $"Online Order Master is {cloudConfig.OnlineOrderMasterTerminalName}.");
        }
        catch (Exception ex)
        {
            return new TerminalRoleCheck(false, $"Could not check Online Order Master setting: {ex.Message}");
        }
    }
}
