using Microsoft.Extensions.Options;
using RecoverFlow.Application.Common;
using RecoverFlow.Infrastructure.Security;

namespace RecoverFlow.Tests.Unit;

public class TokenEncryptorTests
{
    private static TokenEncryptor CreateEncryptor() =>
        new(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(new byte[32]) }));

    [Fact]
    public void Encrypt_then_decrypt_returns_original_plaintext()
    {
        var encryptor = CreateEncryptor();
        const string plaintext = "sk_live_totally_fake_access_token";

        var ciphertext = encryptor.Encrypt(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, encryptor.Decrypt(ciphertext));
    }

    [Fact]
    public void Encrypt_produces_different_ciphertext_each_call()
    {
        var encryptor = CreateEncryptor();
        const string plaintext = "same-input-every-time";

        var first = encryptor.Encrypt(plaintext);
        var second = encryptor.Encrypt(plaintext);

        Assert.NotEqual(first, second); // random nonce per call
    }

    [Fact]
    public void Decrypt_throws_when_ciphertext_is_tampered()
    {
        var encryptor = CreateEncryptor();
        var ciphertext = encryptor.Encrypt("some-access-token");
        var bytes = Convert.FromBase64String(ciphertext);
        bytes[^1] ^= 0xFF; // flip a bit in the auth tag
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => encryptor.Decrypt(tampered));
    }
}
