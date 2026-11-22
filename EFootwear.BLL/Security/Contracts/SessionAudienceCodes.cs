namespace EFootwear.BLL.Security.Contracts;

/// <summary>The two kinds of signed-in person. The frontend uses this to choose the area it shows.</summary>
public static class SessionAudienceCodes
{
    public const string PlatformAdministrator = "PLATFORM_ADMINISTRATOR";
    public const string CompanyUser = "COMPANY_USER";
}
