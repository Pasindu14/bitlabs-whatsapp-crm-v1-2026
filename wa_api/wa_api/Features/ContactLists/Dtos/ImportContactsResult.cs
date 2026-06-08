namespace wa_api.Features.ContactLists.Dtos;

/// <summary>
/// Outcome of an Excel contact import into a list.
/// <para>
/// <see cref="Imported"/> = brand-new contacts created · <see cref="Updated"/> = existing
/// contacts whose name changed · <see cref="AddedToList"/> = membership links created (rows
/// already in the list are not re-counted) · <see cref="SkippedRows"/> = rows rejected, with why.
/// </para>
/// </summary>
public record ImportContactsResult(
    int TotalRows,
    int Imported,
    int Updated,
    int AddedToList,
    int Skipped,
    IReadOnlyList<ImportSkippedRow> SkippedRows
);

/// <summary>A single row that could not be imported, with the reason.</summary>
public record ImportSkippedRow(int Row, string? Phone, string Reason);
