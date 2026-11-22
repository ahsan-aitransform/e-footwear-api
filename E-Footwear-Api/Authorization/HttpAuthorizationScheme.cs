namespace E_Footwear_Api.Authorization;

/// <summary>The HTTP "Authorization" header scheme the client uses: <c>Authorization: Bearer {accessToken}</c>.</summary>
public static class HttpAuthorizationScheme
{
    public const string Bearer = "Bearer";
    public const string BearerPrefix = Bearer + " ";
}
