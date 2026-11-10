using System.Text;
using System.Text.Json;
using Kernel.Altcha;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.FilesModule;

namespace Tests.Shared.Altcha;

/// <summary>
/// ALTCHA challenges (ADR-022): a solution to a challenge this API signed is accepted once, before it expires; anything
/// tampered with, foreign, expired or replayed is refused. Spent solutions are kept in <c>kernel.altcha_solution</c>.
/// </summary>
public class AltchaVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static AltchaVerifier Verifier(AltchaDbContext db, TimeProvider time, string key = "test-key-that-is-at-least-32-chars") =>
        new(Options.Create(new AltchaOptions { HmacKey = key, MaxNumber = 1000 }), new DbAltchaSolutionStore(db), time);

    /// <summary>The widget's payload for <paramref name="challenge"/>, answering <paramref name="number"/>.</summary>
    internal static string Payload(AltchaChallenge challenge, long number, string? salt = null) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            algorithm = challenge.Algorithm,
            challenge = challenge.Challenge,
            number,
            salt = salt ?? challenge.Salt,
            signature = challenge.Signature,
            took = 412,
        })));

    [Fact]
    public async Task A_solution_is_accepted_once()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using AltchaDbContext db = TestDb.NewAltchaContext();
        AltchaVerifier altcha = Verifier(db, new FixedTime(Now));
        AltchaChallenge challenge = altcha.CreateChallenge(417);

        Assert.True(await altcha.VerifyAsync(Payload(challenge, 417), ct));
        Assert.False(await altcha.VerifyAsync(Payload(challenge, 417), ct));
        // The same signed string split differently is the same challenge, already spent.
        Assert.False(await altcha.VerifyAsync(Payload(challenge, 17, challenge.Salt + "4"), ct));
    }

    [Fact]
    public async Task A_challenge_carries_what_the_widget_reads()
    {
        await using AltchaDbContext db = TestDb.NewAltchaContext();

        AltchaChallenge challenge = Verifier(db, new FixedTime(Now)).CreateChallenge();

        Assert.Equal(AltchaVerifier.Algorithm, challenge.Algorithm);
        Assert.Equal(1000, challenge.MaxNumber);
        Assert.EndsWith($"?expires={(Now + TimeSpan.FromMinutes(20)).ToUnixTimeSeconds()}&", challenge.Salt);
        Assert.Matches("^[0-9a-f]{64}$", challenge.Challenge);
        Assert.Matches("^[0-9a-f]{64}$", challenge.Signature);
        Assert.Contains("\"maxnumber\":1000", JsonSerializer.Serialize(challenge));
    }

    [Fact]
    public async Task A_wrong_number_is_refused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using AltchaDbContext db = TestDb.NewAltchaContext();
        AltchaVerifier altcha = Verifier(db, new FixedTime(Now));

        Assert.False(await altcha.VerifyAsync(Payload(altcha.CreateChallenge(417), 418), ct));
        Assert.False(await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM kernel.altcha_solution").AnyAsync(ct));
    }

    [Fact]
    public async Task A_challenge_signed_with_another_key_is_refused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using AltchaDbContext db = TestDb.NewAltchaContext();
        AltchaChallenge foreign = Verifier(db, new FixedTime(Now), "another-key-that-is-at-least-32-chars").CreateChallenge(5);

        Assert.False(await Verifier(db, new FixedTime(Now)).VerifyAsync(Payload(foreign, 5), ct));
    }

    [Fact]
    public async Task An_expired_challenge_is_refused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using AltchaDbContext db = TestDb.NewAltchaContext();
        FixedTime time = new(Now);
        AltchaVerifier altcha = Verifier(db, time);
        AltchaChallenge challenge = altcha.CreateChallenge(5);

        time.Now = Now + TimeSpan.FromMinutes(21);

        Assert.False(await altcha.VerifyAsync(Payload(challenge, 5), ct));
    }

    [Fact]
    public async Task Digits_moved_from_the_number_into_the_salt_do_not_extend_the_expiry()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using AltchaDbContext db = TestDb.NewAltchaContext();
        FixedTime time = new(Now);
        AltchaVerifier altcha = Verifier(db, time);
        AltchaChallenge challenge = altcha.CreateChallenge(417);
        time.Now = Now + TimeSpan.FromMinutes(21);

        // salt + "417" is still the signed string, but the '&' keeps "4" out of the expires value.
        Assert.False(await altcha.VerifyAsync(Payload(challenge, 17, challenge.Salt + "4"), ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("bm90IGpzb24=")] // "not json"
    [InlineData("e30=")] // "{}"
    public async Task A_malformed_payload_is_refused(string? payload)
    {
        await using AltchaDbContext db = TestDb.NewAltchaContext();

        Assert.False(await Verifier(db, new FixedTime(Now)).VerifyAsync(payload, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_pruner_deletes_only_expired_solutions()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using AltchaDbContext db = TestDb.NewAltchaContext();
        DbAltchaSolutionStore store = new(db);
        await store.TrySpendAsync("expired", DateTimeOffset.UtcNow.AddMinutes(-1), ct);
        await store.TrySpendAsync("live", DateTimeOffset.UtcNow.AddMinutes(10), ct);

        int removed = await new AltchaSolutionPruner(db, NullLogger<AltchaSolutionPruner>.Instance).RunAsync(ct);

        Assert.Equal(1, removed);
        Assert.False(await store.TrySpendAsync("live", DateTimeOffset.UtcNow.AddMinutes(10), ct));
        Assert.True(await store.TrySpendAsync("expired", DateTimeOffset.UtcNow.AddMinutes(10), ct));
    }
}
