using System.Security.Cryptography;
using Modules.Backup.Application.Services;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project Backend/tools/DecryptBackup -- <encrypted-file> <new-output-file>");
    return 2;
}
var temporary = Path.GetFullPath(args[1]) + ".partial-" + Guid.NewGuid().ToString("N");
byte[]? key = null;
try
{
    Console.Error.Write("Backup encryption password/key: ");
    string password;
    if (Console.IsInputRedirected) password = Console.ReadLine() ?? "";
    else
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var input = Console.ReadKey(intercept: true);
            if (input.Key == ConsoleKey.Enter) break;
            if (input.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(input.KeyChar) && value.Length < 512) value.Append(input.KeyChar);
        }
        Console.Error.WriteLine(); password = value.ToString();
    }
    if (password.Length is < 1 or > 512) throw new CryptographicException();
    await using var source = File.OpenRead(args[0]);
    var header = new byte[64];
    await source.ReadExactlyAsync(header);
    key = BackupEncryptionFormat.Derive(password, header.AsSpan(24, 16).ToArray());
    source.Position = 0;
    var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
    if (!OperatingSystem.IsWindows()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    await using (var output = new FileStream(temporary, settings))
        await BackupEncryptionFormat.Decrypt(source, output, key, null, CancellationToken.None);
    File.Move(temporary, args[1], overwrite: false);
    Console.WriteLine("Decrypted and authenticated backup saved.");
    return 0;
}
catch
{
    if (File.Exists(temporary)) File.Delete(temporary);
    Console.Error.WriteLine("Decryption failed. Check the password, input integrity and output destination. Existing files were not overwritten.");
    return 1;
}
finally { if (key is not null) CryptographicOperations.ZeroMemory(key); }
