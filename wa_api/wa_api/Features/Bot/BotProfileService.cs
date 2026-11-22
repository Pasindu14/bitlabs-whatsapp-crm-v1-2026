using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Bot.Dtos;
using wa_api.Features.Bot.Entities;
using wa_api.Features.Contacts;
using wa_api.Features.Templates;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot;

/// <summary>
/// CRUD over the AI assistant's configuration. The global query filter scopes every read and write to
/// the caller's company and the AuditInterceptor stamps <c>CompanyId</c> on insert, so nothing here
/// writes a manual <c>WHERE CompanyId</c>.
/// </summary>
public class BotProfileService(AppDbContext db) : IBotProfileService
{
    public async Task<BotProfileResponse> GetAsync(CancellationToken ct = default)
    {
        var profile = await db.BotProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        return profile is null ? Empty() : Map(profile);
    }

    public async Task<BotProfileResponse> SaveAsync(
        SaveBotProfileRequest request, CancellationToken ct = default)
    {
        var alertPhones = NormalizeAlertPhones(request.HandoffAlertPhones);
        await EnsureAlertTemplateAsync(request.HandoffAlertTemplateId, ct);

        var profile = await db.BotProfiles.FirstOrDefaultAsync(ct);
        if (profile is null)
        {
            profile = new BotProfile();
            db.BotProfiles.Add(profile);
        }

        profile.IsEnabled = request.IsEnabled;
        profile.BusinessName = request.BusinessName.Trim();
        profile.BusinessDescription = request.BusinessDescription.Trim();
        profile.ToneNotes = Clean(request.ToneNotes);
        profile.Location = Clean(request.Location);
        profile.ContactNote = Clean(request.ContactNote);
        profile.BookingInstructions = Clean(request.BookingInstructions);
        profile.PreferredLanguage = Clean(request.PreferredLanguage);
        profile.CurrencyLabel = Clean(request.CurrencyLabel);
        profile.DailyReplyLimit = request.DailyReplyLimit;
        profile.ResumeBotAfterIdleHours = request.ResumeBotAfterIdleHours;
        profile.HandoffAlertTemplateId = request.HandoffAlertTemplateId;
        profile.HandoffResumeMinutes = request.HandoffResumeMinutes;
        if (alertPhones is not null) profile.HandoffAlertPhones = alertPhones;
        // Null = an older client that does not know these fields: leave them alone rather than wipe a
        // clinic's medical rule or switch its bookings off.
        if (request.IndustryRules is not null) profile.IndustryRules = Clean(request.IndustryRules);
        if (request.BookingsEnabled is { } bookingsEnabled) profile.BookingsEnabled = bookingsEnabled;
        if (request.FollowUpSteps is not null)
            profile.FollowUpSteps = NormalizeFollowUpSteps(request.FollowUpSteps);

        await db.SaveChangesAsync(ct);
        return Map(profile);
    }

