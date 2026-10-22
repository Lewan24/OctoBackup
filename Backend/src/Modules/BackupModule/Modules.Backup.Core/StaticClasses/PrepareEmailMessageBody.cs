namespace Modules.Backup.Core.StaticClasses;

public static class EmailMessageBody
{
    private static string PrepareMessage(string header, string? body)
    {
        return $"{PrepareHeader(header)}{body}<br><br>{PrepareFooter()}";
    }

    private static string PrepareHeader(string header)
    {
        return $"<h4>{header} - report of the day: {DateTime.Today:d}</h4>";
    }

    private static string PrepareFooter()
    {
        return "<i>Thanks for using Backup Service. See you on next mail :)</i>";
    }

    public static string PrepareStatisticsReport(string? body)
    {
        return PrepareMessage("Backup service statistics", body);
    }

    public static string PrepareErrorReport(string? body)
    {
        return PrepareMessage("Backup service error", body);
    }

    public static string PrepareDbBackupSuccessReport(string? body)
    {
        return PrepareMessage("Backup service backup success", body);
    }

    public static string PrepareDbBackupFailureReport(string? body)
    {
        return PrepareMessage("Backup service backup failure", body);
    }
}
