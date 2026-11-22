namespace E_Footwear_Api.Constants;

/// <summary>Slows password guessing: a limited number of sign-in attempts per client address per window.</summary>
public static class LoginRateLimit
{
    public const string PolicyName = "login";
    public const int AttemptsPerWindow = 10;

    /// <summary>Configuration key that overrides <see cref="AttemptsPerWindow"/> (the integration tests raise it).</summary>
    public const string AttemptsPerWindowConfigurationKey = "RateLimiting:LoginAttemptsPerWindow";
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public const string UnknownClientAddress = "unknown";
}
