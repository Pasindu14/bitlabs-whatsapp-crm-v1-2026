using System.Text.Json.Serialization;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Webhooks.Payloads;

/// <summary>
/// Strongly-typed shape of Meta's WhatsApp Cloud API webhook payload. The dispatcher deserializes the
/// raw body straight into this, so snake_case keys are pinned with <c>[JsonPropertyName]</c> (the MVC
/// camelCase policy does not apply to a manual deserialize). Everything is nullable/tolerant — unknown
/// fields are ignored, and a single POST may batch multiple entries/changes across phone numbers.
/// </summary>
public sealed class MetaWebhookEnvelope
{
    [JsonPropertyName("object")] public string? Object { get; set; }
    [JsonPropertyName("entry")] public List<WebhookEntry> Entry { get; set; } = [];
}

public sealed class WebhookEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }            // WABA id
    [JsonPropertyName("changes")] public List<WebhookChange> Changes { get; set; } = [];
}

public sealed class WebhookChange
{
    [JsonPropertyName("field")] public string? Field { get; set; }      // e.g. "messages", "message_template_status_update"
    [JsonPropertyName("value")] public WebhookValue? Value { get; set; }
}

public sealed class WebhookValue
{
    [JsonPropertyName("messaging_product")] public string? MessagingProduct { get; set; }
    [JsonPropertyName("metadata")] public WebhookMetadata? Metadata { get; set; }

    // ── field = "messages" ──────────────────────────────────────────────────
    [JsonPropertyName("statuses")] public List<WebhookStatus>? Statuses { get; set; }   // outbound delivery status (6.4)
    [JsonPropertyName("messages")] public List<WebhookInboundMessage>? Messages { get; set; } // inbound (Phase 7 stub)
    [JsonPropertyName("contacts")] public List<WebhookContact>? Contacts { get; set; }

    // ── field = "message_template_status_update" ────────────────────────────
    [JsonPropertyName("event")] public string? Event { get; set; }      // APPROVED / REJECTED / PENDING ...
    [JsonPropertyName("message_template_id")] public long? MessageTemplateId { get; set; }
    [JsonPropertyName("message_template_name")] public string? MessageTemplateName { get; set; }
    [JsonPropertyName("message_template_language")] public string? MessageTemplateLanguage { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public sealed class WebhookMetadata
{
    [JsonPropertyName("display_phone_number")] public string? DisplayPhoneNumber { get; set; }
    [JsonPropertyName("phone_number_id")] public string? PhoneNumberId { get; set; }
}

public sealed class WebhookStatus
{
    [JsonPropertyName("id")] public string? Id { get; set; }            // wamid
    [JsonPropertyName("status")] public string? Status { get; set; }    // sent / delivered / read / failed
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; } // epoch seconds (string)
    [JsonPropertyName("recipient_id")] public string? RecipientId { get; set; }
    [JsonPropertyName("pricing")] public WebhookPricing? Pricing { get; set; }
    [JsonPropertyName("errors")] public List<WebhookError>? Errors { get; set; }
}

public sealed class WebhookPricing
{
    [JsonPropertyName("billable")] public bool? Billable { get; set; }
    [JsonPropertyName("pricing_model")] public string? PricingModel { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
}

public sealed class WebhookError
{
    [JsonPropertyName("code")] public long? Code { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class WebhookInboundMessage
{
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }            // wamid
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("text")] public WebhookText? Text { get; set; }
}

public sealed class WebhookText
{
    [JsonPropertyName("body")] public string? Body { get; set; }
}

public sealed class WebhookContact
{
    [JsonPropertyName("wa_id")] public string? WaId { get; set; }
    [JsonPropertyName("profile")] public WebhookContactProfile? Profile { get; set; }
}

public sealed class WebhookContactProfile
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>
/// Per-change context handed to each handler: the change itself plus the resolved owning connection
/// (and thus company). <see cref="Connection"/> is null for events that don't carry a phone_number_id
/// (e.g. template status) or for an un-onboarded number.
/// </summary>
public sealed class WebhookContext
{
    public required string Field { get; init; }
    public required WebhookChange Change { get; init; }
    public WabaConnection? Connection { get; init; }
    public Guid? CompanyId => Connection?.CompanyId;
}
