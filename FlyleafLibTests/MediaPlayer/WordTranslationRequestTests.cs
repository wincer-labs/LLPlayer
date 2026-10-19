using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer;

public class WordTranslationRequestTests
{
    [Fact]
    public async Task SendsContextAndOnlyRequestsSelectedTextWithoutHistory()
    {
        var messages = await CaptureMessages(new TranslateChatConfig(), context: Context);
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        string prompt = messages[1].GetProperty("content").GetString()!;
        Assert.Contains("The river was quiet.", prompt);
        Assert.Contains("We sat on the bank.", prompt);
        Assert.Contains("We watched the water.", prompt);
        Assert.EndsWith("bank", prompt.Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesIndependentWordPromptWithAndWithoutContext(bool includeContext)
    {
        var messages = await CaptureMessages(new TranslateChatConfig
        {
            PromptWord = "Explain the meaning and usage in {target_lang}; source: {source_lang}; word: {source_text}",
            PromptOneByOne = "Subtitle-only instructions",
            PromptKeepContext = "Subtitle history instructions"
        }, context: includeContext ? Context : null);
        string prompt = messages[messages.GetArrayLength() - 1].GetProperty("content").GetString()!;
        Assert.Contains($"Explain the meaning and usage in {TargetLanguage.ChineseSimplified.DisplayName()}; source: English; word: bank", prompt);
        Assert.DoesNotContain("Subtitle-only instructions", messages.ToString());
        Assert.DoesNotContain("Subtitle history instructions", messages.ToString());
        Assert.DoesNotContain("Return only the translation", messages.ToString());
        Assert.Equal(includeContext ? 2 : 1, messages.GetArrayLength());
        Assert.Equal(includeContext, prompt.Contains("We sat on the bank.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncludesSelectedTextWhenCustomPromptOmitsPlaceholder(bool includeContext)
    {
        var messages = await CaptureMessages(new TranslateChatConfig { PromptWord = "Give a short definition." },
            context: includeContext ? Context : null);
        string prompt = messages[messages.GetArrayLength() - 1].GetProperty("content").GetString()!;
        Assert.Contains("Give a short definition.", prompt);
        Assert.EndsWith("Selected text:\nbank", prompt);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, " \n ")]
    [InlineData(true, " \n ")]
    public async Task BlankWordPromptUsesDefault(bool includeContext, string? promptWord)
    {
        var messages = await CaptureMessages(new TranslateChatConfig
        {
            PromptWord = promptWord!, PromptOneByOne = "Do not use the subtitle prompt"
        }, context: includeContext ? Context : null);
        string prompt = messages[messages.GetArrayLength() - 1].GetProperty("content").GetString()!;
        Assert.Contains("Translate the selected word or phrase from English", prompt);
        Assert.EndsWith("bank", prompt.Trim());
        Assert.DoesNotContain("Do not use the subtitle prompt", prompt);
    }

    [Theory]
    [InlineData(ChatTranslateMethod.OneByOne)]
    [InlineData(ChatTranslateMethod.KeepContext)]
    public async Task SubtitleTranslationKeepsItsOwnPrompt(ChatTranslateMethod method)
    {
        var messages = await CaptureMessages(new TranslateChatConfig
        {
            TranslateMethod = method,
            PromptWord = "Word-only instructions",
            PromptOneByOne = "Translate subtitle: {source_text}",
            PromptKeepContext = "Translate subtitle history"
        }, wordMode: false);
        if (method == ChatTranslateMethod.OneByOne)
        {
            Assert.Equal(1, messages.GetArrayLength());
            Assert.Equal("Translate subtitle: bank", messages[0].GetProperty("content").GetString());
        }
        else
        {
            Assert.Equal(2, messages.GetArrayLength());
            Assert.Equal("Translate subtitle history", messages[0].GetProperty("content").GetString());
            Assert.Equal("bank", messages[1].GetProperty("content").GetString());
        }
        Assert.DoesNotContain("Word-only instructions", messages.ToString());
    }

    [Fact]
    public void WordPromptPersistsIndependentlyAndOlderConfigGetsDefault()
    {
        var oldConfig = JsonSerializer.Deserialize<TranslateChatConfig>("""{"PromptOneByOne":"Custom subtitle prompt"}""")!;
        Assert.Equal(TranslateChatConfig.DefaultPromptWord.ReplaceLineEndings("\n"), oldConfig.PromptWord);
        oldConfig.PromptWord = "Explain {source_text} in {target_lang}.\nGive an example.";
        var restored = JsonSerializer.Deserialize<TranslateChatConfig>(JsonSerializer.Serialize(oldConfig))!;
        Assert.Equal(oldConfig.PromptWord, restored.PromptWord);
        Assert.Equal("Custom subtitle prompt", restored.PromptOneByOne);
    }

    private static WordTranslationContext Context => new("We sat on the bank.", ["The river was quiet."], ["We watched the water."]);

    private static async Task<JsonElement> CaptureMessages(TranslateChatConfig config, bool wordMode = true,
        WordTranslationContext? context = null)
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        var receive = ReplyAndCapture(listener, timeout.Token);
        using OpenAIBaseTranslateService service = new(new OllamaTranslateSettings
        {
            Endpoint = $"http://127.0.0.1:{port}", Model = "test-model"
        }, config, wordMode);
        service.Initialize(Language.English, TargetLanguage.ChineseSimplified);
        var translating = context != null
            ? service.TranslateWordAsync("bank", context, timeout.Token)
            : service.TranslateAsync("bank", timeout.Token);
        Assert.Equal("河岸", await translating);
        var (path, body) = await receive;
        using var payload = JsonDocument.Parse(body);
        Assert.Equal("/v1/chat/completions", path);
        return payload.RootElement.GetProperty("messages").Clone();
    }

    private static async Task<(string Path, string Body)> ReplyAndCapture(TcpListener listener, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var stream = client.GetStream();
        StringBuilder headers = new();
        byte[] single = new byte[1];
        while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            await stream.ReadExactlyAsync(single, token);
            headers.Append((char)single[0]);
            if (headers.Length > 65536)
                throw new InvalidDataException("HTTP headers too large");
        }
        string[] lines = headers.ToString().Split("\r\n");
        int length = int.Parse(lines.Single(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        byte[] response = Encoding.UTF8.GetBytes("""{"choices":[{"message":{"role":"assistant","content":"河岸"}}]}""");
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n"), token);
        await stream.WriteAsync(response, token);
        return (lines[0].Split(' ')[1], Encoding.UTF8.GetString(body));
    }
}
