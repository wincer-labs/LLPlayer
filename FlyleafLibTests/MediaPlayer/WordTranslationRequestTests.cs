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
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        var receive = ReplyAndCapture(listener, timeout.Token);
        using OpenAIBaseTranslateService service = new(new OllamaTranslateSettings
        {
            Endpoint = $"http://127.0.0.1:{port}", Model = "test-model"
        }, new TranslateChatConfig(), true);
        service.Initialize(Language.English, TargetLanguage.ChineseSimplified);
        var translating = service.TranslateWordAsync("bank",
            new("We sat on the bank.", ["The river was quiet."], ["We watched the water."]), timeout.Token);
        Assert.Equal("河岸", await translating);
        var (path, body) = await receive;
        using var payload = JsonDocument.Parse(body);
        Assert.Equal("/v1/chat/completions", path);
        var messages = payload.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        string prompt = messages[1].GetProperty("content").GetString()!;
        Assert.Contains("The river was quiet.", prompt);
        Assert.Contains("We sat on the bank.", prompt);
        Assert.Contains("We watched the water.", prompt);
        Assert.EndsWith("bank", prompt.Trim());
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
