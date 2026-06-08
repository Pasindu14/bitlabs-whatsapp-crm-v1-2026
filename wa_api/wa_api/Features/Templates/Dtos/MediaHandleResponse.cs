namespace wa_api.Features.Templates.Dtos;

/// <summary>The Meta sample-media handle returned by <c>POST /templates/media-handle</c>;
/// the builder stores it in <c>header.example.mediaHandle</c>.</summary>
public record MediaHandleResponse(string Handle);
