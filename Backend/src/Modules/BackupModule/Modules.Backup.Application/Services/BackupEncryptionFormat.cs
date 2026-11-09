using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Modules.Backup.Application.Services;

// Portable v1 format. All integers are big endian. Mandatory authenticated end
// record prevents truncation; indices prevent reordering; per-file HKDF keys
// prevent nonce reuse between backups. Memory is bounded to one 1 MiB record.
public static class BackupEncryptionFormat
{
    public const int Iterations = 600_000;
    private const int Chunk = 1024 * 1024;
    private static readonly byte[] Magic = "OCTOENC1"u8.ToArray();
    public static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

    public static async Task Encrypt(Stream source, Stream destination, Guid id, byte[] salt, byte[] key, CancellationToken token)
    {
        var header = new byte[64];
        Magic.CopyTo(header, 0);
        id.ToByteArray().CopyTo(header, 8);
        salt.CopyTo(header, 24);
        RandomNumberGenerator.Fill(header.AsSpan(40, 24));
        await destination.WriteAsync(header, token);
        var fileKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, header.AsSpan(40, 16).ToArray(), Magic);
        try
        {
            using var aes = new AesGcm(fileKey, 16);
            var plain = new byte[Chunk];
            var cipher = new byte[Chunk];
            var tag = new byte[16];
            var nonce = new byte[12];
            header.AsSpan(56, 8).CopyTo(nonce);
            var aad = new byte[68];
            header.CopyTo(aad, 0);
            for (uint index = 0; ; index = checked(index + 1))
            {
                var count = await source.ReadAtLeastAsync(plain, Chunk, false, token);
                BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(64), count);
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), index);
                aes.Encrypt(nonce, plain.AsSpan(0, count), cipher.AsSpan(0, count), tag, aad);
                await destination.WriteAsync(aad.AsMemory(64, 4), token);
                await destination.WriteAsync(cipher.AsMemory(0, count), token);
                await destination.WriteAsync(tag, token);
                if (count == 0) break;
            }
            CryptographicOperations.ZeroMemory(plain);
        }
        finally { CryptographicOperations.ZeroMemory(fileKey); }
    }

    public static async Task Decrypt(Stream source, Stream destination, byte[] key, Guid? expectedId, CancellationToken token)
    {
        var header = new byte[64];
        await source.ReadExactlyAsync(header, token);
        if (!header.AsSpan(0, 8).SequenceEqual(Magic) || (expectedId is not null && new Guid(header.AsSpan(8, 16)) != expectedId))
            throw new CryptographicException("Invalid encrypted backup header.");
        var fileKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, header.AsSpan(40, 16).ToArray(), Magic);
        try
        {
            using var aes = new AesGcm(fileKey, 16);
            var plain = new byte[Chunk];
            var cipher = new byte[Chunk];
            var tag = new byte[16];
            var nonce = new byte[12];
            header.AsSpan(56, 8).CopyTo(nonce);
            var aad = new byte[68];
            header.CopyTo(aad, 0);
            for (uint index = 0; ; index = checked(index + 1))
            {
                await source.ReadExactlyAsync(aad.AsMemory(64, 4), token);
                var count = BinaryPrimitives.ReadInt32BigEndian(aad.AsSpan(64));
                if (count is < 0 or > Chunk) throw new CryptographicException("Invalid encrypted record.");
                await source.ReadExactlyAsync(cipher.AsMemory(0, count), token);
                await source.ReadExactlyAsync(tag, token);
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), index);
                aes.Decrypt(nonce, cipher.AsSpan(0, count), tag, plain.AsSpan(0, count), aad);
                if (count == 0)
                {
                    if (source.ReadByte() != -1) throw new CryptographicException("Unexpected trailing data.");
                    break;
                }
                await destination.WriteAsync(plain.AsMemory(0, count), token);
            }
            CryptographicOperations.ZeroMemory(plain);
        }
        finally { CryptographicOperations.ZeroMemory(fileKey); }
    }
}
