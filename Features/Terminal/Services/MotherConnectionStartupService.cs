namespace POS_in_NET.Services;

public sealed class MotherConnectionStartupService
{
    private readonly TerminalHealthService _terminalHealthService;
    private readonly ClientWebSocketBroadcastService _clientApiService;

    public MotherConnectionStartupService(
        TerminalHealthService terminalHealthService,
        ClientWebSocketBroadcastService clientApiService)
    {
        _terminalHealthService = terminalHealthService;
        _clientApiService = clientApiService;
    }

    public async Task StartAsync()
    {
        if (!TerminalConfigurationService.IsConfigured)
        {
            return;
        }

        _terminalHealthService.Start();

        if (!TerminalConfigurationService.IsMotherTerminal)
        {
            System.Diagnostics.Debug.WriteLine("Mother Client API skipped: this terminal is not configured as Mother.");
            return;
        }

        await _clientApiService.StartAsync(ClientWebSocketBroadcastService.DefaultPort);
        System.Diagnostics.Debug.WriteLine($"Mother Client API service: {_clientApiService.StatusMessage}");
    }
}
