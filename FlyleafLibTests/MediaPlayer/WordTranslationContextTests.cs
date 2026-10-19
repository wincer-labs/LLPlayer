using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib.MediaPlayer;

public class WordTranslationContextTests
{
    [Fact]
    public void UsesClickedRowRatherThanPlaybackPosition()
    {
        Utils.IsTesting = true;
        SubManager manager = new(new Config(true), 0);
        manager.Load(Enumerable.Range(0, 7).Select(i => new SubtitleData
        {
            Text = $"Sentence {i}", StartTime = TimeSpan.FromSeconds(i * 10), EndTime = TimeSpan.FromSeconds(i * 10 + 5)
        }).ToList());
        manager.SetCurrentTime(TimeSpan.FromSeconds(1));

        var context = manager.GetWordTranslationContext(4, "Sentence 4", 2);
        Assert.Equal(["Sentence 2", "Sentence 3"], context.Previous);
        Assert.Equal(["Sentence 5", "Sentence 6"], context.Following);
        Assert.Equal("Sentence 4", context.Sentence);
    }

    [Fact]
    public void HandlesTrackBoundariesAndMissingText()
    {
        SubtitleData[] subtitles = [new() { Text = "First" }, new(), new() { Text = "Last" }];
        var first = WordTranslationContext.Create(subtitles, 0, "First", 3);
        Assert.Empty(first.Previous);
        Assert.Equal(["Last"], first.Following);
        var last = WordTranslationContext.Create(subtitles, 2, "Last", 3);
        Assert.Equal(["First"], last.Previous);
        Assert.Empty(last.Following);
    }

    [Fact]
    public void KeepsCurrentSentenceWhenNoNeighboringSubtitlesAreRequested()
    {
        var context = WordTranslationContext.Create([new() { Text = "bank" }], 0, "We reached the river bank.", 0);
        Assert.Equal("We reached the river bank.", context.Sentence);
        Assert.Empty(context.Previous);
        Assert.Empty(context.Following);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20)]
    public void MissingSubtitleIndexDoesNotAttachUnrelatedSentences(int index)
    {
        var context = WordTranslationContext.Create([new() { Text = "Unrelated" }], index, "Current", 3);
        Assert.Equal("Current", context.Sentence);
        Assert.Empty(context.Previous);
        Assert.Empty(context.Following);
    }
}
