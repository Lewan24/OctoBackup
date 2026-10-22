using Modules.Backup.Shared.Enums;

namespace Modules.Backup.Shared.Dtos;

public sealed record RestorePreview(int TableCount, IReadOnlyList<RestoreTable> Tables, string Notes)
{
    public IReadOnlyList<RestoreTableName> Catalog { get; init; } = [];
}
public sealed record RestoreTableName(string Schema, string Name);
public sealed record RestoreTable(string Schema, string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows);
public sealed record RestoreReportDto(Guid Id, Guid BackupId, ETestStatus Status, bool Automatic,
    DateTime TestedOn, DateTime? FinishedOn, string? Message, RestorePreview? Preview);
