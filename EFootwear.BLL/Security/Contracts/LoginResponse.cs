namespace EFootwear.BLL.Security.Contracts;

/// <summary>
/// AccessToken looks like "{companyId}.{secret}". The client sends it back on every request
/// as "Authorization: Bearer &lt;AccessToken&gt;".
/// </summary>
public sealed record LoginResponse(string AccessToken, bool MustChangePassword);
