using System.Text;
using System.Threading.RateLimiting;
using Dapper;
using E_Footwear_Api.Infrastructure;
using E_Footwear_Api.Modules.Auth;
using E_Footwear_Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace E_Footwear_Api
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // ---- helper: dotnet run -- hash-password  (password from PLATFORM_ADMIN_PASSWORD; no database needed)
            // Prints an Argon2id hash for inserting a platform admin directly with SQL (docs/db/create-platform-admin.sql).
            if (args.Length == 1 && args[0] == "hash-password")
            {
                var pw = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD");
                if (string.IsNullOrEmpty(pw) || pw.Length < 12)
                    throw new InvalidOperationException("Set PLATFORM_ADMIN_PASSWORD (>= 12 chars) in the environment.");
                Console.WriteLine(PasswordHasher.Hash(pw));
                return;
            }

            var builder = WebApplication.CreateBuilder(args);

            var connString = builder.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");
            var dataSource = new NpgsqlDataSourceBuilder(connString).Build();
            builder.Services.AddSingleton(dataSource);
            builder.Services.AddSingleton<IDb, Db>();

            // ---- bootstrap command: dotnet run -- create-platform-admin <userName>  (password from PLATFORM_ADMIN_PASSWORD)
            if (args.Length == 2 && args[0] == "create-platform-admin")
            {
                await CreatePlatformAdmin(dataSource, args[1]);
                return;
            }

            builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
            builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
            var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
            if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
                throw new InvalidOperationException("Jwt:Key must be set (>= 32 bytes) from environment or a secret store.");

            builder.Services.AddSingleton<TokenService>();
            builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
            builder.Services.AddAuthorization();

            builder.Services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = 429;
                o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("Security:LoginPermitsPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
            });

            var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
            builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            {
                if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
            }));

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            var app = builder.Build();

            app.UseMiddleware<ErrorMiddleware>();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.UseSwaggerUI(o =>
                {
                    o.SwaggerEndpoint("/openapi/v1.json", "E-Footwear API v1");
                    o.RoutePrefix = "swagger";
                });
            }
            else
            {
                app.UseHttpsRedirection();
            }

            app.UseCors();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.MapGet("/health/db", async (NpgsqlDataSource db) =>
            {
                await using var conn = await db.OpenConnectionAsync();
                var tables = await conn.ExecuteScalarAsync<long>(
                    "select count(*) from information_schema.tables where table_schema in ('sales','inventory','accounting','security','core')");
                return Results.Ok(new { status = "ok", tables });
            });

            app.Run();
        }

        private static async Task CreatePlatformAdmin(NpgsqlDataSource ds, string userName)
        {
            var pw = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD");
            if (string.IsNullOrEmpty(pw) || pw.Length < 12)
                throw new InvalidOperationException("Set PLATFORM_ADMIN_PASSWORD (>= 12 chars) in the environment.");
            await using var conn = await ds.OpenConnectionAsync();
            await conn.ExecuteAsync("insert into platform.platform_users(user_name,password_hash) values (@u,@h)",
                new { u = userName, h = PasswordHasher.Hash(pw) });
            Console.WriteLine($"Platform admin '{userName}' created.");
        }
    }
}
