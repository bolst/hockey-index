using PhoneNumbers;

namespace HockeyIndex.Api.Infrastructure.Auth;

public enum PhoneRejection
{
    None,
    Invalid,
    Region,
    TollFree,
}

/// <param name="NpaNxx">NANP area code plus exchange, the unit SMS pumping rings target; <c>null</c> outside +1.</param>
public sealed record NormalizedPhone(string E164, string? NpaNxx);

public sealed record PhoneNormalization(NormalizedPhone? Phone, PhoneRejection Rejection)
{
    public bool IsValid => Rejection == PhoneRejection.None;
}

/// <summary>Parses user input to E.164 and admits only US and Canadian numbers (NANP +1 also covers Caribbean regions).</summary>
public static class PhoneNormalizer
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();
    private static readonly HashSet<string> AllowedRegions = new(StringComparer.Ordinal) { "US", "CA" };

    public static PhoneNormalization Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32)
        {
            return new(null, PhoneRejection.Invalid);
        }

        PhoneNumber number;
        try
        {
            number = Util.Parse(input, "US");
        }
        catch (NumberParseException)
        {
            return new(null, PhoneRejection.Invalid);
        }

        if (!Util.IsValidNumber(number))
        {
            return new(null, PhoneRejection.Invalid);
        }

        var npaNxx = number.CountryCode == 1 ? Util.GetNationalSignificantNumber(number)[..6] : null;
        var phone = new NormalizedPhone(Util.Format(number, PhoneNumberFormat.E164), npaNxx);
        if (number.CountryCode != 1 || !AllowedRegions.Contains(Util.GetRegionCodeForNumber(number) ?? ""))
        {
            return new(phone, PhoneRejection.Region);
        }

        return Util.GetNumberType(number) == PhoneNumberType.TOLL_FREE
            ? new(phone, PhoneRejection.TollFree)
            : new(phone, PhoneRejection.None);
    }
}
