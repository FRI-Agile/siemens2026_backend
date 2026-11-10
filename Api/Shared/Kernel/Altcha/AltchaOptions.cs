namespace Kernel.Altcha;

/// <summary>
/// The ALTCHA proof-of-work settings (ADR-022), bound from the <see cref="SectionName"/> section. The key is a secret:
/// Key Vault in production (iam.md §5), passed to the API as <c>Altcha__HmacKey</c>.
/// </summary>
/// <remarks>
/// <see cref="HmacKey"/> is required outside Development, where startup fails without it. In Development it may be left
/// empty: the API signs with a random key of its own, so challenges issued before a restart stop verifying.
/// </remarks>
public sealed class AltchaOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Altcha";

    /// <summary>The shortest key accepted outside Development: 256 bits of base64 or hex is at least this long.</summary>
    public const int MinKeyLength = 32;

    /// <summary>The HMAC-SHA256 key that signs every challenge, so the API only accepts challenges it issued.</summary>
    public string? HmacKey { get; set; }

    /// <summary>
    /// The largest secret number a challenge hides, i.e. how much work solving one takes: the client tries every number
    /// up to it, half of them on average. Defaults to 100 000, well under a second in a phone's browser.
    /// </summary>
    public int MaxNumber { get; set; } = 100_000;

    /// <summary>
    /// How long a challenge may be solved and its solution sent, from when it was issued. Long enough to fill in the
    /// form after the widget solved it on load; defaults to 20 minutes.
    /// </summary>
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(20);
}
