namespace Spydersoft.SwingTrade.Api.Services;

/// <summary>Collects field errors keyed by property name, for a 400 ValidationProblemDetails.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
            _errors[field] = list = [];
        list.Add(message);
    }

    /// <summary>Rejects text longer than its column, which the database would otherwise refuse with a 500.</summary>
    public void MaxLength(string field, string? value, int max)
    {
        if (value is not null && value.Trim().Length > max)
            Add(field, $"{field} cannot be longer than {max} characters.");
    }

    /// <summary>Rejects an amount larger than its column can hold (numeric overflow would be a 500).</summary>
    public void MaxAmount(string field, decimal value, decimal max)
    {
        if (value > max)
            Add(field, $"{field} cannot be greater than {max:0.####}.");
    }

    public Dictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

    public ServiceResult<T> ToResult<T>() => ServiceResult<T>.Invalid(ToDictionary());
}
