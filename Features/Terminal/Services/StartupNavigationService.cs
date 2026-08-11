using Microsoft.Maui.Storage;

namespace POS_in_NET.Services;

public static class StartupNavigationService
{
    public static async Task<string> GetInitialRouteAsync()
    {
        TerminalConfigurationService.TryApplyInstallerDatabaseConfig(forceReapply: true);

        if (!TerminalConfigurationService.IsConfigured)
        {
            return "//terminalsetup";
        }

        return await GetPostSetupRouteAsync();
    }

    public static async Task<string> GetPostSetupRouteAsync()
    {
        var schemaGate = await ChildSchemaVersionGateService.CheckAsync();
        if (!schemaGate.IsCompatible)
        {
            Preferences.Default.Set("child_schema_gate_message", schemaGate.Message);
            return "//login";
        }

        Preferences.Default.Remove("child_schema_gate_message");

        if (TerminalConfigurationService.IsMotherTerminal)
        {
            var auth = AuthenticationService.Instance;
            var schema = await auth.EnsureAuthenticationSchemaAsync();
            if (schema.Success && !await auth.HasAnyUserAsync())
            {
                return "//initialadminsetup";
            }
        }

        return "//login";
    }
}
