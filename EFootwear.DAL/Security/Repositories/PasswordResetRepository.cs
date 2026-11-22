using EFootwear.BLL.Security.Abstractions;
using EFootwear.BLL.Security.Contracts;
using EFootwear.DAL.Persistence;
using EFootwear.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgresErrors = Npgsql.PostgresErrorCodes;

namespace EFootwear.DAL.Security.Repositories;

/// <summary>
/// DAL for password reset tokens. The rules (single use, expiry, hourly limit, ending all sessions) live in the database functions
/// sec.request_password_reset and sec.complete_password_reset; only the SHA-256 hash of a token is stored.
/// </summary>
public sealed class PasswordResetRepository(DatabaseSession databaseSession) : IPasswordResetRepository
{
    public async Task<IssuedPasswordReset?> IssueTokenAsync(Guid companyId, string username, string? clientIpAddress,
                                                            CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var rows = await databaseContext.Database.SqlQuery<IssuedTokenRow>($"""
            select out_user_id as "UserId", out_reset_token as "TokenSecret", out_delivery_phone as "DeliveryPhoneNumber"
              from sec.request_password_reset({companyId}, {username}, {clientIpAddress}::inet)
            """).ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
        return row is null ? null : new IssuedPasswordReset(row.UserId, row.TokenSecret, row.DeliveryPhoneNumber);
    }

    public async Task<int> GetTokenLifetimeMinutesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        return await databaseContext.Database
            .SqlQuery<int>($"select reset_token_minutes as \"Value\" from sec.auth_policy where company_id = {companyId}")
            .SingleAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid companyId, string tokenSecret, string newPasswordHash, CancellationToken cancellationToken)
    {
        databaseSession.UseCompany(companyId);
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        try
        {
            await databaseContext.Database
                .SqlQuery<Guid>($"select sec.complete_password_reset({companyId}, {tokenSecret}, {newPasswordHash}) as \"Value\"")
                .SingleAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrors.InvalidAuthorizationSpecification)
        {
            throw new DomainException(ErrorKind.Unauthorized, "The reset token is invalid, used or expired.");
        }
    }

    private sealed class IssuedTokenRow
    {
        public Guid UserId { get; set; }
        public string TokenSecret { get; set; } = "";
        public string? DeliveryPhoneNumber { get; set; }
    }
}
