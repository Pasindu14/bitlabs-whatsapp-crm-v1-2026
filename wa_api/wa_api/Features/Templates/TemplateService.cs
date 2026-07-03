using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Common.OptOut;
using wa_api.Features.Notifications;
using wa_api.Features.Notifications.Entities;
using wa_api.Features.Templates.Dtos;
using wa_api.Features.Templates.Entities;
using wa_api.Features.WhatsApp.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Templates;

public partial class TemplateService(AppDbContext db, IMetaTemplateClient meta, INotificationService notifications) : ITemplateService
{
    public async Task<(IReadOnlyList<TemplateResponse> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? search, string? status, string? sortBy, string? sortOrder,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Global query filter already scopes this to the caller's company.
        var query = db.Templates.AsNoTracking().Include(t => t.WabaConnection).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TemplateStatus>(status, ignoreCase: true, out var parsed))
            query = query.Where(t => t.Status == parsed);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, term) ||
                EF.Functions.ILike(t.Language, term));
        }

        var desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => desc ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            "language" => desc ? query.OrderByDescending(t => t.Language) : query.OrderBy(t => t.Language),
            "status" => desc ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            _ => desc ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        // Materialize then map: Components is a jsonb value-converted property, mapped in memory.
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (rows.Select(t => Map(t, t.WabaConnection?.DisplayPhoneNumber)).ToList(), total);
    }

    public async Task<TemplateResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.Templates.AsNoTracking()
            .Include(t => t.WabaConnection)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template", id);
        return Map(template, template.WabaConnection?.DisplayPhoneNumber);
    }

    public async Task<TemplateResponse> CreateAsync(CreateTemplateRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim().ToLowerInvariant();
        var language = request.Language.Trim();

        // The connection must exist and be visible to the caller's company (query filter enforces tenancy).
        var conn = await db.WabaConnections.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WabaConnectionId, ct)
            ?? throw new NotFoundException("WabaConnection", request.WabaConnectionId);

        Validate(name, request.Components);

        // Uniqueness is per-company (query filter) on (Name, Language) — same name, other language is OK.
        if (await db.Templates.AnyAsync(t => t.Name == name && t.Language == language, ct))
            throw new ConflictException("TEMPLATE_DUPLICATE",
                "A template with this name and language already exists.");

        var template = new Template
        {
            WabaConnectionId = conn.Id,
            WabaId = conn.WabaId,
            Name = name,
            Language = language,
            Category = TemplateCategory.Marketing,
            ParameterFormat = TemplateParameterFormat.Positional,
            Components = Normalize(request.Components),
            Status = TemplateStatus.Draft,
            // CompanyId is auto-stamped from tenant context by AuditInterceptor on insert.
        };
        db.Templates.Add(template);
        await db.SaveChangesAsync(ct);

        return Map(template, conn.DisplayPhoneNumber);
    }

    public async Task<TemplateResponse> UpdateAsync(Guid id, UpdateTemplateRequest request, CancellationToken ct = default)
    {
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template", id);

        if (template.Status != TemplateStatus.Draft)
            throw new BusinessRuleException("TEMPLATE_NOT_EDITABLE",
                "Only draft templates can be edited. Delete and recreate to change a submitted template.");

        var name = request.Name.Trim().ToLowerInvariant();
        var language = request.Language.Trim();

        var conn = await db.WabaConnections.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WabaConnectionId, ct)
            ?? throw new NotFoundException("WabaConnection", request.WabaConnectionId);

        Validate(name, request.Components);

        // Uniqueness on (Name, Language) must exclude the row being edited.
        if (await db.Templates.AnyAsync(t => t.Id != id && t.Name == name && t.Language == language, ct))
            throw new ConflictException("TEMPLATE_DUPLICATE",
                "A template with this name and language already exists.");

        template.WabaConnectionId = conn.Id;
        template.WabaId = conn.WabaId;
        template.Name = name;
        template.Language = language;
        template.Components = Normalize(request.Components);
        await db.SaveChangesAsync(ct);

        return Map(template, conn.DisplayPhoneNumber);
    }

    public async Task<TemplateResponse> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.Templates
            .Include(t => t.WabaConnection)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template", id);

        // Best-effort: also remove it from Meta once submitted (failures are logged, not fatal —
        // the local row is the source of truth for the UI).
        if (template.MetaTemplateId is not null && template.WabaConnection is not null)
            await meta.DeleteAsync(template.WabaId, template.Name, template.WabaConnection.EncryptedAccessToken, ct);

        template.IsActive = false;
        await db.SaveChangesAsync(ct);

        return Map(template, template.WabaConnection?.DisplayPhoneNumber);
    }

    public async Task<TemplateResponse> SubmitAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.Templates
            .Include(t => t.WabaConnection)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template", id);

        if (template.Status != TemplateStatus.Draft)
            throw new BusinessRuleException("TEMPLATE_NOT_SUBMITTABLE",
                "Only draft templates can be submitted to WhatsApp.");

        var conn = template.WabaConnection
            ?? throw new NotFoundException("WabaConnection", template.WabaConnectionId);

        if (!conn.IsActive || conn.Status != WabaConnectionStatus.Connected)
            throw new BusinessRuleException("WABA_INACTIVE",
                "The WhatsApp connection for this template is inactive.");

        // Guarantee a one-tap opt-out on every marketing template before it goes to Meta. Mutating the
        // jsonb Components tree is picked up by its deep ValueComparer, so the SaveChanges below persists it.
        EnsureOptOutButton(template);

        // Re-validate the structure before spending a network call.
        Validate(template.Name, template.Components);

        var payload = TemplateMapper.ToCreatePayload(template);
        var result = await meta.CreateAsync(template.WabaId, conn.EncryptedAccessToken, payload, ct);

        if (result.MetaTemplateId is null)
            throw new BusinessRuleException("TEMPLATE_SUBMIT_FAILED",
                result.Error ?? "Failed to submit the template to WhatsApp.");

        template.MetaTemplateId = result.MetaTemplateId;
        template.Status = TemplateStatusMapper.Parse(result.Status) ?? TemplateStatus.Pending;
        template.RejectionReason = null;
        template.SubmittedAt = DateTime.UtcNow;
        template.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Map(template, conn.DisplayPhoneNumber);
    }

    public async Task<TemplateResponse> RefreshStatusAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.Templates
            .Include(t => t.WabaConnection)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template", id);

        if (template.MetaTemplateId is null || template.WabaConnection is null)
            throw new BusinessRuleException("TEMPLATE_NOT_SUBMITTED",
                "This template has not been submitted to WhatsApp yet.");

        var status = await meta.GetStatusAsync(template.MetaTemplateId, template.WabaConnection.EncryptedAccessToken, ct);
        if (status is not null)
        {
            var prevStatus = template.Status;
            TemplateStatusMapper.Apply(template, status, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);

            if (prevStatus != TemplateStatus.Approved && template.Status == TemplateStatus.Approved)
                await notifications.CreateAsync(
                    template.CompanyId, null, template.Id,
                    NotificationType.TemplateApproved,
                    $"Template \"{template.Name}\" has been approved",
                    "Your WhatsApp template is ready to use in campaigns.",
                    ct);
        }

        return Map(template, template.WabaConnection.DisplayPhoneNumber);
    }

    // ── Validation ──────────────────────────────────────────────────────────

    [GeneratedRegex(@"^[a-z0-9_]+$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\{\{(\d+)\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^\+[1-9]\d{1,14}$")]
    private static partial Regex E164Regex();

    /// <summary>
    /// Structural validation that mirrors Meta's rules so we fail fast before any network call:
    /// name format, mandatory body, contiguous positional variables, and a sample value for every
    /// variable (body, header text, dynamic URL) + a media handle for media headers.
    /// </summary>
    private static void Validate(string name, TemplateComponents c)
    {
        var fields = new Dictionary<string, string[]>();

        if (!NameRegex().IsMatch(name))
            fields["name"] = ["Name must be lowercase letters, numbers and underscores only (no spaces)."];

        var body = c.Body?.Text?.Trim() ?? string.Empty;
        // Meta rejects a body with no literal text, more than two consecutive newlines, or more
        // than 10 emojis (its error OR's all three). Mirror the web schema's precedence so the
        // message the user sees is the same whether the draft is saved or submitted.
        if (string.IsNullOrWhiteSpace(body))
        {
            fields["components.body.text"] = ["Body text is required."];
        }
        else if (PlaceholderRegex().Replace(body, "").Trim().Length == 0)
        {
            fields["components.body.text"] = ["The body needs some text, not just variables."];
        }
        else if (body.Replace("\r\n", "\n").Replace("\r", "\n").Contains("\n\n\n"))
        {
            fields["components.body.text"] = ["Remove the extra line breaks — no more than two newlines in a row."];
        }
        else if (EmojiCount(body) > 10)
        {
            fields["components.body.text"] = ["Use at most 10 emojis in the body."];
        }
        else
        {
            var indices = PlaceholderRegex().Matches(body)
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct().OrderBy(n => n).ToList();

            // Meta's variable-density rule: (words + variables) ≥ 3·variables + 1. Splitting on
            // null splits on all whitespace (incl. newlines) and RemoveEmptyEntries drops blanks,
            // so a stray space can't inflate the count. indices is the distinct variable count.
            var tokenCount = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            var minTokens = 3 * indices.Count + 1;
            if (!indices.SequenceEqual(Enumerable.Range(1, indices.Count)))
                fields["components.body.text"] = ["Body variables must be numbered consecutively starting at {{1}}."];
            else if (indices.Count > 0 && tokenCount < minTokens)
            {
                var deficit = minTokens - tokenCount;
                fields["components.body.text"] =
                    [$"Too many variables for the message length — add {deficit} more word{(deficit == 1 ? "" : "s")} or remove a variable."];
            }
            else
            {
                var examples = c.Body!.Examples ?? [];
                if (examples.Count != indices.Count || examples.Any(string.IsNullOrWhiteSpace))
                    fields["components.body.examples"] = [$"Provide an example value for each of the {indices.Count} body variable(s)."];
            }
        }

        // Reject unknown discriminators loudly (a web/server casing mismatch would otherwise build a
        // malformed Meta payload that only fails at submit time).
        if (c.Header is { } hdr && hdr.Type is not ("none" or "text" or "image" or "video" or "document" or "location"))
            fields["components.header.type"] = [$"Unsupported header type '{hdr.Type}'."];

        if (c.Header is { Type: "text" } h && !string.IsNullOrWhiteSpace(h.Text)
            && PlaceholderRegex().IsMatch(h.Text!) && string.IsNullOrWhiteSpace(h.TextExample))
            fields["components.header.example"] = ["Provide an example value for the header variable."];

        if (c.Header is { Type: "image" or "video" or "document" } m && string.IsNullOrWhiteSpace(m.MediaHandle))
            fields["components.header.media"] = ["Upload a sample media file for the media header."];

        var buttons = c.Buttons ?? [];
        if (buttons.Count > 10)
            fields["components.buttons"] = ["A template can have at most 10 buttons."];

        for (var i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            if (b.Type is not ("quick_reply" or "url" or "phone_number" or "copy_code"))
                fields[$"components.buttons[{i}].type"] = [$"Unsupported button type '{b.Type}'."];
            if ((b.Type is "quick_reply" or "url" or "phone_number") && string.IsNullOrWhiteSpace(b.Text))
                fields[$"components.buttons[{i}].text"] = ["Button text is required."];
            if (b.Type == "url" && string.IsNullOrWhiteSpace(b.Url))
                fields[$"components.buttons[{i}].url"] = ["A URL button needs a URL."];
            if (b.Type == "url" && !string.IsNullOrWhiteSpace(b.Url)
                && PlaceholderRegex().IsMatch(b.Url!) && string.IsNullOrWhiteSpace(b.UrlExample))
                fields[$"components.buttons[{i}].example"] = ["Provide an example for the dynamic URL."];
            if (b.Type == "phone_number")
            {
                if (string.IsNullOrWhiteSpace(b.PhoneNumber))
                    fields[$"components.buttons[{i}].phoneNumber"] = ["A call button needs a phone number."];
                else if (!E164Regex().IsMatch(NormalizePhone(b.PhoneNumber)))
                    fields[$"components.buttons[{i}].phoneNumber"] =
                        ["Use international format, e.g. +14155552671 — country code, no spaces."];
            }
        }

        if (fields.Count > 0)
            throw new ValidationException(fields);
    }

    /// <summary>
    /// Counts rendered emoji (grapheme clusters) so a compound emoji — a ZWJ family or a skin-tone
    /// variant — counts once, not once per code point. Mirrors the web schema's grapheme count.
    /// Deliberately lenient (flags/keycaps aren't matched) — better to let Meta catch a rare case
    /// than block a valid template.
    /// </summary>
    private static int EmojiCount(string text)
    {
        var count = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            foreach (var rune in ((string)elements.Current).EnumerateRunes())
            {
                // Supplementary emoji planes (≥ U+1F000) or BMP "Symbol, other" (☀ ❤ ⭐ …).
                if (rune.Value >= 0x1F000 || Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol)
                {
                    count++;
                    break; // count each grapheme at most once
                }
            }
        }
        return count;
    }

    /// <summary>Keeps only '+' and digits so "+94 77 123-4567" → "+94771234567" (matches the web schema).</summary>
    private static string NormalizePhone(string s) =>
        new(s.Where(ch => ch == '+' || char.IsDigit(ch)).ToArray());

    /// <summary>Trims user text so stored components are clean; leaves variable tokens intact.</summary>
    private static TemplateComponents Normalize(TemplateComponents c)
    {
        c.Body.Text = c.Body.Text?.Trim() ?? string.Empty;
        c.Body.Examples = (c.Body.Examples ?? []).Select(e => e?.Trim() ?? string.Empty).ToList();
        if (c.Footer is not null) c.Footer.Text = c.Footer.Text?.Trim() ?? string.Empty;
        if (c.Header is { Type: "none" }) c.Header = null;
        foreach (var b in c.Buttons ?? [])
            if (b.Type == "phone_number" && !string.IsNullOrWhiteSpace(b.PhoneNumber))
                b.PhoneNumber = NormalizePhone(b.PhoneNumber);
        return c;
    }

    /// <summary>
    /// Guarantees a one-tap opt-out on every MARKETING template before submission and pins it to the
    /// BOTTOM of the button stack. If no quick-reply button already carries an opt-out keyword and
    /// there's room under Meta's 10-button cap, a "Stop" quick-reply is added. Buttons are then
    /// reordered so call-to-action buttons stay on top and every quick-reply sits below them, with the
    /// opt-out button last — Meta requires quick-reply buttons to be a contiguous group and renders
    /// buttons in array order, so this keeps "Stop" as the final button rather than above a CTA.
    /// Utility/auth templates are transactional and left untouched. Idempotent.
    /// </summary>
    private static void EnsureOptOutButton(Template t)
    {
        if (t.Category != TemplateCategory.Marketing) return;

        var buttons = t.Components.Buttons;

        var stop = buttons.FirstOrDefault(b => b.Type == "quick_reply" && OptOutKeywords.IsStopIntent(b.Text));
        if (stop is null)
        {
            if (buttons.Count >= 10) return; // no room; let the operator manage buttons manually
            stop = new TemplateButton { Type = "quick_reply", Text = OptOutKeywords.StopButtonText };
        }

        // Rebuild in place: CTAs first (stable order), then any other quick-replies, then the opt-out last.
        var ctas = buttons.Where(b => b.Type != "quick_reply").ToList();
        var quickReplies = buttons.Where(b => b.Type == "quick_reply" && !ReferenceEquals(b, stop)).ToList();

        buttons.Clear();
        buttons.AddRange(ctas);
        buttons.AddRange(quickReplies);
        buttons.Add(stop);
    }

    private static TemplateResponse Map(Template t, string? displayPhoneNumber)
        => new(
            t.Id, t.WabaConnectionId, displayPhoneNumber, t.WabaId,
            t.Name, t.Language, t.Category, t.ParameterFormat, t.Components,
            t.Status, t.MetaTemplateId, t.RejectionReason, t.SubmittedAt, t.ApprovedAt, t.LastSyncedAt,
            t.IsActive, t.CreatedAt);
}
