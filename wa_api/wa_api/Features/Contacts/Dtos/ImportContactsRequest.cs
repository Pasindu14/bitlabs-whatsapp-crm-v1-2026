using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Contacts.Dtos;

/// <summary>One row of a bulk import: a phone number and an optional display name.</summary>
public record ImportContactRow(
    [Required, StringLength(30, MinimumLength = 3)] string Phone,
    [StringLength(200)] string? Name
);

/// <summary>
/// Payload for <c>POST /api/v1/contacts/import</c>. Each row is normalized to E.164, validated, and
/// de-duplicated (within the batch and against existing contacts) before insert. The owning company is
/// resolved from the caller's JWT.
/// </summary>
public record ImportContactsRequest(
    [Required, MinLength(1)] IReadOnlyList<ImportContactRow> Contacts,
    // When true, imported contacts are marked opted-in (ConsentSource.Import) — attesting the operator
    // has off-platform proof of consent for the whole list.
    bool MarkOptedIn = false
);

/// <summary>A single row that could not be imported, with a human-readable reason.</summary>
public record ImportSkippedRow(string Phone, string Reason);

/// <summary>Summary of a bulk import so the operator sees exactly what happened.</summary>
public record ImportContactsResult(
    int Submitted,
    int Imported,
    int DuplicateInFile,
    int DuplicateExisting,
    int Invalid,
    IReadOnlyList<ImportSkippedRow> Skipped
);
