using Microsoft.EntityFrameworkCore;

namespace Kernel.Altcha;

/// <summary>Remembers which challenges have been solved, so each solution is accepted only once (ADR-022).</summary>
public interface IAltchaSolutionStore
{
    /// <summary>Marks <paramref name="challenge"/> spent, unless it already is.</summary>
    /// <param name="challenge">The challenge hash the solution answers.</param>
    /// <param name="expiresAt">When the challenge expires; the record is kept until then.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns><c>true</c> if this call spent it; <c>false</c> if it was spent before.</returns>
    Task<bool> TrySpendAsync(string challenge, DateTimeOffset expiresAt, CancellationToken ct = default);
}

/// <summary>
/// The context over <c>kernel.altcha_solution</c>. It maps no entity: the store and the pruner are one statement each.
/// The table is created by <c>configs/postgres/init/01-create-db.sql</c>, not by this context (ADR-D002).
/// </summary>
/// <param name="options">The context options (connection, retry policy) supplied by the host.</param>
public sealed class AltchaDbContext(DbContextOptions<AltchaDbContext> options) : DbContext(options);

/// <summary>
/// The PostgreSQL-backed <see cref="IAltchaSolutionStore"/>. Spent solutions survive a restart and are shared by every
/// instance; in memory, a deployment would let every solution of the last <see cref="AltchaOptions.ChallengeLifetime"/>
/// be replayed.
/// </summary>
/// <param name="db">The context, resolved per request.</param>
internal sealed class DbAltchaSolutionStore(AltchaDbContext db) : IAltchaSolutionStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The insert is the check: two requests carrying the same solution race on the primary key, and exactly one gets a
    /// row back. A retry after a lost acknowledgement finds its own row and is refused, which only costs the client a
    /// fresh solution.
    /// </remarks>
    public async Task<bool> TrySpendAsync(string challenge, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        List<bool> spent = await db.Database.SqlQuery<bool>(
            $"""
             INSERT INTO kernel.altcha_solution (challenge, expires_at)
             VALUES ({challenge}, {expiresAt})
             ON CONFLICT (challenge) DO NOTHING
             RETURNING true AS "Value"
             """).ToListAsync(ct);
        return spent.Count > 0;
    }
}
