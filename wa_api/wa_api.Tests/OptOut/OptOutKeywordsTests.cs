using wa_api.Common.OptOut;
using Xunit;

namespace wa_api.Tests.OptOut;

/// <summary>
/// The opt-out vocabulary is shared by the inbound webhook (detects a STOP) and the template service
/// (labels the default quick-reply button). These tests pin the matching rules and — critically — the
/// invariant that the button label we ship is itself recognized as a stop when the recipient taps it.
/// </summary>
public class OptOutKeywordsTests
{
    [Theory]
    [InlineData("stop")]
    [InlineData("STOP")]
    [InlineData(" Stop ")]       // trimmed + case-insensitive
    [InlineData("stopall")]
    [InlineData("stop all")]
    [InlineData("unsubscribe")]
    [InlineData("optout")]
    [InlineData("opt out")]
    [InlineData("STOP!")]        // surrounding punctuation tolerated (L2)
    [InlineData("(stop)")]
    [InlineData("stop.")]
    [InlineData("stop all.")]
    public void IsStopIntent_True_ForOptOutKeywords(string text) =>
        Assert.True(OptOutKeywords.IsStopIntent(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Track order")]         // a normal quick-reply button label
    [InlineData("cancel my order")]     // broad verb deliberately excluded
    [InlineData("please stop sending")] // not an exact match
    public void IsStopIntent_False_ForEverythingElse(string? text) =>
        Assert.False(OptOutKeywords.IsStopIntent(text));

    [Fact]
    public void StopButtonText_IsRecognizedAsAStop()
    {
        // The default template button label MUST match the detector — otherwise tapping the very button
        // we add would not opt the contact out. This is the button↔webhook contract in one assertion.
        Assert.True(OptOutKeywords.IsStopIntent(OptOutKeywords.StopButtonText));
    }
}
