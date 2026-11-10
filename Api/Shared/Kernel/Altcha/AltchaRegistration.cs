using System.Security.Cryptography;
using Kernel.Endpoints;
using Kernel.Exceptions;
using Kernel.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Kernel.Altcha;

/// <summary>
/// Host wiring for the ALTCHA bot protection on the anonymous applicant endpoints (ADR-022): the services, the
/// challenge endpoint the widget fetches, and <see cref="RequireAltcha{TBuilder}"/> for the endpoints it guards.
/// </summary>
public static class AltchaRegistration
{
    /// <summary>The request header carrying the widget's solution.</summary>
    public const string HeaderName = "X-Altcha";

    /// <summary>The Slovak message a missing, wrong, expired or already used solution is answered with.</summary>
    public const string FailedMessage =
        "Overenie, že formulár neodosiela robot, sa nepodarilo. Skúste formulár odoslať znova.";

    /// <summary>
    /// Registers the options, the verifier, the PostgreSQL store of spent solutions and its daily retention pass.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configureDb">
    /// Configures the store's provider and connection, as for the idempotency store: the host supplies it, so the Kernel
    /// stays provider-free. The store's SQL is PostgreSQL.
    /// </param>
    /// <param name="pruneAt">When the daily retention pass runs (UTC).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAltcha(
        this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configureDb, TimeOnly pruneAt)
    {
        services.AddOptions<AltchaOptions>()
            .BindConfiguration(AltchaOptions.SectionName)
            .PostConfigure<IHostEnvironment>(static (options, environment) =>
            {
                // A key of the process's own, so Development runs without configuration; nothing outlives the process.
                if (environment.IsDevelopment() && string.IsNullOrEmpty(options.HmacKey))
                    options.HmacKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            })
            .Validate(static options => options.HmacKey is { Length: >= AltchaOptions.MinKeyLength },
                $"{AltchaOptions.SectionName}:HmacKey must be set to at least {AltchaOptions.MinKeyLength} characters outside Development.")
            .Validate(static options => options.MaxNumber > 0 && options.ChallengeLifetime > TimeSpan.Zero,
                $"{AltchaOptions.SectionName}:MaxNumber and {AltchaOptions.SectionName}:ChallengeLifetime must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextPool<AltchaDbContext>(configureDb);
        services.AddScoped<IAltchaSolutionStore, DbAltchaSolutionStore>();
        services.AddScoped<AltchaVerifier>();
        services.AddDailyWorker<AltchaSolutionPruner>(pruneAt);
        return services;
    }

    /// <summary>
    /// Maps <c>GET /altcha/challenge</c>, the widget's <c>challengeurl</c>: a fresh challenge per call, never cached.
    /// </summary>
    /// <param name="api">The versioned API group.</param>
    /// <returns>The builder for the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapAltchaChallenge(this IEndpointRouteBuilder api) =>
        api.MapGet("/altcha/challenge", static (HttpContext context, AltchaVerifier altcha) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(altcha.CreateChallenge());
            })
            .WithTags("altcha")
            .WithName("GetAltchaChallenge")
            .Produces<AltchaChallenge>(StatusCodes.Status200OK);

    /// <summary>
    /// Requires a fresh ALTCHA solution in <see cref="HeaderName"/> before the endpoint runs, and documents the header.
    /// A missing, invalid, expired or replayed solution is answered <c>400</c> with <see cref="FailedMessage"/>.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// The check runs after the idempotency middleware, so a retry replaying a stored response is not asked for the
    /// solution it already spent. A solution that passes is spent even when the endpoint then refuses the request, so the
    /// client solves a new challenge for every submit.
    /// </remarks>
    public static TBuilder RequireAltcha<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(static async (context, next) =>
        {
            HttpContext http = context.HttpContext;
            AltchaVerifier altcha = http.RequestServices.GetRequiredService<AltchaVerifier>();
            if (!await altcha.VerifyAsync(http.Request.Headers[HeaderName], http.RequestAborted))
                throw new BadRequestException(FailedMessage);
            return await next(context);
        });
        return builder.RequiresHeader(HeaderName,
            "The ALTCHA widget's solution (base64 JSON) to a challenge from GET /altcha/challenge. Accepted once.");
    }
}
