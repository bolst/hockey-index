using System.Globalization;
using System.Text;

namespace HockeyIndex.Api.Features.Discovery;

/// <summary>
/// URL slug for <c>/e/{publicId}/{slug}</c>. MUST match <c>eventSlug</c> in <c>src/web/src/shared-render/event-summary.ts</c>:
/// NFKD, drop non-spacing marks, lowercase, runs of anything but <c>[a-z0-9]</c> become one hyphen, trim, 60 chars.
/// </summary>
public static class EventSlug
{
    public const int MaxLength = 60;

    public static string From(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var slug = new StringBuilder(title.Length);
        var pendingHyphen = false;
        foreach (var character in title.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingHyphen && slug.Length > 0)
                {
                    slug.Append('-');
                }

                pendingHyphen = false;
                slug.Append(lower);
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var text = slug.Length > MaxLength ? slug.ToString(0, MaxLength) : slug.ToString();
        return text.TrimEnd('-');
    }

    /// <summary><c>/e/{publicId}/{slug}</c>, or <c>/e/{publicId}</c> when the title has no letters or digits.</summary>
    public static string CanonicalPath(string publicId, string title)
    {
        var slug = From(title);
        return slug.Length == 0 ? $"/e/{publicId}" : $"/e/{publicId}/{slug}";
    }
}
