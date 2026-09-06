namespace OrderWeb.Contracts.Results;

public enum OperationErrorCode
{
    None = 0,
    Validation,
    Unauthorized,
    Forbidden,
    Offline,
    Conflict,
    NotFound,
    ServerError,
    Retryable,
    Unknown
}

public sealed record OperationError(
    OperationErrorCode Code,
    string Message,
    string? Detail = null,
    string? CorrelationId = null);

public record OperationResult
{
    protected OperationResult(bool isSuccess, OperationError? error)
    {
        if (isSuccess == (error is not null))
        {
            throw new ArgumentException("A successful result cannot contain an error, and a failed result must contain one.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public OperationError? Error { get; }

    public static OperationResult Success() => new(true, null);

    public static OperationResult Failure(OperationError error) =>
        new(false, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed record OperationResult<T> : OperationResult
{
    private OperationResult(bool isSuccess, T? value, OperationError? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Success(T value) => new(true, value, null);

    public new static OperationResult<T> Failure(OperationError error) =>
        new(false, default, error ?? throw new ArgumentNullException(nameof(error)));
}
