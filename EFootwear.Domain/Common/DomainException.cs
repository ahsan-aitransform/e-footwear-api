namespace EFootwear.Domain.Common;

public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Locked,
}

/// <summary>A business-rule failure that the API turns into a 4xx response.</summary>
public sealed class DomainException(ErrorKind kind, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;
}
