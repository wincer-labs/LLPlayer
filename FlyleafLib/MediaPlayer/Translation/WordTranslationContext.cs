namespace FlyleafLib.MediaPlayer.Translation;

/// <summary>Source-language subtitle context for a single word lookup.</summary>
public sealed record WordTranslationContext(string Sentence, string[] Previous, string[] Following)
{
    public static WordTranslationContext Create(IReadOnlyList<SubtitleData> subtitles,
        int index, string sentence, int surroundingCount)
    {
        if (index < 0 || index >= subtitles.Count)
            return new(sentence, [], []);

        int count = Math.Clamp(surroundingCount, 0, 10);
        List<string> previous = [], following = [];
        for (int i = Math.Max(0, index - count); i < index; i++)
            if (!string.IsNullOrWhiteSpace(subtitles[i].Text))
                previous.Add(subtitles[i].Text!);
        for (int i = index + 1; i < Math.Min(subtitles.Count, index + count + 1); i++)
            if (!string.IsNullOrWhiteSpace(subtitles[i].Text))
                following.Add(subtitles[i].Text!);

        return new(sentence, previous.ToArray(), following.ToArray());
    }
}
