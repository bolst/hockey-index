using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using HockeyIndex.Api.Domain;
using Microsoft.Extensions.Primitives;
using NodaTime;
using NodaTime.Text;

namespace HockeyIndex.Api.Features.Discovery;

/// <summary>
/// A canonical search (AC-DS-7). Only canonical query strings are accepted, so the edge cache key space stays bounded:
/// coordinates with at most 2 decimals, radius and window from fixed sets, <c>from</c> truncated to the UTC hour,
/// skill bounds in 0–7, fee from fixed buckets. Anything else is a 400.
/// </summary>
public sealed partial record SearchQuery(
    decimal Latitude,
    decimal Longitude,
    int RadiusMiles,
    Instant From,
    int Days,
    int SkillMin,
    int SkillMax,
    EventType? Type,
    int? MaxFeeCents)
{
    public const double MetersPerMile = 1609.344;

    public static readonly FrozenSet<int> RadiusMilesOptions = FrozenSet.Create(5, 10, 25, 50, 100);
    public static readonly FrozenSet<int> DaysOptions = FrozenSet.Create(1, 3, 7, 14, 30);
    public static readonly FrozenSet<int> MaxFeeCentsOptions = FrozenSet.Create(0, 1000, 1500, 2000, 2500, 3000, 5000);

    /// <summary>How far <c>from</c> may sit before the current hour (stale cached links) or after it.</summary>
    public static readonly Duration MaxPast = Duration.FromDays(1);
    public static readonly Duration MaxFuture = Duration.FromDays(366);

    private static readonly FrozenSet<string> KnownParameters =
        FrozenSet.Create(StringComparer.Ordinal, "lat", "lng", "r", "from", "days", "smin", "smax", "type", "maxFee");

    private static readonly InstantPattern FromPattern = InstantPattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'ss'Z'");

    public Instant To => From + Duration.FromDays(Days);

    public double RadiusMeters => RadiusMiles * MetersPerMile;

    /// <returns>The query, or null with field errors (code <c>invalid_search</c>).</returns>
    public static SearchQuery? TryParse(IQueryCollection query, Instant now, out IReadOnlyDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in query.Keys.Where(key => !KnownParameters.Contains(key)))
        {
            problems[key] = "Unknown parameter.";
        }

        var latitude = ReadCoordinate(query, "lat", 90, problems);
        var longitude = ReadCoordinate(query, "lng", 180, problems);
        var radius = ReadFromSet(query, "r", RadiusMilesOptions, required: true, problems);
        var days = ReadFromSet(query, "days", DaysOptions, required: true, problems);
        var from = ReadFrom(query, now, problems);
        var skillMin = ReadSkill(query, "smin", problems) ?? HockeyEvent.MinSkill;
        var skillMax = ReadSkill(query, "smax", problems) ?? HockeyEvent.MaxSkill;
        if (skillMin > skillMax && !problems.ContainsKey("smin") && !problems.ContainsKey("smax"))
        {
            problems["smin"] = "Must not exceed smax.";
        }

        var type = ReadType(query, problems);
        var maxFee = ReadFromSet(query, "maxFee", MaxFeeCentsOptions, required: false, problems);

        errors = problems;
        return problems.Count == 0
            ? new SearchQuery(latitude!.Value, longitude!.Value, radius!.Value, from!.Value, days!.Value, skillMin, skillMax, type, maxFee)
            : null;
    }

    private static string? ReadSingle(IQueryCollection query, string name, bool required, Dictionary<string, string> problems)
    {
        if (!query.TryGetValue(name, out StringValues values))
        {
            if (required)
            {
                problems[name] = "Required.";
            }

            return null;
        }

        if (values.Count != 1)
        {
            problems[name] = "Give exactly one value.";
            return null;
        }

        return values[0];
    }

    private static decimal? ReadCoordinate(IQueryCollection query, string name, int maxAbsolute, Dictionary<string, string> problems)
    {
        var text = ReadSingle(query, name, required: true, problems);
        if (text is null)
        {
            return null;
        }

        if (!CoordinatePattern().IsMatch(text)
            || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || Math.Abs(value) > maxAbsolute)
        {
            problems[name] = $"Must be a number between -{maxAbsolute} and {maxAbsolute} with at most 2 decimals.";
            return null;
        }

        return value;
    }

    private static int? ReadFromSet(IQueryCollection query, string name, FrozenSet<int> options, bool required, Dictionary<string, string> problems)
    {
        var text = ReadSingle(query, name, required, problems);
        if (text is null)
        {
            return null;
        }

        if (!IntegerPattern().IsMatch(text)
            || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || !options.Contains(value))
        {
            problems[name] = $"Must be one of {string.Join(", ", options.Order())}.";
            return null;
        }

        return value;
    }

    private static int? ReadSkill(IQueryCollection query, string name, Dictionary<string, string> problems)
    {
        var text = ReadSingle(query, name, required: false, problems);
        if (text is null)
        {
            return null;
        }

        if (text.Length != 1 || text[0] < '0' || text[0] > '7')
        {
            problems[name] = $"Must be a whole number from {HockeyEvent.MinSkill} to {HockeyEvent.MaxSkill}.";
            return null;
        }

        return text[0] - '0';
    }

    private static Instant? ReadFrom(IQueryCollection query, Instant now, Dictionary<string, string> problems)
    {
        var text = ReadSingle(query, "from", required: true, problems);
        if (text is null)
        {
            return null;
        }

        var parsed = FromPattern.Parse(text);
        if (!parsed.Success || parsed.Value.ToUnixTimeTicks() % NodaConstants.TicksPerHour != 0)
        {
            problems["from"] = "Must be a UTC instant truncated to the hour, e.g. 2026-10-03T12:00:00Z.";
            return null;
        }

        var currentHour = Instant.FromUnixTimeTicks(now.ToUnixTimeTicks() - (now.ToUnixTimeTicks() % NodaConstants.TicksPerHour));
        if (parsed.Value < currentHour - MaxPast || parsed.Value > currentHour + MaxFuture)
        {
            problems["from"] = "Must be within a day before and a year after the current hour.";
            return null;
        }

        return parsed.Value;
    }

    private static EventType? ReadType(IQueryCollection query, Dictionary<string, string> problems)
    {
        var text = ReadSingle(query, "type", required: false, problems);
        if (text is null)
        {
            return null;
        }

        if (!SnakeCaseText.TryParse<EventType>(text, out var type))
        {
            problems["type"] = "Must be scrimmage, league or tournament.";
            return null;
        }

        return type;
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]{0,2})(\.[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinatePattern();

    [GeneratedRegex("^(0|[1-9][0-9]{0,5})$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();
}
