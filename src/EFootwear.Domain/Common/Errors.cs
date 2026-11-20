namespace EFootwear.Domain.Common;

/// <summary>Base class for every expected business/validation failure. Mapped to RFC 7807 responses by the API.</summary>
public abstract class AppException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class NotFoundException(string what, object? key = null)
    : AppException("not_found", key is null ? $"{what} was not found." : $"{what} '{key}' was not found.", 404);

public sealed class ForbiddenException(string message = "You do not have permission to do this.")
    : AppException("forbidden", message, 403);

public sealed class UnauthorizedAppException(string message = "Authentication failed.")
    : AppException("unauthorized", message, 401);

public sealed class ConflictException(string message, string code = "conflict")
    : AppException(code, message, 409);

/// <summary>A business rule was broken (for example insufficient stock or an over-return). HTTP 422.</summary>
public sealed class BusinessRuleException(string message, string code = "business_rule")
    : AppException(code, message, 422);

public sealed class ValidationException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("validation_failed", "One or more fields are invalid.", 400) => Errors = errors;
}

/// <summary>Collects field errors and throws one <see cref="ValidationException"/>.</summary>
public sealed class Check
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public Check Require(bool ok, string field, string message)
    {
        if (!ok)
        {
            if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
            list.Add(message);
        }
        return this;
    }

    public Check NotBlank(string? value, string field, string? label = null)
        => Require(!string.IsNullOrWhiteSpace(value), field, $"{label ?? field} is required.");

    public Check MaxLength(string? value, int max, string field)
        => Require(value is null || value.Length <= max, field, $"{field} must be at most {max} characters.");

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
            throw new ValidationException(_errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));
    }
}
