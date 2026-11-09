using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Tests.Shared;

/// <summary>Reads the plan Postgres would run for an EF Core query, to pin a query to the indexes the schema gives it.</summary>
internal static partial class QueryPlans
{
    /// <summary>
    /// The plan of the SQL EF Core sends for <paramref name="query"/>, with sequential scans priced out: an empty table
    /// is cheaper to scan, so the plan otherwise says nothing about whether an index <em>can</em> serve the query.
    /// </summary>
    public static async Task<string> ExplainAsync<T>(DbContext db, IQueryable<T> query, CancellationToken ct)
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
