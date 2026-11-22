using wa_api.Features.Bot.Ai;
using Xunit;

namespace wa_api.Tests.Bot;

/// <summary>
/// Pins the shape of what reaches a customer's phone. The prompt asks the model for one or two short
/// messages; this is the part that guarantees it, so a model that ignores the instruction still cannot
/// send a wall of text or a five-message burst.
/// </summary>
public class BubbleSplitterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n  \n")]
    public void EmptyInput_ProducesNothingToSend(string? reply)
    {
        // Nothing sendable is a deliberate non-reply, not an error — the job stays silent.
        Assert.Empty(BubbleSplitter.Split(reply));
    }

    [Fact]
    public void SingleParagraph_StaysOneBubble()
    {
        var result = BubbleSplitter.Split("Yes, we're open till 6 today.");

        Assert.Single(result);
        Assert.Equal("Yes, we're open till 6 today.", result[0]);
    }

    [Fact]
    public void BlankLine_SplitsIntoTwoBubbles()
    {
        var result = BubbleSplitter.Split("Yes, we do that.\n\nWant me to check this week?");

        Assert.Equal(2, result.Count);
        Assert.Equal("Yes, we do that.", result[0]);
        Assert.Equal("Want me to check this week?", result[1]);
    }

    [Fact]
    public void WindowsLineEndings_SplitTheSameWay()
    {
        var result = BubbleSplitter.Split("First.\r\n\r\nSecond.");

        Assert.Equal(2, result.Count);
        Assert.Equal("Second.", result[1]);
    }

    [Fact]
    public void SingleNewline_IsNotASplit()
    {
        // Only a blank line separates messages. A soft wrap is still one message.
        var result = BubbleSplitter.Split("Line one\nline two");

        Assert.Single(result);
        Assert.Equal("Line one\nline two", result[0]);
    }

    [Fact]
    public void MoreThanTwoParagraphs_FoldIntoTheSecondBubble()
    {
        // The tail is joined rather than dropped: the model's closing line is usually the question
        // that moves the sale, and discarding it would leave the customer nothing to answer.
        var result = BubbleSplitter.Split("One.\n\nTwo.\n\nThree.\n\nFour?");

        Assert.Equal(2, result.Count);
        Assert.Equal("One.", result[0]);
        Assert.Equal("Two. Three. Four?", result[1]);
    }

    [Fact]
    public void OverlongBubble_IsTruncatedAtAWordBoundary()
    {
        var reply = string.Join(" ", Enumerable.Repeat("word", 200));   // ~1000 chars

        var result = BubbleSplitter.Split(reply);

        Assert.Single(result);
        Assert.True(result[0].Length <= BubbleSplitter.MaxBubbleLength + 1);   // +1 for the ellipsis
        Assert.EndsWith("…", result[0]);
        Assert.DoesNotContain("wor…", result[0]);                              // never mid-word
    }

    [Fact]
    public void OverlongBubbleWithNoLateSpace_IsClippedRatherThanGutted()
    {
        // A long unbroken run (a URL) has no word boundary near the cap. Falling back to the last
        // space anywhere would cut the message to a fraction of its length, so it is clipped instead.
        var reply = "See " + new string('x', 600);

        var result = BubbleSplitter.Split(reply);

        Assert.Single(result);
        Assert.True(result[0].Length > BubbleSplitter.MaxBubbleLength * 3 / 4);
    }

    [Fact]
    public void BothBubbles_AreIndependentlyCapped()
    {
        var long1 = new string('a', 500);
        var long2 = new string('b', 500);

        var result = BubbleSplitter.Split($"{long1}\n\n{long2}");

        Assert.Equal(2, result.Count);
        Assert.All(result, b => Assert.True(b.Length <= BubbleSplitter.MaxBubbleLength + 1));
    }

    [Fact]
    public void SurroundingWhitespace_IsTrimmedFromEachBubble()
    {
        var result = BubbleSplitter.Split("  padded  \n\n   also padded   ");

        Assert.Equal(["padded", "also padded"], result);
    }
}
