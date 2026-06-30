using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;
using CrossETF.Terminal.UiShell.Reference.Infrastructure.Alert;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Alert;

public class VoiceAlertPlayerTests
{
    [Fact]
    public async Task PlayAsync_DoesNotDisposeEnginePerPlay()
    {
        var engine = new RecordingVoiceEngine();
        using var player = new VoiceAlertPlayer(engine);

        AlertChannelResult result = await player.PlayAsync(CreateAlert("159941", "极端溢价"));

        Assert.True(result.Success);
        Assert.False(engine.Disposed);
        Assert.Single(engine.SpokenTexts);
    }

    [Fact]
    public async Task PlayAsync_SerializesContinuousVoiceAlerts()
    {
        var engine = new RecordingVoiceEngine(delayMs: 40);
        using var player = new VoiceAlertPlayer(engine);

        Task<AlertChannelResult> first = player.PlayAsync(CreateAlert("159941", "极端溢价"));
        Task<AlertChannelResult> second = player.PlayAsync(CreateAlert("159509", "战略底仓"));

        await Task.WhenAll(first, second);

        Assert.True(first.Result.Success);
        Assert.True(second.Result.Success);
        Assert.Collection(
            engine.Events,
            item => Assert.StartsWith("start:跨境ETF预警，159941", item),
            item => Assert.StartsWith("end:跨境ETF预警，159941", item),
            item => Assert.StartsWith("start:跨境ETF预警，159509", item),
            item => Assert.StartsWith("end:跨境ETF预警，159509", item));
    }

    [Fact]
    public async Task PlayAsync_EngineExceptionReturnsFailureWithoutThrowing()
    {
        using var player = new VoiceAlertPlayer(new ThrowingVoiceEngine());

        AlertChannelResult result = await player.PlayAsync(CreateAlert("159941", "极端溢价"));

        Assert.False(result.Success);
        Assert.Equal("失败", result.Status);
        Assert.Contains("voice failed", result.Error);
    }

    [Fact]
    public void BuildVoiceText_UsesShortTestText()
    {
        string text = VoiceAlertPlayer.BuildVoiceText(AlertRuleEvaluator.CreateTestVoiceEvent());

        Assert.Equal("跨境ETF智能投资决策系统，语音提示测试成功。", text);
    }

    private static AlertEvent CreateAlert(string code, string action)
        => new AlertEvent
        {
            AlertType = AlertTypes.StrategyDecision,
            Severity = AlertSeverity.Severe,
            StrategyCode = code,
            Action = action,
            Title = "title",
            Content = "content",
            DedupeKey = AlertEvent.BuildDedupeKey(AlertTypes.StrategyDecision, code, action, null)
        }.WithStableHash();

    private sealed class RecordingVoiceEngine : IVoiceAlertEngine
    {
        private readonly int _delayMs;
        private readonly object _sync = new();

        public RecordingVoiceEngine(int delayMs = 0)
        {
            _delayMs = delayMs;
        }

        public List<string> Events { get; } = new();
        public List<string> SpokenTexts { get; } = new();
        public bool Disposed { get; private set; }

        public void Speak(string text, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                Events.Add("start:" + text);
                SpokenTexts.Add(text);
            }

            if (_delayMs > 0)
            {
                Thread.Sleep(_delayMs);
            }

            lock (_sync)
            {
                Events.Add("end:" + text);
            }
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class ThrowingVoiceEngine : IVoiceAlertEngine
    {
        public void Speak(string text, CancellationToken cancellationToken)
            => throw new InvalidOperationException("voice failed");

        public void Dispose()
        {
        }
    }
}
