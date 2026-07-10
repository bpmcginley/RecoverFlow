using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;

namespace RecoverFlow.Infrastructure.Security;

/// <summary>AES-256-GCM at-rest encryption for merchant Stripe OAuth access tokens.</summary>
public sealed class TokenEncryptor(IOptions<EncryptionOptions> options) : ITokenEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] key = Convert.FromBase64String(options.Value.Key);

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return Convert.ToBase64String([.. nonce, .. cipherBytes, .. tag]);
    }

    public string Decrypt(string ciphertext)
    {
        var all = Convert.FromBase64String(ciphertext);
        var nonce = all[..NonceSize];
        var tag = all[^TagSize..];
        var cipherBytes = all[NonceSize..^TagSize];
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}
