using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Kernel.Altcha;

/// <summary>
/// A proof-of-work challenge, in the shape the ALTCHA widget reads from its <c>challengeurl</c>: find the number in
/// <c>0..maxnumber</c> whose <c>SHA-256(salt + number)</c> is <c>challenge</c>.
/// </summary>
/// <param name="Algorithm">Always <c>SHA-256</c>.</param>
/// <param name="Challenge">The hex SHA-256 to find the number for.</param>
/// <param name="MaxNumber">The largest number to try.</param>
/// <param name="Salt">Random hex, then the challenge's expiry as <c>?expires=&lt;unix seconds&gt;&amp;</c>.</param>
/// <param name="Signature">The API's hex HMAC-SHA256 of <paramref name="Challenge"/>.</param>
public sealed record AltchaChallenge(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("challenge")] string Challenge,
    [property: JsonPropertyName("maxnumber")] int MaxNumber,
    [property: JsonPropertyName("salt")] string Salt,
    [property: JsonPropertyName("signature")] string Signature);

/// <summary>Issues ALTCHA challenges and accepts each solution once (ADR-022). Stateless apart from the store.</summary>
/// <remarks>
/// The ALTCHA v1 protocol, verified in-process with no third party. The signature covers the challenge hash only, so
/// everything else the client sends back is re-derived from it: the hash from the salt and number, the expiry from the
/// salt. The salt ends with <c>&amp;</c>, so the digits of the number cannot be shifted into the expiry to extend it.
/// </remarks>
public sealed class AltchaVerifier(
    IOptions<AltchaOptions> options, IAltchaSolutionStore store, TimeProvider time)
{
    /// <summary>The only algorithm issued or accepted.</summary>
    public const string Algorithm = "SHA-256";

    private readonly AltchaOptions _options = options.Value;

    /// <summary>A new challenge, valid for <see cref="AltchaOptions.ChallengeLifetime"/>.</summary>
    public AltchaChallenge CreateChallenge() =>
        CreateChallenge(RandomNumberGenerator.GetInt32(0, _options.MaxNumber + 1));

    /// <summary>A challenge hiding <paramref name="number"/>; for tests, which need to know the answer.</summary>
    internal AltchaChallenge CreateChallenge(int number)
    {
        long expires = (time.GetUtcNow() + _options.ChallengeLifetime).ToUnixTimeSeconds();
        string salt = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}?expires={expires}&";
        string challenge = Sha256Hex(salt + number.ToString(CultureInfo.InvariantCulture));
        return new AltchaChallenge(Algorithm, challenge, _options.MaxNumber, salt, Sign(challenge));
    }

    /// <summary>
    /// Whether <paramref name="payload"/> — the widget's base64 JSON — solves a challenge this API issued that has not
    /// expired, and if so spends it, so the same payload is refused next time.
    /// </summary>
    /// <param name="payload">The value the widget produced, as the client sent it.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public async Task<bool> VerifyAsync(string? payload, CancellationToken ct = default)
    {
        if (Parse(payload) is not { } solution
            || solution.Algorithm != Algorithm
            || solution.Number < 0 || solution.Number > _options.MaxNumber
            || Expiry(solution.Salt!) is not { } expiresAt)
            return false;

        // Never longer than this API issues: a later expiry means the salt was not ours.
        DateTimeOffset now = time.GetUtcNow();
        if (expiresAt <= now || expiresAt > now + _options.ChallengeLifetime + TimeSpan.FromMinutes(1))
            return false;

        string challenge = Sha256Hex(solution.Salt + solution.Number.ToString(CultureInfo.InvariantCulture));
        if (!FixedTimeEquals(challenge, solution.Challenge) || !FixedTimeEquals(Sign(challenge), solution.Signature))
            return false;

        return await store.TrySpendAsync(challenge, expiresAt, ct);
    }

    private string Sign(string challenge) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.HmacKey!), Encoding.UTF8.GetBytes(challenge)));

    private static string Sha256Hex(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string expected, string? actual) =>
        actual is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));

    /// <summary>The <c>expires</c> parameter after the salt's <c>?</c>; <c>null</c> without exactly one.</summary>
    private static DateTimeOffset? Expiry(string salt)
    {
        int query = salt.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
            return null;

        string[] expires = [.. salt[(query + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(static p => p.StartsWith("expires=", StringComparison.Ordinal))];
        return expires is [{ } only]
               && long.TryParse(only["expires=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
               && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static Solution? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 2048)
            return null;

        try
        {
            Solution? solution = JsonSerializer.Deserialize<Solution>(Convert.FromBase64String(payload));
            return solution is { Algorithm: not null, Challenge: not null, Salt: not null, Signature: not null }
                ? solution
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The widget's payload; <c>took</c> and anything else it adds are ignored.</summary>
    private sealed record Solution(
        [property: JsonPropertyName("algorithm")] string? Algorithm,
        [property: JsonPropertyName("challenge")] string? Challenge,
        [property: JsonPropertyName("number")] long Number,
        [property: JsonPropertyName("salt")] string? Salt,
        [property: JsonPropertyName("signature")] string? Signature);
}
