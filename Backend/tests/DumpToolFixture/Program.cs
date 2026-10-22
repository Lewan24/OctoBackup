namespace OctoBackup.DumpToolFixture;

public static class EntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        if (args[0] == "success")
        {
            Console.Write("SELECT 'fixture-row';\n");
            return 0;
        }
        if (args[0] == "timeout")
        {
            Console.Write("incomplete");
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 0;
        }
        Console.Write("partial dump");
        Console.Error.Write("mysqldump: backup has insufficient privileges to SHOW CREATE PROCEDURE `sensitive_routine`! ");
        Console.Error.Write(Environment.GetEnvironmentVariable("MYSQL_PWD"));
        Console.Error.Write(new string('x', 256 * 1024));
        return 2;
    }
}

public sealed class DumpToolFixtureMarker;
