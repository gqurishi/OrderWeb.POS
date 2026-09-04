namespace OrderWeb.DatabaseSetup;

public sealed class CommandLineOptions
{
    public string? ConfigPath { get; init; }
    public string? MigrationsPath { get; init; }
    public string? RootUser { get; init; }
    public string? RootPassword { get; init; }
    public string? DatabasePassword { get; init; }
    public IReadOnlyList<string> ChildHosts { get; init; } = [];
    public bool RequireTls { get; init; }
    public string? OutputPath { get; init; }
    public string? InputPath { get; init; }
    public int? RequiredSchemaVersion { get; init; }
    public bool SkipBackup { get; init; }
    public bool Quiet { get; init; }
    public bool Json { get; init; }

    public static CommandLineOptions Parse(IEnumerable<string> args)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var token = list[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map[key] = list[++i];
            }
            else
            {
                flags.Add(key);
            }
        }

        return new CommandLineOptions
        {
            ConfigPath = map.GetValueOrDefault("config-path"),
            MigrationsPath = map.GetValueOrDefault("migrations-path"),
            RootUser = map.GetValueOrDefault("root-user") ?? Environment.GetEnvironmentVariable("ORDERWEB_ROOT_USER"),
            RootPassword = map.GetValueOrDefault("root-password") ?? Environment.GetEnvironmentVariable("ORDERWEB_ROOT_PASSWORD"),
            DatabasePassword = map.GetValueOrDefault("database-password") ?? Environment.GetEnvironmentVariable("ORDERWEB_APP_PASSWORD"),
            ChildHosts = ParseChildHosts(map.GetValueOrDefault("child-ips")),
            RequireTls = flags.Contains("require-tls"),
            OutputPath = map.GetValueOrDefault("output"),
            InputPath = map.GetValueOrDefault("input"),
            RequiredSchemaVersion = int.TryParse(map.GetValueOrDefault("required"), out var required) ? required : null,
            SkipBackup = flags.Contains("skip-backup"),
            Quiet = flags.Contains("quiet"),
            Json = flags.Contains("json")
        };
    }

    private static IReadOnlyList<string> ParseChildHosts(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}
