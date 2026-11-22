namespace EFootwear.Domain.Common;

/// <summary>The category of a business-rule failure. The API turns each kind into one HTTP status code.</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    Locked,
}
