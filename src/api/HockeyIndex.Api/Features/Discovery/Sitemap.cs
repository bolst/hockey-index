using System.Globalization;
using System.Text;
using System.Xml;
using HockeyIndex.Api.Domain;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Features.Discovery;

/// <summary>
/// GET /v1/sitemap.xml (AC-DS-8): canonical <c>/e/{id}/{slug}</c> pages of published and cancelled events on the SPA origin.
/// Cached for an hour; entries that go away meanwhile 404 on the detail page.
/// </summary>
public sealed class Sitemap(AppDbContext db, IOptions<SpaCorsOptions> site)
{
    /// <summary>The sitemap protocol limit per file.</summary>
    public const int MaxUrls = 50_000;

    private const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking()
            .Where(evt => evt.Status == EventStatus.Published || evt.Status == EventStatus.Cancelled)
            .OrderBy(evt => evt.StartsAt)
            .ThenBy(evt => evt.Id)
            .Take(MaxUrls)
            .Select(evt => new { evt.PublicId, evt.Title, evt.UpdatedAt })
            .ToListAsync(cancellationToken);

        var origin = site.Value.SpaOrigin.TrimEnd('/');
        using var buffer = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", Namespace);
            foreach (var evt in events)
            {
                writer.WriteStartElement("url", Namespace);
                writer.WriteElementString("loc", Namespace, origin + EventSlug.CanonicalPath(evt.PublicId, evt.Title));
                writer.WriteElementString("lastmod", Namespace, evt.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return TypedResults.Bytes(buffer.ToArray(), "application/xml; charset=utf-8");
    }
}
