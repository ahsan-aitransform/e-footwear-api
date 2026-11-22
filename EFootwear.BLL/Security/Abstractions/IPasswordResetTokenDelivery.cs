using EFootwear.BLL.Security.Contracts;

namespace EFootwear.BLL.Security.Abstractions;

/// <summary>
/// How a password reset token gets to a user who asked for one by themselves (email, SMS ...). The channel is
/// [BUSINESS DECISION REQUIRED]: nothing is invented here. The default implementation delivers nothing and says so in the log;
/// administrators issue tokens by hand until a channel is approved.
/// </summary>
public interface IPasswordResetTokenDelivery
{
    Task DeliverAsync(PasswordResetDelivery delivery, CancellationToken cancellationToken);
}
