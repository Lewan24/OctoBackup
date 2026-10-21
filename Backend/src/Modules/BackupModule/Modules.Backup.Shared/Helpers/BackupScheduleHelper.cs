using Modules.Backup.Shared.Dtos;

namespace Modules.Backup.Shared.Helpers;

public static class BackupScheduleHelper
{
    private static DateTime GetNextDateTime(DayOfWeek day, TimeOnly time, DateTime now)
    {
        var daysUntil = ((int)day - (int)now.DayOfWeek + 7) % 7;
        var date = now.Date.AddDays(daysUntil).Add(time.ToTimeSpan());
        if (date <= now)
            date = date.AddDays(7);
        return date;
    }

    public static DateTime GetNextDateTime(BackupsScheduleDto schedule)
    {
        return GetNextDateTime(schedule.SelectedDays, schedule.SelectedTimes);
    }

    public static DateTime GetNextDateTime(List<DayOfWeek> selectedDays, List<TimeOnly> selectedTimes, DateTime? instant = null)
    {
        var now = instant ?? DateTime.Now;
        if (!selectedDays.Any() || !selectedTimes.Any())
            return now.AddHours(1);

        var candidates = selectedDays
            .SelectMany(_ => selectedTimes, (day, time) => GetNextDateTime(day, time, now))
            .ToList();

        return candidates.Min();
    }

    public static string GetNextBackupDateString(BackupsScheduleDto schedule)
    {
        if (!schedule.SelectedDays.Any() || !schedule.SelectedTimes.Any())
            return "-";

        var now = DateTime.Now;
        var candidates = schedule.SelectedDays
            .SelectMany(_ => schedule.SelectedTimes, (day, time) => GetNextDateTime(day, time, now))
            .ToList();

        return candidates.Min().ToString("g");
    }
}
