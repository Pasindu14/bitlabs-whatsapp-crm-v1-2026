using wa_api.Common.Entities;
using wa_api.Common.Tenancy;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.WhatsApp.Entities;

namespace wa_api.Features.Messages.Entities;

public enum MessageDirection { Outbound }

public enum MessageStatus { Sent, Failed }

/// <summary>
/// A single WhatsApp message sent by the tenant to a contact.
/// Tenant-scoped via CompanyId; isolated by the global query filter.
/// </summary>
public class Message : BaseEntity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid ContactId { get; set; }
    public Guid WabaConnectionId { get; set; }

    public string Body { get; set; } = string.Empty;

    public MessageDirection Direction { get; set; } = MessageDirection.Outbound;
    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    /// <summary>Message ID returned by Meta's API (wamid). Null when send failed.</summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>Error message from Meta when Status == Failed.</summary>
    public string? ErrorMessage { get; set; }

    public Contact Contact { get; set; } = null!;
    public WabaConnection WabaConnection { get; set; } = null!;
}
