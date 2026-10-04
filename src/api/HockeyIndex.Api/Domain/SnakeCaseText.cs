using System.Collections.Frozen;
using System.Text.Json;

namespace HockeyIndex.Api.Domain;

/// <summary>Maps enum members to the snake_case text stored in CHECK-constrained columns and returned by the API.</summary>
public static class SnakeCaseText
{
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum => Map<TEnum>.Texts[value];

    public static TEnum Parse<TEnum>(string text)
        where TEnum : struct, Enum =>
        TryParse<TEnum>(text, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(text), text, $"Unknown {typeof(TEnum).Name}.");

    public static bool TryParse<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        if (text is not null && Map<TEnum>.Values.TryGetValue(text, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>SQL list for CHECK constraints, e.g. <c>'draft','published'</c>.</summary>
    public static string SqlList<TEnum>()
        where TEnum : struct, Enum => string.Join(",", Enum.GetValues<TEnum>().Select(value => $"'{Of(value)}'"));

    private static class Map<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> Texts =
            Enum.GetValues<TEnum>().ToFrozenDictionary(value => value, value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()));

        public static readonly FrozenDictionary<string, TEnum> Values =
            Texts.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
    }
}
