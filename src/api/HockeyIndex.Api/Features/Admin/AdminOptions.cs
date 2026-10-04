namespace HockeyIndex.Api.Features.Admin;

/// <summary>Dev and e2e only: listed phones become admins on their next sign-in. Refused in Production.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public const string BootstrapReason = "Admin:BootstrapPhones configuration";

    /// <summary>Phone numbers in any form <c>PhoneNormalizer</c> accepts.</summary>
    public IReadOnlyList<string> BootstrapPhones { get; set; } = [];
}
