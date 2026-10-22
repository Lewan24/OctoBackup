namespace Modules.Crypto.Shared.Interfaces;

public interface ICryptoService
{
    string? Encrypt(string? plain);
    string? Decrypt(string? cipherTextBase64);
}
