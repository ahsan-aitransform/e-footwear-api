using Dapper;
using Npgsql;

namespace E_Footwear_Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            var connString = builder.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");
            builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connString).Build());

            var app = builder.Build();

            // Swagger UI
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.UseSwaggerUI(o =>
                {
                    o.SwaggerEndpoint("/openapi/v1.json", "E-Footwear API v1");
                    o.RoutePrefix = "swagger";
                });
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            // Quick check that the app can reach the database.
            app.MapGet("/health/db", async (NpgsqlDataSource db) =>
            {
                await using var conn = await db.OpenConnectionAsync();
                var version = await conn.ExecuteScalarAsync<string>("select version()");
                var tables = await conn.ExecuteScalarAsync<long>(
                    "select count(*) from information_schema.tables where table_schema in ('sales','inv','acct')");
                return Results.Ok(new { version, tables });
            });

            app.Run();
        }
    }
}
