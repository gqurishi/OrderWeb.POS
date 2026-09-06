namespace OrderWeb.Client.Services;

/// <summary>Identity headers required by Mother for every protected operation.</summary>
public static class ClientCompatibilityHeaders
{
    public const int PayloadVersion = 1;
    public static void Apply(HttpClient client)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-POS-App-Version", AppInfo.VersionString);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-POS-Build-Number", AppInfo.BuildString);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-POS-Platform", DeviceInfo.Platform.ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-POS-Schema-Version", ClientCacheService.CurrentSchemaVersion.ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-POS-Payload-Version", PayloadVersion.ToString());
    }
}
