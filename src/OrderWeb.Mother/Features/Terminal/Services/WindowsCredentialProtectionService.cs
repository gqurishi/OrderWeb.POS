using System.Security.Cryptography;
using System.Text;

namespace POS_in_NET.Services;

/// <summary>
/// Protects till credentials with the Windows Data Protection API. Ciphertext is
/// bound to the signed-in Windows account and cannot be copied to another user.
/// </summary>
internal static class WindowsCredentialProtectionService
{
    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("OrderWebPOS.TerminalDatabaseCredential.v1"));

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static string Protect(string plaintext)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Windows credential protection is available on production Windows tills only.");
        }

        var clearBytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
        try
        {
            var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public static string Unprotect(string protectedValue)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Windows credential protection is available on production Windows tills only.");
        }

        var protectedBytes = Convert.FromBase64String(protectedValue);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(clearBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }
}
