using wa_api.Features.Campaigns.Jobs;
using wa_api.Features.Messages;
using Xunit;

namespace wa_api.Tests.Campaigns;

/// <summary>
/// Pins the campaign send-response error-code parse. Meta returns <c>error.code</c> as a JSON NUMBER,
/// and a prior <c>GetString()</c> threw on that shape and left the code null — which silently defeated
/// every policy classifier (spam pause, account restriction, transient throttle, undeliverable), so a
/// number flagged for spam kept blasting instead of pausing. These tests fail if that regression returns.
/// </summary>
public class MetaErrorCodeParseTests
{
    [Fact]
    public void ParsesNumericCode_TheShapeMetaActuallyReturns()
    {
        // Real Meta spam-rate-limit body: code is a bare number, not a quoted string.
        const string body = """{"error":{"message":"Spam rate limit hit","type":"OAuthException","code":131048}}""";

        var code = CampaignBatchSendJob.ParseMetaErrorCode(body);

        Assert.Equal("131048", code);
        Assert.True(MetaPolicyErrorCodes.IsCampaignPause(code),
            "a spam-rate-limit (131048) response must flow through to pausing the campaign");
    }

    [Theory]
    [InlineData(368, true, false, false, false)]     // account restricted → stop all sends
    [InlineData(131031, true, false, false, false)]  // account suspended  → stop all sends
    [InlineData(131048, false, true, false, false)]  // spam rate limit    → pause campaign
    [InlineData(131049, false, true, false, false)]  // ecosystem limit    → pause campaign
    [InlineData(130429, false, false, true, false)]  // throughput throttle→ back off + retry
    [InlineData(131056, false, false, true, false)]  // pair rate limit    → back off + retry
    [InlineData(131026, false, false, false, true)]  // undeliverable      → mark contact invalid
    public void NumericCode_RoutesToTheRightPolicyClassifier(
        int metaCode, bool restriction, bool pause, bool throttle, bool undeliverable)
    {
        var body = "{\"error\":{\"message\":\"x\",\"code\":" + metaCode + "}}";

        var code = CampaignBatchSendJob.ParseMetaErrorCode(body);

        Assert.Equal(restriction, MetaPolicyErrorCodes.IsAccountRestriction(code));
        Assert.Equal(pause, MetaPolicyErrorCodes.IsCampaignPause(code));
        Assert.Equal(throttle, MetaPolicyErrorCodes.IsTransientThrottle(code));
        Assert.Equal(undeliverable, MetaPolicyErrorCodes.IsUndeliverableRecipient(code));
    }

    [Fact]
    public void ParsesStringCode_ForForwardCompatibility()
    {
        const string body = """{"error":{"code":"131048"}}""";
        Assert.Equal("131048", CampaignBatchSendJob.ParseMetaErrorCode(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"error":{"message":"no code here"}}""")]
    [InlineData("""{"unexpected":"shape"}""")]
    public void ReturnsNull_OnMissingOrMalformedBody(string body)
    {
        Assert.Null(CampaignBatchSendJob.ParseMetaErrorCode(body));
    }
}
