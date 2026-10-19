using System.Text.Json;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib.MediaPlayer;

public class WordTranslationSourceTests
{
    [Theory]
    [InlineData(@"C:\Private\Shows\Example Season\Episode 01.mkv")]
    [InlineData("file:///C:/Private/Shows/Example%20Season/Episode%2001.mkv")]
    [InlineData(@"\\private-server\SecretShare\Example Season\Episode 01.mkv")]
    public void ClassifiesLocalMediaByItsParentDirectoryWithoutLeakingPaths(string path)
    {
        var source = WordTranslationSource.FromMedia(path, 123000);
        Assert.Equal("Example Season", source.Category);
        Assert.Equal("Episode 01.mkv", source.MediaTitle);
        Assert.Equal(123000, source.PositionMs);
        string json = JsonSerializer.Serialize(source);
        Assert.DoesNotContain("Private", json);
        Assert.DoesNotContain("SecretShare", json);
        Assert.DoesNotContain("private-server", json);
        Assert.DoesNotContain("C:", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/movie.mp4")]
    [InlineData("https://secret-user:password@example.com/secret-folder/movie.mp4?token=secret")]
    [InlineData("https://www.youtube.com/watch?v=example")]
    public void MissingOrNetworkMediaFallsBackToLLPlayerWithoutUrlOrSecrets(string? path)
    {
        var source = WordTranslationSource.FromMedia(path);
        Assert.Equal("LLPlayer", source.Category);
        Assert.Null(source.MediaTitle);
        Assert.Null(source.PositionMs);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(source), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileAtDriveRootUsesFallbackCategory()
    {
        var source = WordTranslationSource.FromMedia(@"C:\Episode.mkv");
        Assert.Equal("LLPlayer", source.Category);
        Assert.Equal("Episode.mkv", source.MediaTitle);
    }
}
