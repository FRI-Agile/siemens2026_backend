using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Infrastructure;

/// <summary>The lists' search can use the trigram indexes the schema gives it, rather than scanning every post.</summary>
public partial class JobPostQueryExtensionsTests
{
    [Fact]
    public async Task The_search_the_lists_send_is_served_by_both_trigram_indexes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        IQueryable<JobPost> search = db.JobPosts.MatchingSearch("elektr").NewestFirst();

        string plan = await ExplainAsync(db, search, ct);

        Assert.Contains("idx_job_post_title", plan);
        Assert.Contains("idx_job_post_location", plan);
        Assert.DoesNotContain("Seq Scan", plan);
    }

    /// <summary>
    /// The plan of the SQL EF Core sends for <paramref name="query"/>, with sequential scans priced out: an empty table
    /// is cheaper to scan, so the plan otherwise says nothing about whether an index <em>can</em> serve the query.
    /// </summary>
    private static async Task<string> ExplainAsync(DbContext db, IQueryable<JobPost> query, CancellationToken ct)
    {
        // ToQueryString states each parameter as a leading `-- @name='value'` comment line.
        string[] lines = query.ToQueryString().Split('\n');
        DbConnection connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using (NpgsqlCommand off = new("SET enable_seqscan = off", (NpgsqlConnection)connection))
            await off.ExecuteNonQueryAsync(ct);

        await using NpgsqlCommand explain = new(
            "EXPLAIN " + string.Join('\n', lines.Where(l => !l.StartsWith("-- @", StringComparison.Ordinal))),
            (NpgsqlConnection)connection);
        foreach (Match parameter in lines.Select(l => ParameterLine().Match(l)).Where(m => m.Success))
            explain.Parameters.AddWithValue(parameter.Groups["name"].Value, parameter.Groups["value"].Value);

        List<string> plan = [];
        await using NpgsqlDataReader reader = await explain.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            plan.Add(reader.GetString(0));
        return string.Join('\n', plan);
    }

    [GeneratedRegex("^-- @(?<name>\\w+)='(?<value>.*)'(?: \\(DbType = \\w+\\))?\\r?$")]
    private static partial Regex ParameterLine();
}
