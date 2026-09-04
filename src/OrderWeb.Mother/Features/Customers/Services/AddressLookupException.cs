namespace POS_in_NET.Services;

public sealed class AddressLookupException : Exception
{
    public AddressLookupException(string message, IReadOnlyList<string>? suggestions = null, string? errorCode = null)
        : base(message)
    {
        Suggestions = suggestions ?? Array.Empty<string>();
        ErrorCode = errorCode;
    }

    public IReadOnlyList<string> Suggestions { get; }
    public string? ErrorCode { get; }
}
