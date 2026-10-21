using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using Modules.Crypto.Shared.Interfaces;

namespace Modules.Crypto.Application.Services;

internal sealed class CryptoService : ICryptoService
{
    private readonly byte[] _key;

    public CryptoService(IConfiguration configuration)
    {
        var keySource = configuration["CRYPTO_KEY"] ??
                        throw new InvalidOperationException("CRYPTO_KEY is missing. Configure it with environment variables or dotnet user-secrets before starting the API.");
        _key = ParseKey(keySource);

        if (_key.Length is not (16 or 24 or 32))
            throw new InvalidOperationException("Invalid key length. Use 16/24/32 bytes (AES-128/192/256).");
    }

    public string? Encrypt(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
            return null;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var bytes = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[bytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, bytes, cipher, tag);
        return "gcm1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string? Decrypt(string? cipherTextBase64)
    {
        if (string.IsNullOrWhiteSpace(cipherTextBase64))
            return null;

        if (cipherTextBase64.StartsWith("gcm1:", StringComparison.Ordinal))
        {
            var payload = Convert.FromBase64String(cipherTextBase64[5..]);
            if (payload.Length < 28) throw new CryptographicException("Invalid encrypted payload.");
            var plain = new byte[payload.Length - 28];
            using var gcm = new AesGcm(_key, 16);
            gcm.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        var allBytes = Convert.FromBase64String(cipherTextBase64);

        using var aes = CreateAes();
        var ivLength = aes.BlockSize / 8;
        var iv = new byte[ivLength];
        var cipherBytes = new byte[allBytes.Length - ivLength];

        Buffer.BlockCopy(allBytes, 0, iv, 0, ivLength);
        Buffer.BlockCopy(allBytes, ivLength, cipherBytes, 0, cipherBytes.Length);

        aes.IV = iv;

        var plainBytes = aes.CreateDecryptor().TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] ParseKey(string s)
    {
        if (Convert.TryFromBase64String(s, new Span<byte>(new byte[s.Length]), out _))
            return Convert.FromBase64String(s);

        if (s.Length % 2 != 0)
            return Encoding.UTF8.GetBytes(s);

        try
        {
            return Enumerable.Range(0, s.Length / 2)
                .Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16))
                .ToArray();
        }
        catch
        {
            /* ignore */
        }

        return Encoding.UTF8.GetBytes(s);
    }

    private Aes CreateAes()
    {
        var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }
}
