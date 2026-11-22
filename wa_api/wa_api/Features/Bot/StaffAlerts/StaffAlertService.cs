using Microsoft.EntityFrameworkCore;
using wa_api.Features.Contacts.Entities;
using wa_api.Features.Messages;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Features.Templates;
using wa_api.Features.Templates.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot.StaffAlerts;

/// <summary>
/// One customer the team needs to contact: who, and a single line on why.
/// </summary>
/// <param name="CompanyId">The business whose team is told.</param>
/// <param name="ContactId">The customer.</param>
/// <param name="ConversationId">Their thread, used to pick the WhatsApp number the alert is sent from.</param>
/// <param name="Summary">What they need. Flattened to one line before it is sent.</param>
/// <param name="InApp">
/// False when the caller already raised its own in-app notification with more detail than a summary
/// carries (a cancellation, a failed booking), so the team is not shown the same thing twice.
/// </param>
public sealed record StaffAlert(
    Guid CompanyId, Guid ContactId, Guid? ConversationId, string Summary, bool InApp = true);

/// <summary>
/// Tells the team a customer is waiting for a person: an in-app notification, and a WhatsApp template to
/// each number the business listed.
/// </summary>
public interface IStaffAlertService
{
    /// <summary>
    /// Never throws. The bot has already paused and told the customer someone will be in touch, so a failed
    /// alert must not undo that — it is logged, and the in-app notification is the fallback record.
    /// </summary>
    Task AlertAsync(StaffAlert alert, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class StaffAlertService(
    AppDbContext db,
    IWhatsAppMessageSender sender,
    INotificationService notifications,
    ILogger<StaffAlertService> logger) : IStaffAlertService
{
    /// <summary>
    /// Longer than a follow-up's variable: this one is the whole point of the message. Still far under
    /// what Meta accepts, and short enough to read at a glance on a lock screen.
    /// </summary>
    public const int MaxSummaryLength = 300;

    private const int MaxNameLength = 60;

    public async Task AlertAsync(StaffAlert alert, CancellationToken ct = default)
    {
        try
        {
            var contact = await db.Contacts.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == alert.ContactId && c.CompanyId == alert.CompanyId, ct);

            if (contact is null) return;

            var summary = Shorten(alert.Summary) ?? "No details were given.";

            if (alert.InApp) await NotifyInAppAsync(alert.CompanyId, contact, summary, ct);

            await SendWhatsAppAsync(alert, contact, summary, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not alert the team about contact {ContactId} of company {CompanyId}.",
                alert.ContactId, alert.CompanyId);
        }
    }

    private async Task NotifyInAppAsync(Guid companyId, Contact contact, string summary, CancellationToken ct)
    {
        try
        {
            await notifications.CreateAsync(
                companyId, NotificationType.System, "A customer needs a person",
                $"{DisplayName(contact)} (+{contact.Phone}): {summary}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not raise the in-app handoff notification for company {CompanyId}.", companyId);
        }
    }

    private async Task SendWhatsAppAsync(StaffAlert alert, Contact customer, string summary, CancellationToken ct)
    {
        var profile = await db.BotProfiles.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == alert.CompanyId, ct);

        if (profile?.HandoffAlertTemplateId is not { } templateId || profile.HandoffAlertPhones is not { Count: > 0 })
            return;

        var template = await db.Templates.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId && t.CompanyId == alert.CompanyId && t.IsActive, ct);

        // Meta can pause or disable an approved template at any time, so this is checked at send time.
        if (template is null || template.Status != TemplateStatus.Approved
            || TemplateFill.VariableCount(template) != 3 || !TemplateSendability.IsBodyOnly(template))
        {
            logger.LogWarning(
                "Handoff alert for company {CompanyId} not sent over WhatsApp: template {TemplateId} is "
                + "{Status}.", alert.CompanyId, templateId, template?.Status.ToString() ?? "missing");
            return;
        }

        var waba = await FindConnectionAsync(alert, template, ct);
        if (waba is null)
        {
            logger.LogWarning(
                "Handoff alert for company {CompanyId} not sent over WhatsApp: no active WhatsApp number.",
                alert.CompanyId);
            return;
        }

        string[] parameters =
        [
            TemplateFill.Sanitize(DisplayName(customer), MaxNameLength) ?? "A customer",
            customer.Phone,
            summary,
        ];

        var rendered = TemplateFill.Render(template.Components.Body.Text, parameters);

        foreach (var phone in profile.HandoffAlertPhones.Where(p => p != customer.Phone).Distinct())
        {
            // One number failing (an unreachable phone, a rate limit) must not stop the rest being told.
            try
            {
                var staff = await GetOrCreateStaffContactAsync(alert.CompanyId, phone, ct);

                var sent = await sender.SendTemplateAsync(
                    staff, waba, template.Name, template.Language, parameters, rendered, conversationId: null, ct);

                if (sent.Status == Messages.Entities.MessageStatus.Failed)
                    logger.LogWarning(
                        "Handoff alert to {Phone} for company {CompanyId} was refused: {Error}",
                        phone, alert.CompanyId, sent.ErrorMessage);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Handoff alert to {Phone} for company {CompanyId} failed.", phone, alert.CompanyId);
            }
        }
    }

    /// <summary>
    /// The number the customer wrote to, so the alert arrives from the business's own number. Falls back to
    /// the template's number, then to any active one.
    /// </summary>
    private async Task<WabaConnection?> FindConnectionAsync(StaffAlert alert, Template template, CancellationToken ct)
    {
        var connections = db.WabaConnections.IgnoreQueryFilters()
            .Where(w => w.CompanyId == alert.CompanyId && w.IsActive);

        if (alert.ConversationId is { } conversationId)
        {
            var viaConversation = await db.Conversations.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Id == conversationId && c.CompanyId == alert.CompanyId)
                .Select(c => c.WabaConnectionId)
                .FirstOrDefaultAsync(ct);

            if (viaConversation != Guid.Empty
                && await connections.FirstOrDefaultAsync(w => w.Id == viaConversation, ct) is { } conversational)
            {
                return conversational;
            }
        }

        return await connections.FirstOrDefaultAsync(w => w.Id == template.WabaConnectionId, ct)
               ?? await connections.FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The message row needs a contact to hang off, so a team member gets one. It is a real contact, which
    /// is why the inbound webhook knows to keep the bot away from the team's numbers.
    /// </summary>
    private async Task<Contact> GetOrCreateStaffContactAsync(Guid companyId, string phone, CancellationToken ct)
    {
        var existing = await db.Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.Phone == phone, ct);

        if (existing is not null) return existing;

        var created = new Contact { CompanyId = companyId, Phone = phone, Name = phone };
        db.Contacts.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    private static string DisplayName(Contact contact) =>
        string.IsNullOrWhiteSpace(contact.Name) || contact.Name == contact.Phone ? "A customer" : contact.Name.Trim();

    /// <summary>A too-long summary is cut rather than dropped — a truncated reason beats none.</summary>
    private static string? Shorten(string? text)
    {
        var flat = TemplateFill.Sanitize(text, int.MaxValue);
        if (flat is null) return null;

        return flat.Length <= MaxSummaryLength ? flat : flat[..(MaxSummaryLength - 1)].TrimEnd() + "…";
    }
}
