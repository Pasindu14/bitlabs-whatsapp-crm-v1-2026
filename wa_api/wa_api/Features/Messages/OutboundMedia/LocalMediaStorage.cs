namespace wa_api.Features.Messages.OutboundMedia;

/// <summary>
/// Stores media under <c>Media:StoragePath</c> (default <c>/data/media</c>, a Docker volume in production).
/// Layout: <c>{companyId}/{yyyy}/{MM}/{messageId}{ext}</c>. Every path handed in is resolved and checked to
/// stay inside the root, so a bad value in the database can never read or delete outside it.
/// </summary>
public sealed class LocalMediaStorage : IMediaStorage
{
    private readonly string _root;

    public LocalMediaStorage(IConfiguration config)
    {
        var configured = config["Media:StoragePath"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "media")
            : configured;
        _root = Path.GetFullPath(root);
    }

    public async Task<string> SaveAsync(
        Guid companyId, Guid messageId, string extension, byte[] content, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var relative = Path.Combine(companyId.ToString("N"), now.ToString("yyyy"), now.ToString("MM"), messageId.ToString("N") + ext);

        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, content, ct);
        return relative.Replace('\\', '/');
    }

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));

    public Stream OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);
        if (!File.Exists(full)) throw new FileNotFoundException("Media file is gone.", relativePath);
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
    }

    public bool Delete(string relativePath)
    {
        var full = Resolve(relativePath);
        if (!File.Exists(full)) return false;
        File.Delete(full);
        return true;
    }

    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Media path escapes the storage root.");
        return full;
    }
}
