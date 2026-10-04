using System.Data;
using System.Diagnostics;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using NpgsqlTypes;

namespace HockeyIndex.Api.Features.Discovery;

/// <summary>GET /v1/search (AC-DS-2..5, AC-DS-7). Range overlap on time and skill; cancelled events stay listed until they end.</summary>
public sealed class SearchEvents(AppDbContext db, IClock clock, VenueTimeConverter times, HockeyIndexMetrics metrics)
{
    public const int MaxResults = 100;
    public const string CacheTag = "search";

    /// <summary>
    /// Literal status list so the planner matches the partial indexes <c>ix_events_time_active</c> and <c>ix_events_skill</c>.
    /// <c>greatest(starts_at, @from)</c> sorts in-progress leagues at the window start. <c>LIMIT 101</c> detects truncation.
    /// </summary>
    internal const string Sql = """
        SELECT e.public_id, e.status, e.type, e.title, e.rink_label,
               e.starts_local, e.ends_local, e.starts_at, e.ends_at,
               lower(e.skill_range) AS skill_min, upper(e.skill_range) - 1 AS skill_max,
               e.fee_cents, e.currency,
               v.public_id AS venue_public_id, v.name AS venue_name, v.address_line, v.city, v.region, v.country, v.time_zone,
               ST_Y(v.geo::geometry) AS latitude, ST_X(v.geo::geometry) AS longitude,
               ST_Distance(v.geo, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography) AS distance_m
        FROM events e
        JOIN venues v ON v.id = e.venue_id
        WHERE e.status IN ('published','cancelled')
          AND e.ends_at > @now
          AND ST_DWithin(v.geo, ST_SetSRID(ST_MakePoint(@lng, @lat), 4326)::geography, @radius_m)
          AND tstzrange(e.starts_at, e.ends_at, '[)') && tstzrange(@from, @to, '[)')
          AND e.skill_range && int4range(@smin, @smax, '[]')
          AND (@type IS NULL OR e.type = @type)
          AND (@max_fee IS NULL OR e.fee_cents <= @max_fee)
        ORDER BY greatest(e.starts_at, @from), e.id
        LIMIT 101
        """;

    public async Task<IResult> HandleAsync(HttpRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var now = clock.GetCurrentInstant();
        if (SearchQuery.TryParse(request.Query, now, out var errors) is not { } query)
        {
            return DiscoveryProblems.InvalidSearch(errors);
        }

        var started = Stopwatch.GetTimestamp();
        var results = await QueryAsync(query, now, cancellationToken);
        response.Headers["Cache-Tag"] = CacheTag;
        var truncated = results.Count > MaxResults;
        metrics.RecordSearch(Stopwatch.GetElapsedTime(started), truncated);
        return TypedResults.Ok(new SearchResponse(truncated ? results.GetRange(0, MaxResults) : results, truncated));
    }

    /// <summary>Parameters for <see cref="Sql"/>; also used by tests to <c>EXPLAIN</c> the exact statement.</summary>
    internal static void AddParameters(NpgsqlCommand command, SearchQuery query, Instant now)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(query);
        var parameters = command.Parameters;
        parameters.Add(new NpgsqlParameter("lat", NpgsqlDbType.Double) { Value = (double)query.Latitude });
        parameters.Add(new NpgsqlParameter("lng", NpgsqlDbType.Double) { Value = (double)query.Longitude });
        parameters.Add(new NpgsqlParameter("radius_m", NpgsqlDbType.Double) { Value = query.RadiusMeters });
        parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToDateTimeUtc() });
        parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = query.From.ToDateTimeUtc() });
        parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = query.To.ToDateTimeUtc() });
        parameters.Add(new NpgsqlParameter("smin", NpgsqlDbType.Integer) { Value = query.SkillMin });
        parameters.Add(new NpgsqlParameter("smax", NpgsqlDbType.Integer) { Value = query.SkillMax });
        parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = query.Type is { } type ? SnakeCaseText.Of(type) : DBNull.Value });
        parameters.Add(new NpgsqlParameter("max_fee", NpgsqlDbType.Integer) { Value = query.MaxFeeCents is { } fee ? fee : DBNull.Value });
    }

    private async Task<List<SearchResult>> QueryAsync(SearchQuery query, Instant now, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(Sql, connection);
            AddParameters(command, query, now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<SearchResult>();
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadResult(reader));
            }

            return results;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private SearchResult ReadResult(NpgsqlDataReader reader)
    {
        var type = SnakeCaseText.Parse<EventType>(reader.GetString(2));
        var startsLocal = reader.GetDateTime(5);
        var endsLocal = reader.GetDateTime(6);
        var timeZone = reader.GetString(19);
        var isSeason = type != EventType.Scrimmage;
        var venue = new PublicVenue(
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetString(18),
            timeZone,
            reader.GetDouble(20),
            reader.GetDouble(21));

        return new SearchResult(
            reader.GetString(0),
            reader.GetString(1),
            SnakeCaseText.Of(type),
            reader.GetString(3),
            venue,
            reader.IsDBNull(4) ? null : reader.GetString(4),
            startsLocal,
            endsLocal,
            times.ToVenueOffset(timeZone, reader.GetFieldValue<DateTimeOffset>(7)),
            times.ToVenueOffset(timeZone, reader.GetFieldValue<DateTimeOffset>(8)),
            isSeason ? DateOnly.FromDateTime(startsLocal) : null,
            isSeason ? DateOnly.FromDateTime(endsLocal).AddDays(-1) : null,
            new SkillRangeDto(reader.GetInt32(9), reader.GetInt32(10)),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.GetString(12),
            Math.Round(reader.GetDouble(22) / SearchQuery.MetersPerMile, 1));
    }
}
