using Microsoft.EntityFrameworkCore;
using wa_api.Common.Errors;
using wa_api.Features.Bot.Dtos;
using wa_api.Features.Bot.Entities;
using wa_api.Infrastructure.Persistence;

namespace wa_api.Features.Bot;

/// <summary>
/// CRUD over a company's auto-reply rules. The global query filter scopes every read and write to the
/// caller's company, so nothing here writes a manual <c>WHERE CompanyId</c>; the AuditInterceptor stamps
/// <c>CompanyId</c> on insert.
/// </summary>
public class AutoReplyRuleService(AppDbContext db) : IAutoReplyRuleService
{
    public async Task<IReadOnlyList<AutoReplyRuleResponse>> GetAllAsync(CancellationToken ct = default)
    {
        // Same ordering the engine applies at match time, so the list reads in evaluation order.
        return await db.AutoReplyRules
            .AsNoTracking()
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .Select(r => new AutoReplyRuleResponse(
                r.Id, r.Name, r.Trigger, r.Terms, r.ReplyBody, r.Priority, r.IsEnabled,
                r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<AutoReplyRuleResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.AutoReplyRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("AutoReplyRule", id);

        return Map(rule);
    }

    public async Task<AutoReplyRuleResponse> CreateAsync(
        CreateAutoReplyRuleRequest request, CancellationToken ct = default)
    {
        var terms = NormalizeTerms(request.Trigger, request.Terms);

        if (await db.AutoReplyRules.AnyAsync(r => r.Name == request.Name.Trim(), ct))
            throw new DuplicateResourceException("AutoReplyRule");

        var rule = new AutoReplyRule
        {
            Name = request.Name.Trim(),
            Trigger = request.Trigger,
            Terms = terms,
            ReplyBody = request.ReplyBody.Trim(),
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
        };

        db.AutoReplyRules.Add(rule);
        await db.SaveChangesAsync(ct);

        return Map(rule);
    }

    public async Task<AutoReplyRuleResponse> UpdateAsync(
        Guid id, UpdateAutoReplyRuleRequest request, CancellationToken ct = default)
    {
        var rule = await db.AutoReplyRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("AutoReplyRule", id);

        var terms = NormalizeTerms(request.Trigger, request.Terms);

        if (await db.AutoReplyRules.AnyAsync(r => r.Name == request.Name.Trim() && r.Id != id, ct))
            throw new DuplicateResourceException("AutoReplyRule");

        rule.Name = request.Name.Trim();
        rule.Trigger = request.Trigger;
        rule.Terms = terms;
        rule.ReplyBody = request.ReplyBody.Trim();
        rule.Priority = request.Priority;
        rule.IsEnabled = request.IsEnabled;

        await db.SaveChangesAsync(ct);

        return Map(rule);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.AutoReplyRules.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("AutoReplyRule", id);

        rule.IsActive = false;               // soft delete — rows are never physically removed
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Welcome and Fallback match on position, not content, so their terms are dropped rather than
    /// stored and silently ignored. Every other trigger needs at least one non-blank term — a
    /// content rule with no terms can never fire, which is a misconfiguration worth rejecting up front.
    /// </summary>
    private static List<string> NormalizeTerms(AutoReplyTrigger trigger, IReadOnlyList<string>? terms)
    {
        if (trigger is AutoReplyTrigger.Welcome or AutoReplyTrigger.Fallback)
            return [];

        var cleaned = (terms ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
            throw new BusinessRuleException("TERMS_REQUIRED",
                $"A {trigger} rule needs at least one trigger term.");

        return cleaned;
    }

    private static AutoReplyRuleResponse Map(AutoReplyRule r) => new(
        r.Id, r.Name, r.Trigger, r.Terms, r.ReplyBody, r.Priority, r.IsEnabled, r.CreatedAt, r.UpdatedAt);
}
