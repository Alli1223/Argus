namespace Argus.Agent.Transport;

internal enum FailureKind
{
    None,

    /// <summary>The server could not be reached (network error or timeout); try again later.</summary>
    Unreachable,

    /// <summary>The server does not accept the agent key or enrollment token.</summary>
    Unauthorized,

    /// <summary>The server refused the request itself (4xx); sending it again would not help.</summary>
    Rejected,

    /// <summary>Too many requests; wait for the time the server asked for.</summary>
    RateLimited,

    /// <summary>The server failed (5xx); try again later.</summary>
    ServerError,
}

internal sealed record ApiResult<T>(T? Value, FailureKind Failure, int StatusCode, string? Detail, TimeSpan? RetryAfter)
{
    public bool IsSuccess => Failure == FailureKind.None;

    public static ApiResult<T> Success(T value) => new(value, FailureKind.None, 200, null, null);

    public static ApiResult<T> Fail(FailureKind failure, string? detail, int statusCode = 0, TimeSpan? retryAfter = null) =>
        new(default, failure, statusCode, detail, retryAfter);

    public ApiResult<TOther> As<TOther>() => new(default, Failure, StatusCode, Detail, RetryAfter);

    public string Describe() =>
        StatusCode > 0 ? $"HTTP {StatusCode}{(string.IsNullOrWhiteSpace(Detail) ? "" : ": " + Detail)}" : Detail ?? Failure.ToString();
}
