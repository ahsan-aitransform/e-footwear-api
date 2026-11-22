using System.Threading.RateLimiting;
using E_Footwear_Api.Authentication;
using E_Footwear_Api.Authorization;
using E_Footwear_Api.Cli;
using E_Footwear_Api.Constants;
using E_Footwear_Api.Middleware;
using E_Footwear_Api.Routing;
using EFootwear.DAL;
using EFootwear.Domain.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Serilog;

namespace E_Footwear_Api
{
    public class Program
    {
        private const string ApplicationName = "E-Footwear-Api";
        private const string SwaggerDocumentTitle = "E-Footwear API v1";

        public static int Main(string[] arguments)
        {
            // Operator commands run without starting the web server, e.g.:
            //   dotnet run --project E-Footwear-Api -- create-platform-admin <username> "<full name>"
            //   dotnet run --project E-Footwear-Api -- migrate
            //   dotnet run --project E-Footwear-Api -- generate-permission-codes
            if (CommandLineCommands.IsCommand(arguments))
                return CommandLineCommands.RunAsync(arguments).GetAwaiter().GetResult();

            // A minimal logger first, so a failure while building the app (bad config, no database) is still logged.
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
            try
            {
                Run(arguments);
                return 0;
            }
            catch (Exception exception) when (exception is not HostAbortedException)
            {
                Log.Fatal(exception, "The API stopped because of an unhandled error");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();   // writes any buffered log lines to the file
            }
        }

        private static void Run(string[] arguments)
        {
            var builder = WebApplication.CreateBuilder(arguments);

            // Logging: levels, console and rolling file are configured in appsettings ("Serilog" section).
            // Every log line carries the correlation id and, once signed in, the company and user ids.
            builder.Host.UseSerilog((hostContext, services, loggerConfiguration) => loggerConfiguration
                .ReadFrom.Configuration(hostContext.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", ApplicationName)
                .Enrich.WithProperty("Environment", hostContext.HostingEnvironment.EnvironmentName));

            builder.Services.AddControllers();
            builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());
            builder.Services.AddDal(builder.Configuration);

            builder.Services.AddAuthentication(SessionAuthenticationDefaults.Scheme)
                .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationDefaults.Scheme, null);
            builder.Services.AddAuthorizationBuilder()
                .AddPolicy(AuthorizationPolicies.PlatformAdministratorOnly, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(RoleCodes.PlatformAdmin))
                .AddPolicy(AuthorizationPolicies.CompanyUserOnly, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(SessionAuthenticationDefaults.CompanyIdClaimType));
            builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationMiddlewareResultHandler>();

            builder.Services.AddExceptionHandler<ApiExceptionHandler>();
            builder.Services.AddProblemDetails();

            var loginAttemptsPerWindow = builder.Configuration.GetValue(
                LoginRateLimit.AttemptsPerWindowConfigurationKey, LoginRateLimit.AttemptsPerWindow);
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, _) =>
                {
                    Log.Warning("Rate limit hit on {Path} from {ClientIpAddress}", context.HttpContext.Request.Path,
                                context.HttpContext.Connection.RemoteIpAddress);
                    return ValueTask.CompletedTask;
                };
                options.AddPolicy(LoginRateLimit.PolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? LoginRateLimit.UnknownClientAddress,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = loginAttemptsPerWindow,
                        Window = LoginRateLimit.Window,
                    }));
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
            // 10. authorization      - enforces [Authorize] and the policies
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms";
                // Health checks and API docs are noise: log them only at the lowest level.
                options.GetLevel = (httpContext, _, exception) =>
                    exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
                        ? Serilog.Events.LogEventLevel.Error
                    : httpContext.Request.Path.StartsWithSegments(ApiRoutes.HealthCheck)
                        || httpContext.Request.Path.StartsWithSegments("/openapi")
                        || httpContext.Request.Path.StartsWithSegments("/" + ApiRoutes.SwaggerRoutePrefix)
                        ? Serilog.Events.LogEventLevel.Verbose
                    : Serilog.Events.LogEventLevel.Information;
                options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                    diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
            });
            app.UseExceptionHandler();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint(ApiRoutes.OpenApiDocument, SwaggerDocumentTitle);
                    options.RoutePrefix = ApiRoutes.SwaggerRoutePrefix;
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
            app.MapGet(ApiRoutes.HealthCheck, () => Results.Ok(new { status = "ok" }));

            Log.Information("Starting {Application} in {Environment}", ApplicationName, app.Environment.EnvironmentName);
            app.Run();
        }
    }
}

// lets the test project start the API in memory
public partial class Program;
