namespace OrderWeb.DatabaseSetup;

/// <summary>
/// Exit codes for Inno Setup and automation scripts.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int ConfigError = 2;
    public const int ConnectionFailed = 3;
    public const int MigrationFailed = 4;
    public const int VerifyFailed = 5;
    public const int BackupFailed = 6;
    public const int RestoreFailed = 7;
    public const int VersionMismatch = 8;
    public const int UnexpectedError = 99;
}
