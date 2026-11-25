namespace wa_api.Features.Messages.OutboundMedia;

/// <summary>
/// What an agent may attach, mirroring WhatsApp Cloud API limits. Checked BEFORE any Meta call so a bad file
/// is refused with a clear message instead of burning a round-trip. The stored extension comes from this
/// table (never from the uploaded file name), so a hostile name can't pick the file's type on disk.
/// </summary>
public static class OutboundMediaRules
{
    private const long MB = 1024 * 1024;

    /// <summary>WhatsApp caps captions at 1024 characters.</summary>
    public const int MaxCaptionLength = 1024;

    public sealed record Kind(string MediaType, long MaxBytes, string Extension);

    private static readonly Dictionary<string, Kind> ByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = new("image", 5 * MB, ".jpg"),
        ["image/png"] = new("image", 5 * MB, ".png"),

        ["audio/aac"] = new("audio", 16 * MB, ".aac"),
        ["audio/mp4"] = new("audio", 16 * MB, ".m4a"),
        ["audio/mpeg"] = new("audio", 16 * MB, ".mp3"),
        ["audio/amr"] = new("audio", 16 * MB, ".amr"),
        ["audio/ogg"] = new("audio", 16 * MB, ".ogg"),

        ["video/mp4"] = new("video", 16 * MB, ".mp4"),
        ["video/3gpp"] = new("video", 16 * MB, ".3gp"),

        ["application/pdf"] = new("document", 100 * MB, ".pdf"),
        ["application/msword"] = new("document", 100 * MB, ".doc"),
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = new("document", 100 * MB, ".docx"),
        ["application/vnd.ms-excel"] = new("document", 100 * MB, ".xls"),
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = new("document", 100 * MB, ".xlsx"),
        ["application/vnd.ms-powerpoint"] = new("document", 100 * MB, ".ppt"),
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = new("document", 100 * MB, ".pptx"),
        ["text/plain"] = new("document", 100 * MB, ".txt"),
    };

    /// <summary>Looks up the kind for a MIME type (parameters such as <c>; codecs=opus</c> are ignored).</summary>
    public static Kind? Classify(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var mime = contentType.Split(';')[0].Trim();
        return ByMime.GetValueOrDefault(mime);
    }

    /// <summary>The MIME type with any parameters stripped — what Meta and the browser should see.</summary>
    public static string NormalizeMime(string contentType) => contentType.Split(';')[0].Trim().ToLowerInvariant();

    /// <summary>Only images, video and documents can carry a caption; WhatsApp ignores it on audio.</summary>
    public static bool SupportsCaption(string mediaType) => mediaType != "audio";

    /// <summary>The label shown in the inbox list when the message has no caption.</summary>
    public static string PreviewLabel(string mediaType) => mediaType switch
    {
        "image" => "Photo",
        "audio" => "Audio",
        "video" => "Video",
        _ => "Document",
    };
}
