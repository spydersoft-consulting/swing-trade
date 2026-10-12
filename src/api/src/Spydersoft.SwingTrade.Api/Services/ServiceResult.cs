namespace Spydersoft.SwingTrade.Api.Services;

public enum ResultKind
{
    Ok,
    NotFound,
    Invalid,
    Conflict
}

/// <summary>Outcome of an application service call; controllers map it to HTTP.</summary>
public sealed class ServiceResult<T>
{
    private ServiceResult(ResultKind kind, T? value, IDictionary<string, string[]>? errors, string? detail)
    {
        Kind = kind;
        Value = value;
        Errors = errors ?? new Dictionary<string, string[]>();
        Detail = detail;
    }

    public ResultKind Kind { get; }
    public T? Value { get; }
    public IDictionary<string, string[]> Errors { get; }
    public string? Detail { get; }
    public bool IsOk => Kind == ResultKind.Ok;

    public static ServiceResult<T> Ok(T value) => new(ResultKind.Ok, value, null, null);
    public static ServiceResult<T> NotFound() => new(ResultKind.NotFound, default, null, null);
    public static ServiceResult<T> Invalid(IDictionary<string, string[]> errors) => new(ResultKind.Invalid, default, errors, null);
    public static ServiceResult<T> Conflict(string detail) => new(ResultKind.Conflict, default, null, detail);
}
