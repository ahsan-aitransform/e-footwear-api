using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Contacts.Abstractions;
using EFootwear.DAL.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Contacts.Repositories;

/// <summary>Allocates customer, vendor and staff codes with the database function contacts.next_code (one counter per company).</summary>
public sealed class ContactCodeGenerator(DatabaseSession databaseSession, ICurrentUser currentUser) : IContactCodeGenerator
{
    public async Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken)
    {
        var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
        var companyId = currentUser.CompanyId;
        return await databaseContext.Database.SqlQuery<string>($"select contacts.next_code({companyId}) as \"Value\"").SingleAsync(cancellationToken);
    }
}
