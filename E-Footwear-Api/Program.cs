using System.Threading.RateLimiting;
using E_Footwear_Api.Authentication;
using E_Footwear_Api.Cli;
using E_Footwear_Api.Middleware;
using EFootwear.DAL;
using Microsoft.AspNetCore.Authentication;

namespace E_Footwear_Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Operator commands run without starting the web server, e.g.:
            //   dotnet run --project E-Footwear-Api -- create-platform-admin <username> "<full name>"
            if (args.Length > 0 && args[0] == "create-platform-admin")
            {
                Environment.Exit(PlatformAdminCli.RunAsync(args, builder.Configuration).GetAwaiter().GetResult());
                return;
            }

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
                o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
            });

            var app = builder.Build();

            // Request pipeline, in order. Order matters:
            //  1. exception handler  - turns any error into a clean JSON error response
            //  2. swagger (dev only)
            //  3. https redirect     - production only; the local frontend talks plain HTTP
            //  4. rate limiter       - limits login attempts
            //  5. authentication     - reads the token and fills RequestContext
            //  6. must-change-password guard - a temporary password allows only /auth endpoints
            //  7. authorization      - enforces [Authorize]
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
            app.UseMiddleware<MustChangePasswordGuard>();
            app.UseAuthorization();
            app.MapControllers();
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            app.Run();
        }
    }
}

// lets the test project start the API in memory
public partial class Program;
