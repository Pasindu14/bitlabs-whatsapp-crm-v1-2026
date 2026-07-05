using Stripe;
using wa_api.Features.Billing.Jobs;
using Xunit;

namespace wa_api.Tests.Billing;

/// <summary>
/// Pins the invoice.paid period-reset guard (H4). The quota counter must be zeroed only on a genuine renewal
/// cycle — not on Stripe's redeliveries, on a late payment of an OLD invoice, or on a mid-period proration
/// invoice, each of which would otherwise wipe an in-progress period and hand the tenant a free full-quota
/// refill. The rule is two-part (BillingReason + strict period advance); these tests exercise both helpers.
/// </summary>
public class InvoicePeriodResetTests
{
    private const string Cycle = "subscription_cycle";     // the monthly renewal
    private const string Create = "subscription_create";   // first invoice
    private const string Update = "subscription_update";   // mid-cycle proration

    private static readonly DateTime Jan = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Invoice InvoiceWith(params (DateTime start, DateTime end)[] linePeriods) => new()
    {
        Id = "in_" + Guid.NewGuid().ToString("N"),
        Lines = new StripeList<InvoiceLineItem>
        {
            Data = linePeriods
                .Select(p => new InvoiceLineItem
                {
                    Period = new InvoiceLineItemPeriod { Start = p.start, End = p.end },
                })
                .ToList(),
        },
    };

    // ── ShouldResetUsage ──────────────────────────────────────────────────────

    [Fact]
    public void RenewalCycle_NewPeriod_Resets()
    {
        Assert.True(StripeWebhookProcessingJob.ShouldResetUsage(Cycle, recordedPeriodStart: Jan, invoicePeriodStart: Feb));
    }

    [Fact]
    public void RenewalCycle_SamePeriod_DoesNotReset()
    {
        // Redelivery / the first cycle invoice of the period we already recorded: same start → no reset.
        Assert.False(StripeWebhookProcessingJob.ShouldResetUsage(Cycle, recordedPeriodStart: Feb, invoicePeriodStart: Feb));
    }

    [Fact]
    public void LateOldCycleInvoice_DoesNotWipeCurrentPeriod()
    {
        // A late invoice.paid for a PRIOR period arrives after we've advanced to Feb — must not zero Feb.
        Assert.False(StripeWebhookProcessingJob.ShouldResetUsage(Cycle, recordedPeriodStart: Feb, invoicePeriodStart: Jan));
    }

    [Fact]
    public void ProrationInvoice_MidPeriod_DoesNotReset()
    {
        // The dangerous case: a mid-cycle proration invoice's line period starts AFTER the recorded period
        // start (looks "newer"), but BillingReason is subscription_update — the gate must block the reset.
        var midPeriod = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(StripeWebhookProcessingJob.ShouldResetUsage(Update, recordedPeriodStart: Feb, invoicePeriodStart: midPeriod));
    }

    [Fact]
    public void ManualOrOneOffInvoice_DoesNotReset()
    {
        Assert.False(StripeWebhookProcessingJob.ShouldResetUsage("manual", recordedPeriodStart: Jan, invoicePeriodStart: Mar));
        Assert.False(StripeWebhookProcessingJob.ShouldResetUsage(null, recordedPeriodStart: Jan, invoicePeriodStart: Mar));
    }

    [Fact]
    public void CreateInvoice_NewPeriod_Resets()
    {
        Assert.True(StripeWebhookProcessingJob.ShouldResetUsage(Create, recordedPeriodStart: Jan, invoicePeriodStart: Feb));
    }

    [Fact]
    public void NoInvoicePeriod_DoesNotReset()
    {
        Assert.False(StripeWebhookProcessingJob.ShouldResetUsage(Cycle, recordedPeriodStart: Feb, invoicePeriodStart: null));
    }

    // ── InvoiceBillingPeriod ──────────────────────────────────────────────────

    [Fact]
    public void RenewalInvoice_YieldsItsPeriod()
    {
        var period = StripeWebhookProcessingJob.InvoiceBillingPeriod(InvoiceWith((Mar, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc))));

        Assert.NotNull(period);
        Assert.Equal(Mar, period!.Value.Start);
    }

    [Fact]
    public void MixedInvoice_PicksLatestLinePeriod_SoRenewalDrivesTheDecision()
    {
        // A proration line inside the current period + the renewal line for the next period. The latest start
        // (the renewal) must win so a real renewal on a mixed invoice still resets.
        var currentStart = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var currentEnd = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var renewalStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var renewalEnd = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        var period = StripeWebhookProcessingJob.InvoiceBillingPeriod(
            InvoiceWith((currentStart, currentEnd), (renewalStart, renewalEnd)));

        Assert.Equal(renewalStart, period!.Value.Start);
        Assert.Equal(renewalEnd, period.Value.End);
    }

    [Fact]
    public void EmptyInvoice_YieldsNull()
    {
        Assert.Null(StripeWebhookProcessingJob.InvoiceBillingPeriod(InvoiceWith()));
    }
}
