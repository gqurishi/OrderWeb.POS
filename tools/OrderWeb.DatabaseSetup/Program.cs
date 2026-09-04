using System.Reflection;

namespace OrderWeb.DatabaseSetup;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            CommandRunner.PrintHelp();
            return args.Length == 0 ? ExitCodes.InvalidArguments : ExitCodes.Success;
        }

        try
        {
            var command = args[0];
            var options = CommandLineOptions.Parse(args.Skip(1));
            var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var runner = new CommandRunner(appVersion, options);
            return await runner.RunAsync(command);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return ExitCodes.UnexpectedError;
        }
    }
}
