using wa_api.Features.Bot;

namespace wa_api.Tests.Bot;

/// <summary>A gate whose answers the test sets directly.</summary>
internal sealed class TestBotGate(bool live = true, bool clinic = false) : IBotGate
{
    public bool Live { get; set; } = live;
    public bool Clinic { get; set; } = clinic;
    public int LiveChecks { get; private set; }

    public bool GloballyEnabled => true;
    public BotOptions Options { get; } = new() { Enabled = true };

    public Task<bool> IsEntitledAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult(Live);

    public Task<bool> IsLiveAsync(Guid companyId, CancellationToken ct = default)
    {
        LiveChecks++;
        return Task.FromResult(Live);
    }

    public Task<bool> HasClinicCrmAsync(Guid companyId, CancellationToken ct = default) => Task.FromResult(Clinic);

    public bool ClinicNotify { get; set; }

    public Task<bool> NotifiesClinicCrmOfMessagesAsync(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(Clinic && ClinicNotify);

    public void Forget(Guid companyId) { }
}
