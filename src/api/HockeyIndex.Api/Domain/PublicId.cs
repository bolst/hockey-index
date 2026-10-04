using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HockeyIndex.Api.Domain;

/// <summary>
/// 10-character lowercase RFC 4648 base32 identifier (50 random bits) used in public URLs for every entity.
/// The alphabet matches the Pages Function route check <c>^[a-z2-7]{10}$</c> and the <c>ck_*_public_id</c> constraints.
/// </summary>
public static partial class PublicId
{
    public const int Length = 10;
    public const string Pattern = "^[a-z2-7]{10}$";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    public static bool IsValid(string? value) => value is not null && PatternRegex().IsMatch(value);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex PatternRegex();
}
