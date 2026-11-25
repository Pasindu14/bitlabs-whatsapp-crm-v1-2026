namespace wa_api.Features.Messages.OutboundMedia;

/// <summary>
/// Where the bytes of a SENT media file live. The database row (<c>MessageMedia.StoragePath</c>) keeps only
/// a relative path, so Supabase Postgres stays small and the files sit on the VPS disk (a Docker volume).
/// Swapping this for object storage later means a new implementation, not a schema change.
/// </summary>
public interface IMediaStorage
{
    /// <summary>Writes the file and returns its path relative to the storage root.</summary>
    Task<string> SaveAsync(
        Guid companyId, Guid messageId, string extension, byte[] content, CancellationToken ct = default);

    bool Exists(string relativePath);

    /// <summary>Opens the file for reading. Throws <see cref="FileNotFoundException"/> when it is gone.</summary>
    Stream OpenRead(string relativePath);

    /// <summary>Deletes the file; a missing file is not an error. Returns whether something was removed.</summary>
    bool Delete(string relativePath);
}
