using wa_api.Features.Campaigns;
using Xunit;

namespace wa_api.Tests.Campaigns;

public class SendWindowOptionsTests
{
    private static readonly Guid Reviewer = Guid.NewGuid();

    [Fact]
    public void Listed_company_is_exempt()
    {
        var opts = new SendWindowOptions { ExemptCompanyIds = [Reviewer] };
        Assert.True(opts.IsExempt(Reviewer));
    }

    [Fact]
    public void Unlisted_company_still_respects_the_window()
    {
        var opts = new SendWindowOptions { ExemptCompanyIds = [Reviewer] };
        Assert.False(opts.IsExempt(Guid.NewGuid()));
    }

    [Fact]
    public void No_company_context_is_never_exempt()
    {
        var opts = new SendWindowOptions { ExemptCompanyIds = [Reviewer] };
        Assert.False(opts.IsExempt(null));
    }

    [Fact]
    public void Empty_config_exempts_nobody()
    {
        Assert.False(new SendWindowOptions().IsExempt(Reviewer));
    }
}
