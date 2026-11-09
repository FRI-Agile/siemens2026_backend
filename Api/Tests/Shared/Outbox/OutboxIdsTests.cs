using Kernel.Outbox;
using Npgsql;
using Tests.Shared;

namespace Tests.Shared.Outbox;

/// <summary>Outbox ids sort in the order they were issued, in .NET and in PostgreSQL alike.</summary>
public class OutboxIdsTests
{
    [Fact]
    public void Ids_issued_in_one_millisecond_still_increase()
    {
        Guid[] ids = [.. Enumerable.Range(0, 10_000).Select(_ => OutboxIds.Next())];

        Assert.Equal(ids, ids.Order());
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal(7, id.Version));
    }

    [Fact]
    public async Task PostgreSQL_orders_them_the_same_way()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Guid[] ids = [.. Enumerable.Range(0, 500).Select(_ => OutboxIds.Next())];

        await using NpgsqlCommand command = PostgresTestHost.Current.DataSource.CreateCommand(
            "SELECT id FROM unnest(@ids) AS t(id) ORDER BY id");
        command.Parameters.AddWithValue("ids", ids.Reverse().ToArray());
        List<Guid> sorted = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            sorted.Add(reader.GetGuid(0));

        Assert.Equal(ids, sorted);
    }
}
