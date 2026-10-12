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

    public Dictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray());

    public ServiceResult<T> ToResult<T>() => ServiceResult<T>.Invalid(ToDictionary());
}
