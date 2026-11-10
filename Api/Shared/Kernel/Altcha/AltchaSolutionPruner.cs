using Kernel.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kernel.Altcha;

/// <summary>
/// Deletes spent solutions whose challenge has expired: an expired challenge is refused on its expiry alone, so its row
/// no longer guards anything.
/// </summary>
/// <param name="db">The context (a fresh pooled instance per run).</param>
/// <param name="logger">Logger for the pruned-row count.</param>
public sealed partial class AltchaSolutionPruner(AltchaDbContext db, ILogger<AltchaSolutionPruner> logger) : IScheduledWorker
{
    /// <summary>A cheap range delete on <c>idx_altcha_solution_expires_at</c>.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        int removed = await db.Database.ExecuteSqlAsync(
            $"DELETE FROM kernel.altcha_solution WHERE expires_at <= now()", ct);

        if (removed > 0)
            LogPruned(logger, removed);

        return removed;
    }

    [LoggerMessage(EventId = 3220, EventName = "altcha.solution.pruned", Level = LogLevel.Information,
        Message = "Pruned {Count} expired ALTCHA solution(s).")]
    private static partial void LogPruned(ILogger logger, int count);
}
