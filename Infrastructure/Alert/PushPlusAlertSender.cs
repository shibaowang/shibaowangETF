using System.Net.Http;
using System.Text;
using System.Text.Json;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Alert;

public sealed class PushPlusAlertSender : IAlertSender, IDisposable
{
    private static readonly Uri PushPlusEndpoint = new("http://www.pushplus.plus/send");
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public PushPlusAlertSender()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(5) }, true)
    {
    }

    public PushPlusAlertSender(HttpClient client)
        : this(client, false)
    {
    }

    private PushPlusAlertSender(HttpClient client, bool ownsClient)
    {
        _client = client;
        _ownsClient = ownsClient;
    }

    public async Task<AlertChannelResult> SendAsync(AlertEvent alert, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AlertChannelResult.Failed("PushPlus Token 未配置");
        }

        try
        {
            var payload = new
            {
                token = token.Trim(),
                title = alert.Title,
                content = alert.Content
            };
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.ContentType!.CharSet = "utf-8";

            using HttpResponseMessage response = await _client.PostAsync(PushPlusEndpoint, content, cancellationToken).ConfigureAwait(false);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return AlertChannelResult.Failed($"PushPlus HTTP {(int)response.StatusCode}: {ShortText(responseText, 160)}");
            }

            return AlertChannelResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AlertChannelResult.Failed($"PushPlus 发送失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static string ShortText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string text = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
