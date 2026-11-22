namespace EFootwear.Domain.Common;

/// <summary>
/// A business-rule failure that the API turns into a 4xx response.
/// <paramref name="errorCode"/> is optional: when it is omitted the default code of the kind is used
/// (see <see cref="ErrorCodes.ForKind"/>).
/// </summary>
public sealed class DomainException(ErrorKind kind, string message, string? errorCode = null) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;

    public string ErrorCode { get; } = errorCode ?? ErrorCodes.ForKind(kind);
}
