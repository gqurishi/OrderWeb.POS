namespace OrderWeb.Contracts.Results;

public enum OperationErrorCode
{
    None = 0,
    Validation = 1,
    Unauthorized = 2,
    Forbidden = 3,
    Offline = 4,
    Conflict = 5,
    NotFound = 6,
    Server = 7,
    Retryable = 8,
    Unknown = 9,
    PermissionDenied = 10
}

public sealed record OperationError(
    OperationErrorCode Code,
    string Message,
    string? Detail = null,
    string? CorrelationId = null)
{
    public static OperationError Validation(string message, string? detail = null) =>
        new(OperationErrorCode.Validation, message, detail);

    public static OperationError NotFound(string message, string? detail = null) =>
        new(OperationErrorCode.NotFound, message, detail);

    public static OperationError Conflict(string message, string? detail = null) =>
        new(OperationErrorCode.Conflict, message, detail);

    public static OperationError Forbidden(string message, string? detail = null) =>
        new(OperationErrorCode.Forbidden, message, detail);

    public static OperationError PermissionDenied(string message, string? detail = null) =>
        new(OperationErrorCode.PermissionDenied, message, detail);

    public static OperationError Failure(string message, string? detail = null) =>
        new(OperationErrorCode.Server, message, detail);
}

public class OperationResult
{
    protected OperationResult(bool isSuccess, OperationError? error)
    {
        if (isSuccess == (error is not null))
        {
            throw new ArgumentException("Success results cannot include an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public OperationError? Error { get; }

    public static OperationResult Ok() => new(true, null);
    public static OperationResult Fail(OperationError error) => new(false, error);
}

public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool isSuccess, T? value, OperationError? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Ok(T value) => new(true, value, null);
    public new static OperationResult<T> Fail(OperationError error) => new(false, default, error);
}
