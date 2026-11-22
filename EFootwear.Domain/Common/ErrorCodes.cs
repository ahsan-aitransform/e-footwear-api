namespace EFootwear.Domain.Common;

/// <summary>
/// Machine-readable error codes. They travel to the client in the "errorCode" field of every error response
/// (RFC 7807 problem details), so the frontend can react to a code instead of parsing a message.
/// Add a constant here before using a new code anywhere.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string Locked = "LOCKED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string DatabasePermissionDenied = "DATABASE_PERMISSION_DENIED";
    public const string DatabaseUniqueViolation = "DATABASE_UNIQUE_VIOLATION";
    public const string DatabaseForeignKeyViolation = "DATABASE_FOREIGN_KEY_VIOLATION";
    public const string DatabaseRuleViolation = "DATABASE_RULE_VIOLATION";
    public const string ConcurrentUpdate = "CONCURRENT_UPDATE";

    /// <summary>The record cannot be deleted because other records still use it (the message says where).</summary>
    public const string RecordInUse = "RECORD_IN_USE";
    public const string PlanLimitReached = "PLAN_LIMIT_REACHED";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string UnsupportedFileType = "UNSUPPORTED_FILE_TYPE";
    public const string DocumentNumberWouldRepeat = "DOCUMENT_NUMBER_WOULD_REPEAT";
    public const string InternalServerError = "INTERNAL_SERVER_ERROR";

    /// <summary>The default code for an error kind, used when the thrower does not name a more specific one.</summary>
    public static string ForKind(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => ValidationFailed,
        ErrorKind.Unauthorized => Unauthorized,
        ErrorKind.Forbidden => Forbidden,
        ErrorKind.NotFound => NotFound,
        ErrorKind.Conflict => Conflict,
        ErrorKind.Locked => Locked,
        _ => ValidationFailed,
    };
}
