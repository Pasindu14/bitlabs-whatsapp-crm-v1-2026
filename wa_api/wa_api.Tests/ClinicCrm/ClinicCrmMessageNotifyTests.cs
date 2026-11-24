using Microsoft.Extensions.Logging.Abstractions;
using wa_api.Features.ClinicCrm;
using wa_api.Features.ClinicCrm.Client;
using wa_api.Features.ClinicCrm.Jobs;
using wa_api.Features.Webhooks.Payloads;
using Xunit;

namespace wa_api.Tests.ClinicCrm;

/// <summary>What counts as a message worth telling the clinic about, and the job that tells it.</summary>
public class ClinicCrmMessageNotifyTests
{
    private static WebhookInboundMessage Text(string body) => new() { Type = "text", Text = new WebhookText { Body = body } };

    [Theory]
    [InlineData("Hi, I want to book a PRP session")]
    [InlineData("How much is botox?")]
    [InlineData("Hi")]
    [InlineData("yes")]
    [InlineData("tomorrow 4pm")]
    [InlineData("مرحبا")]
    public void RealText_IsMeaningful(string body) =>
        Assert.True(ClinicCrmMessageRelevance.IsMeaningful(Text(body)));

    [Theory]
    [InlineData("STOP")]
    [InlineData("stop.")]
    [InlineData("Unsubscribe")]
    [InlineData("ok")]
    [InlineData("Ok 👍")]
    [InlineData("Thank you!!")]
    [InlineData("👍")]
    [InlineData("...")]
    [InlineData("   ")]
    public void StopAcknowledgementsAndEmoji_AreNotMeaningful(string body) =>
        Assert.False(ClinicCrmMessageRelevance.IsMeaningful(Text(body)));

    [Fact]
    public void ATappedStopButton_IsNotMeaningful() =>
        Assert.False(ClinicCrmMessageRelevance.IsMeaningful(
            new WebhookInboundMessage { Type = "button", Button = new WebhookButton { Text = "Stop", Payload = "STOP_OPTOUT" } }));

    [Fact]
    public void ATappedBookingButton_IsMeaningful() =>
        Assert.True(ClinicCrmMessageRelevance.IsMeaningful(
            new WebhookInboundMessage { Type = "button", Button = new WebhookButton { Text = "Book now" } }));

    [Fact]
    public void Media_IsMeaningful_EvenWithoutACaption_ButAStickerIsNot()
    {
        Assert.True(ClinicCrmMessageRelevance.IsMeaningful(
            new WebhookInboundMessage { Type = "image", Image = new WebhookMedia { Id = "m1" } }));
        Assert.True(ClinicCrmMessageRelevance.IsMeaningful(
            new WebhookInboundMessage { Type = "audio", Audio = new WebhookMedia { Id = "m2", Voice = true } }));
        Assert.False(ClinicCrmMessageRelevance.IsMeaningful(
            new WebhookInboundMessage { Type = "sticker", Sticker = new WebhookMedia { Id = "m3" } }));
    }

    [Theory]
    [InlineData("reaction")]
    [InlineData("unsupported")]
    [InlineData("system")]
    public void ContentlessEvents_AreNotMeaningful(string type) =>
        Assert.False(ClinicCrmMessageRelevance.IsMeaningful(new WebhookInboundMessage { Type = type }));

    [Fact]
    public void ALocation_IsMeaningful() =>
        Assert.True(ClinicCrmMessageRelevance.IsMeaningful(new WebhookInboundMessage { Type = "location" }));

    // ── the job ─────────────────────────────────────────────────────────────────────────────

    private static ClinicCrmMessageJob Job(ClinicCrmTestKit kit, FakeClinicCrmBookings crm) =>
        new(kit.Tenant, crm, NullLogger<ClinicCrmMessageJob>.Instance);

    [Fact]
    public async Task SwitchOn_NotifiesTheNumber()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit) { NotifyEndpoint = kit.Endpoint };

        await Job(kit, crm).NotifyAsync(kit.CompanyId, "971501234567", "Sarah", CancellationToken.None);

        Assert.Equal("971501234567", Assert.Single(crm.MessageNotifications));
        Assert.Empty(crm.SavedCustomers);
    }

    [Fact]
    public async Task SwitchedOffAfterQueueing_SendsNothing()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit) { NotifyEndpoint = null };

        await Job(kit, crm).NotifyAsync(kit.CompanyId, "971501234567", "Sarah", CancellationToken.None);

        Assert.Empty(crm.MessageNotifications);
    }

    [Fact]
    public async Task AnUnknownNumber_IsSavedFirst_ThenNotifiedOnceMore()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit) { NotifyEndpoint = kit.Endpoint };
        crm.NotifyReplies.Enqueue(ClinicCrmOutcome.NotFound);
        crm.NotifyReplies.Enqueue(ClinicCrmOutcome.Ok);

        await Job(kit, crm).NotifyAsync(kit.CompanyId, "971501234567", "Sarah", CancellationToken.None);

        Assert.Equal(2, crm.MessageNotifications.Count);
        Assert.Equal(("Sarah", "971501234567"), Assert.Single(crm.SavedCustomers));
    }

    [Fact]
    public async Task CrmUnreachable_Throws_SoHangfireRetries()
    {
        var kit = await ClinicCrmTestKit.CreateAsync();
        var crm = new FakeClinicCrmBookings(kit) { NotifyEndpoint = kit.Endpoint };
        crm.NotifyReplies.Enqueue(ClinicCrmOutcome.Unavailable);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Job(kit, crm).NotifyAsync(kit.CompanyId, "971501234567", null, CancellationToken.None));
    }
}
