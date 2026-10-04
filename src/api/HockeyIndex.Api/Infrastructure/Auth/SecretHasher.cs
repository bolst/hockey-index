using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HockeyIndex.Api.Infrastructure.Auth;

public sealed class HmacOptions
{
    public const string SectionName = "Hmac";

    [Required]
    [MinLength(32)]
    public string Key { get; set; } = "";
}

/// <summary>Keyed hashes for values stored only as lookups (phone, email, IP key, email codes).</summary>
public sealed class SecretHasher(IOptions<HmacOptions> options)
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(options.Value.Key);

    public byte[] Hash(string purpose, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{purpose}\n{value}"));

    public byte[] HashPhone(string e164) => Hash("phone", e164);

    public byte[] HashEmail(string normalizedEmail) => Hash("email", normalizedEmail);

    public byte[] HashIpKey(string ipKeyHex) => Hash("ip", ipKeyHex);
}
