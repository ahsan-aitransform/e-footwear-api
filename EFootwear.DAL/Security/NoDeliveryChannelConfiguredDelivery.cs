using EFootwear.BLL.Common;
using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using Microsoft.Extensions.Logging;

namespace EFootwear.DAL.Security;

/// <summary>
/// The password reset delivery used until the business approves a channel (email or SMS: [BUSINESS DECISION REQUIRED]). It delivers
/// nothing and says so in the log, without the token. Administrators issue reset tokens by hand in the meantime.
/// </summary>
public sealed class NoDeliveryChannelConfiguredDelivery(ILogger<NoDeliveryChannelConfiguredDelivery> logger) : IPasswordResetTokenDelivery
{
    public Task DeliverAsync(PasswordResetDelivery delivery, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "A password reset token was created for {Email} in company {CompanyId} but no delivery channel is configured, so nothing was sent",
            LogMask.Email(delivery.Email), delivery.CompanyId);
        return Task.CompletedTask;
    }
}
