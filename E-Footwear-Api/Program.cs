using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EFootwear.Api;
using EFootwear.Api.Security;
using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

// Operator commands (no web host needed):  dotnet run -- migrate | create-company …   (see Cli.Usage)
if (args.Length > 0 && args[0] is "migrate" or "create-company")
    return await Cli.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

var security = cfg.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions();
builder.Services.AddInfrastructure(
    cfg.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not set."), security);

builder.Services.AddHostedService<EFootwear.Api.Workers.PriceRefreshWorker>();
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());
builder.Services.AddAuthentication(SessionDefaults.Scheme).AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(SessionDefaults.Scheme, null);
builder.Services.AddAuthorization();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DictionaryKeyPolicy = null;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();

// Login is the brute-force target: limit it per client address on top of the database's per-user lockout.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("Security:LoginRateLimitPerMinute", 20), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

if (cfg.GetValue("Security:TrustForwardedHeaders", false))
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });

var app = builder.Build();

if (cfg.GetValue("Security:TrustForwardedHeaders", false)) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => { o.SwaggerEndpoint("/openapi/v1.json", "E-Footwear API v1"); o.RoutePrefix = "swagger"; });
}
else
{
    app.UseHsts();
}

if (!cfg.GetValue("Security:DisableHttpsRedirect", false)) app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapGet("/health/db", async (Npgsql.NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    await using var cmd = new Npgsql.NpgsqlCommand("select count(*) from sec.permissions", conn);
    return Results.Ok(new { database = "ok", permissions = (long)(await cmd.ExecuteScalarAsync())! });
});

app.Run();
return 0;

public partial class Program;
