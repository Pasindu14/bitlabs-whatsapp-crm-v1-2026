namespace wa_api.Features.Templates.Dtos;

/// <summary>The Meta sample-media handle returned by <c>POST /templates/media-handle</c> (stored in
/// <c>header.mediaHandle</c>), plus the id of our persisted copy of the bytes (stored in
/// <c>header.mediaPreviewId</c>) so the builder can render a real preview.</summary>
public record MediaHandleResponse(string Handle, Guid PreviewId);
