using System.Net;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Alert;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Alert;

public class PushPlusAlertSenderTests
{
    [Fact]
    public async Task EmptyToken_DoesNotSendHttpRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(handler);
        var sender = new PushPlusAlertSender(client);

        var result = await sender.SendAsync(CreateAlert(), "");

        Assert.False(result.Success);
        Assert.Equal(0, handler.SendCount);
        Assert.Contains("Token 未配置", result.Error);
    }

    [Fact]
    public async Task HttpSuccess_ReturnsSuccess()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{\"code\":200}");
        using var client = new HttpClient(handler);
        var sender = new PushPlusAlertSender(client);

        var result = await sender.SendAsync(CreateAlert(), "secret-token");

        Assert.True(result.Success);
        Assert.Equal(1, handler.SendCount);
        Assert.NotNull(handler.LastRequestContent);
        Assert.Contains("\"token\":\"secret-token\"", handler.LastRequestContent);
        Assert.Contains("\"title\"", handler.LastRequestContent);
    }

    [Fact]
    public async Task HttpFailure_ReturnsFailureWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.BadGateway, "bad gateway");
        using var client = new HttpClient(handler);
        var sender = new PushPlusAlertSender(client);

        var result = await sender.SendAsync(CreateAlert(), "secret-token");

        Assert.False(result.Success);
        Assert.Equal(1, handler.SendCount);
        Assert.Contains("HTTP", result.Error);
    }

    private static AlertEvent CreateAlert()
        => new AlertEvent
        {
            AlertType = AlertTypes.Test,
            Title = "【测试预警】PushPlus 微信预警测试",
            Content = "content",
            DedupeKey = "test",
            Source = "test"
        }.WithStableHash();

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _response;

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string response)
        {
            _statusCode = statusCode;
            _response = response;
        }

        public int SendCount { get; private set; }
        public string? LastRequestContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequestContent = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response)
            };
        }
    }
}
