using Dapper;
using Npgsql;

namespace E_Footwear_Api.Infrastructure;

public static class Audit
{
    /// <summary>Append a row to audit.activity_log inside the caller's transaction (Part 29).</summary>
    public static Task Log(NpgsqlConnection c, NpgsqlTransaction t, long? companyId, long? userId, string? userName,
        string activity, string? entity = null, long? entityId = null, string? remarks = null, string? ip = null) =>
        c.ExecuteAsync(
            "insert into audit.activity_log(company_id,user_id,user_name,activity,entity_name,entity_id,remarks,ip_address) " +
            "values (@companyId,@userId,@userName,@activity,@entity,@entityId,@remarks,@ip::inet)",
            new { companyId, userId, userName, activity, entity, entityId, remarks, ip }, t);
}