    public async Task<IReadOnlyList<BotFaqResponse>> GetFaqsAsync(CancellationToken ct = default)
    {
        // Same ordering the prompt builder applies, so the list reads in the order the model sees.
        // Deleted rows stay in the table (soft delete). Without this filter they came back on screen and
        // the list looked like every answer had been entered twice.
        return await db.BotFaqs
            .AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.CreatedAt)
            .Select(f => new BotFaqResponse(
                f.Id, f.Question, f.Answer, f.SortOrder, f.IsEnabled, f.CreatedAt, f.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<BotFaqResponse> CreateFaqAsync(
        SaveBotFaqRequest request, CancellationToken ct = default)
    {
        var faq = new BotFaq
        {
            Question = request.Question.Trim(),
            Answer = request.Answer.Trim(),
            SortOrder = request.SortOrder,
            IsEnabled = request.IsEnabled,
        };

        db.BotFaqs.Add(faq);
        await db.SaveChangesAsync(ct);

        return Map(faq);
    }

    public async Task<BotFaqResponse> UpdateFaqAsync(
        Guid id, SaveBotFaqRequest request, CancellationToken ct = default)
    {
        var faq = await db.BotFaqs.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException("BotFaq", id);

        faq.Question = request.Question.Trim();
        faq.Answer = request.Answer.Trim();
        faq.SortOrder = request.SortOrder;
        faq.IsEnabled = request.IsEnabled;

        await db.SaveChangesAsync(ct);
        return Map(faq);
    }

    public async Task DeleteFaqAsync(Guid id, CancellationToken ct = default)
    {
        var faq = await db.BotFaqs.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException("BotFaq", id);

        faq.IsActive = false;                // soft delete — rows are never physically removed
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BotLinkResponse>> GetLinksAsync(CancellationToken ct = default)
    {
        return await db.BotLinks
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.CreatedAt)
            .Select(l => new BotLinkResponse(
                l.Id, l.Title, l.Url, l.WhenToShare, l.Message, l.SortOrder, l.IsEnabled,
                l.CreatedAt, l.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<BotLinkResponse> CreateLinkAsync(
        SaveBotLinkRequest request, CancellationToken ct = default)
    {
        var link = new BotLink();
        Apply(link, request);

        db.BotLinks.Add(link);
        await db.SaveChangesAsync(ct);

        return Map(link);
    }

    public async Task<BotLinkResponse> UpdateLinkAsync(
        Guid id, SaveBotLinkRequest request, CancellationToken ct = default)
    {
        var link = await db.BotLinks.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("BotLink", id);

        Apply(link, request);

        await db.SaveChangesAsync(ct);
        return Map(link);
    }

    public async Task DeleteLinkAsync(Guid id, CancellationToken ct = default)
    {
        var link = await db.BotLinks.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("BotLink", id);

        link.IsActive = false;               // soft delete — rows are never physically removed
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The URL is sent to customers verbatim, so it must be a real web address — a typo'd
    /// "www.site" or a javascript: URI would go out exactly as typed.
    /// </summary>
    private static void Apply(BotLink link, SaveBotLinkRequest request)
    {
        var url = request.Url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BusinessRuleException("INVALID_URL",
                "Enter a full web address starting with https:// (or http://).");
        }

        link.Title = request.Title.Trim();
        link.Url = url;
        link.WhenToShare = Clean(request.WhenToShare);
        link.Message = Clean(request.Message);
        link.SortOrder = request.SortOrder;
        link.IsEnabled = request.IsEnabled;
    }

    /// <summary>
    /// The team numbers as digits only, deduplicated. Null means "leave the saved list alone"; an empty
    /// list clears it. A number that cannot be a real WhatsApp number is refused rather than saved, because
    /// the alert would then fail silently at the moment someone needed it.
    /// </summary>
    private const int MaxAlertPhones = 5;

    private static List<string>? NormalizeAlertPhones(IReadOnlyList<string>? requested)
    {
        if (requested is null) return null;

        var result = new List<string>();

        foreach (var raw in requested.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!PhoneNumber.TryNormalize(raw, out var phone))
                throw new BusinessRuleException("INVALID_ALERT_PHONE",
                    $"\"{raw.Trim()}\" is not a valid WhatsApp number. Include the country code, e.g. 94771234567.");

            if (!result.Contains(phone)) result.Add(phone);
        }

        if (result.Count > MaxAlertPhones)
            throw new BusinessRuleException("TOO_MANY_ALERT_PHONES",
                $"List at most {MaxAlertPhones} team numbers.");

        return result;
    }

    /// <summary>Only a template that actually takes name, number and summary can carry the alert.</summary>
    private async Task EnsureAlertTemplateAsync(Guid? templateId, CancellationToken ct)
    {
        if (templateId is not { } id) return;

        var template = await db.Templates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.IsActive, ct)
            ?? throw new BusinessRuleException("UNKNOWN_TEMPLATE", "That template no longer exists.");

        if (!TemplateSendability.IsBodyOnly(template))
            throw new BusinessRuleException("INVALID_ALERT_TEMPLATE",
                "The handoff alert must be a text-only template: no image, video or document header, no "
                + "carousel and no button link with a variable.");

        if (TemplateFill.VariableCount(template) != 3)
            throw new BusinessRuleException("INVALID_ALERT_TEMPLATE",
                "The handoff alert template needs exactly three variables: the customer's name, their number "
                + "and a short summary.");
    }

    /// <summary>
    /// Blank steps dropped, delays clamped, and the rest put in time order — the job walks the list by
    /// index, so an out-of-order list would send "Day 4" before "Day 2".
    /// </summary>
    private static List<BotFollowUpStep> NormalizeFollowUpSteps(IReadOnlyList<BotFollowUpStepDto> steps) =>
        [.. steps
            .Where(s => !string.IsNullOrWhiteSpace(s.Message))
            .Select(s => new BotFollowUpStep
            {
                DelayMinutes = Math.Clamp(s.DelayMinutes, 1, Jobs.BotFollowUpJob.MaxStepDelayMinutes),
                Message = s.Message.Trim(),
                TemplateId = s.TemplateId,
                VariableOverrides = [.. (s.VariableOverrides ?? []).Select(v => v.Trim())],
            })
            .OrderBy(s => s.DelayMinutes)
            .Take(10)];

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The shape the settings screen renders before a tenant has saved anything.</summary>
    private static BotProfileResponse Empty() => new(
        null, false, false, string.Empty, string.Empty, null, null, null, null, null, null, 500, null, [], 24, [], null, 0,
        null, true);

    private static BotProfileResponse Map(BotProfile p) => new(
        p.Id, true, p.IsEnabled, p.BusinessName, p.BusinessDescription, p.ToneNotes, p.Location,
        p.ContactNote, p.BookingInstructions, p.PreferredLanguage, p.CurrencyLabel,
        p.DailyReplyLimit, p.UpdatedAt,
        [.. (p.FollowUpSteps ?? []).Select(s => new BotFollowUpStepDto(
            s.DelayMinutes, s.Message, s.TemplateId, s.VariableOverrides))],
        p.ResumeBotAfterIdleHours,
        [.. p.HandoffAlertPhones ?? []],
        p.HandoffAlertTemplateId,
        p.HandoffResumeMinutes,
        p.IndustryRules,
        p.BookingsEnabled);

    private static BotFaqResponse Map(BotFaq f) => new(
        f.Id, f.Question, f.Answer, f.SortOrder, f.IsEnabled, f.CreatedAt, f.UpdatedAt);

    private static BotLinkResponse Map(BotLink l) => new(
        l.Id, l.Title, l.Url, l.WhenToShare, l.Message, l.SortOrder, l.IsEnabled, l.CreatedAt, l.UpdatedAt);
}
