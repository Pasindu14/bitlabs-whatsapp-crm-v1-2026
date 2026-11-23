using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Conversations.Dtos;

/// <summary>
/// Body for POST /conversations — start (or reuse) a conversation with a contact.
/// <para>
/// Send either free text (<see cref="Body"/>) or an approved template (<see cref="TemplateId"/> plus its
/// values). WhatsApp only accepts free text inside the customer's 24-hour reply window, so a cold contact
/// can only be reached with a template; the service refuses free text outside the window.
/// </para>
/// </summary>
/// <param name="TemplateVariables">Values for <c>{{1}}</c>, <c>{{2}}</c>… in order.</param>
public record StartConversationRequest(
    [Required] Guid ContactId,
    [Required] Guid WabaConnectionId,
    [MaxLength(4096)] string? Body = null,
    Guid? TemplateId = null,
    [MaxLength(20)] IReadOnlyList<string>? TemplateVariables = null
);
