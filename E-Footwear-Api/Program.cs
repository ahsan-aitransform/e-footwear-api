using System.Threading.RateLimiting;
using E_Footwear_Api.Authentication;
using E_Footwear_Api.Cli;
using E_Footwear_Api.Middleware;
using EFootwear.DAL;
using Microsoft.AspNetCore.Authentication;
using Serilog;

namespace E_Footwear_Api
{
    public class Program
    {
        public static int Main(string[] args)
        {
            // Operator commands run without starting the web server, e.g.:
            //   dotnet run --project E-Footwear-Api -- create-platform-admin <username> "<full name>"
            if (args.Length > 0 && args[0] == "create-platform-admin")
                return PlatformAdminCli.RunAsync(args, WebApplication.CreateBuilder(args).Configuration).GetAwaiter().GetResult();

            // A minimal logger first, so a failure while building the app (bad config, no database) is still logged.
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
            try
            {
                Run(args);
                return 0;
            }
            catch (Exception ex) when (ex is not HostAbortedException)
            {
                Log.Fatal(ex, "The API stopped because of an unhandled error");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();   // writes any buffered log lines to the file
            }
        }

        private static void Run(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Logging: levels, console and rolling file are configured in appsettings ("Serilog" section).
            // Every log line carries the correlation id and, once signed in, the company and user ids.
            builder.Host.UseSerilog((ctx, services, cfg) => cfg
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "E-Footwear-Api")
                .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName));

            builder.Services.AddControllers();
            builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecurityTransformer>());
            builder.Services.AddDal(builder.Configuration);

            builder.Services.AddAuthentication(SessionAuth.Scheme)
                .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuth.Scheme, null);
            builder.Services.AddAuthorization();

            builder.Services.AddExceptionHandler<ApiExceptionHandler>();
            builder.Services.AddProblemDetails();

            // Slows password guessing: 10 login attempts per minute per client address.
            builder.Services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                o.OnRejected = (context, _) =>
                {
                    Log.Warning("Rate limit hit on {Path} from {Ip}", context.HttpContext.Request.Path,
                                context.HttpContext.Connection.RemoteIpAddress);
                    return ValueTask.CompletedTask;
                };
                o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
            });

            var app = builder.Build();

            // Request pipeline, in order. Order matters:
            //  1. correlation id     - first, so every later log line carries it
            //  2. request logging    - one summary line per request (method, path, status, time, caller ids)
            //  3. exception handler  - turns any error into a clean JSON error response (and logs it)
            //  4. swagger (dev only)
            //  5. https redirect     - production only; the local frontend talks plain HTTP
            //  6. rate limiter       - limits login attempts
            //  7. authentication     - reads the token and fills RequestContext
            //  8. log enrichment     - adds company / user ids to the log lines that follow
            //  9. must-change-password guard - a temporary password allows only /auth endpoints
            // 10. authorization      - enforces [Authorize]
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseSerilogRequestLogging(o =>
            {
                o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms";
                // Health checks and API docs are noise: log them only at the lowest level.
                o.GetLevel = (http, _, ex) =>
                    ex is not null || http.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
                    : http.Request.Path.StartsWithSegments("/health") || http.Request.Path.StartsWithSegments("/openapi")
                        || http.Request.Path.StartsWithSegments("/swagger") ? Serilog.Events.LogEventLevel.Verbose
                    : Serilog.Events.LogEventLevel.Information;
                o.EnrichDiagnosticContext = (diag, http) =>
                    diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
            });
            app.UseExceptionHandler();

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

            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseMiddleware<LogUserEnrichmentMiddleware>();
            app.UseMiddleware<MustChangePasswordGuard>();
            app.UseAuthorization();
            app.MapControllers();
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            Log.Information("Starting E-Footwear API in {Environment}", app.Environment.EnvironmentName);
            app.Run();
        }
    }
}

// lets the test project start the API in memory
public partial class Program;
