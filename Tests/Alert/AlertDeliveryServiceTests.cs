using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Alert;

public class AlertDeliveryServiceTests
{
    [Fact]
    public void DedupService_SuppressesRepeatWithinNormalInterval()
    {
        var service = new AlertDedupService();
        AlertEvent alert = CreateAlert();
        var state = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = alert.ContentHash,
            LastSentAt = "2026-06-18 09:00:00"
        };

        bool allowed = service.ShouldDeliver(
            alert,
            state,
            AlertSettings.Default,
            new DateTimeOffset(2026, 6, 18, 9, 20, 0, TimeSpan.Zero));

        Assert.False(allowed);
    }

    [Fact]
    public void DedupService_AllowsRepeatAfterNormalInterval()
    {
        var service = new AlertDedupService();
        AlertEvent alert = CreateAlert();
        var state = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = alert.ContentHash,
            LastSentAt = "2026-06-18 09:00:00"
        };

        bool allowed = service.ShouldDeliver(
            alert,
            state,
            AlertSettings.Default,
            new DateTimeOffset(2026, 6, 18, 9, 31, 0, TimeSpan.Zero));

        Assert.True(allowed);
    }

    [Fact]
    public void DedupService_UsesSevereInterval()
    {
        var service = new AlertDedupService();
        AlertEvent alert = CreateAlert() with { Severity = AlertSeverity.Severe };
        alert = alert.WithStableHash();
        var state = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = alert.ContentHash,
            LastSentAt = "2026-06-18 09:00:00"
        };

        Assert.False(service.ShouldDeliver(alert, state, AlertSettings.Default, new DateTimeOffset(2026, 6, 18, 9, 4, 0, TimeSpan.Zero)));
        Assert.True(service.ShouldDeliver(alert, state, AlertSettings.Default, new DateTimeOffset(2026, 6, 18, 9, 6, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DedupService_UsesMarketInterval()
    {
        var service = new AlertDedupService();
        AlertEvent alert = CreateAlert() with { AlertType = AlertTypes.MarketRuntime, Severity = AlertSeverity.Market };
        alert = alert.WithStableHash();
        var state = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = alert.ContentHash,
            LastSentAt = "2026-06-18 09:00:00"
        };

        Assert.False(service.ShouldDeliver(alert, state, AlertSettings.Default, new DateTimeOffset(2026, 6, 18, 9, 9, 0, TimeSpan.Zero)));
        Assert.True(service.ShouldDeliver(alert, state, AlertSettings.Default, new DateTimeOffset(2026, 6, 18, 9, 11, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DedupService_UsesMarketIntervalBeforeSevereInterval()
    {
        var service = new AlertDedupService();
        AlertEvent alert = CreateMarketAlert() with { Severity = AlertSeverity.Severe };
        alert = alert.WithStableHash();
        var state = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = alert.ContentHash,
            LastSentAt = "2026-06-18 09:00:00"
        };
        AlertSettings settings = AlertSettings.Default with { SevereIntervalMinutes = 5, MarketIntervalMinutes = 30 };

        Assert.False(service.ShouldDeliver(alert, state, settings, new DateTimeOffset(2026, 6, 18, 9, 20, 0, TimeSpan.Zero)));
        Assert.True(service.ShouldDeliver(alert, state, settings, new DateTimeOffset(2026, 6, 18, 9, 31, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DedupService_MarketAlertDoesNotBypassWhenContentHashChangesWithinInterval()
    {
        var service = new AlertDedupService();
        AlertEvent alert = CreateMarketAlert() with { Content = "new dynamic detail" };
        alert = alert.WithStableHash();
        var state = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = "old-hash",
            LastSentAt = "2026-06-18 09:00:00"
        };

        bool allowed = service.ShouldDeliver(
            alert,
            state,
            AlertSettings.Default with { MarketIntervalMinutes = 30 },
            new DateTimeOffset(2026, 6, 18, 9, 20, 0, TimeSpan.Zero));

        Assert.False(allowed);
    }

    [Fact]
    public void DedupService_MarketAlertAllowsAfterMarketIntervalEvenWhenContentHashChanges()
    {
        var service = new AlertDedupService();
        AlertEvent alert = CreateMarketAlert() with { Content = "new dynamic detail" };
        alert = alert.WithStableHash();
        var state = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = "old-hash",
            LastSentAt = "2026-06-18 09:00:00"
        };

        bool allowed = service.ShouldDeliver(
            alert,
            state,
            AlertSettings.Default with { MarketIntervalMinutes = 30 },
            new DateTimeOffset(2026, 6, 18, 9, 31, 0, TimeSpan.Zero));

        Assert.True(allowed);
    }

    [Fact]
    public async Task Delivery_WritesAlertLogAndStateOnSuccess()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Ok());
        var voice = new FakeVoicePlayer(AlertChannelResult.Disabled());
        var service = new AlertDeliveryService(store, sender, voice);

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { CreateAlert() },
            AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token" });

        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Delivered);
        Assert.Single(store.Logs);
        Assert.Single(store.States);
        Assert.Equal("成功", store.Logs[0].WechatStatus);
    }

    [Fact]
    public async Task Delivery_DoesNotRunWhenAllChannelsDisabled()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Ok());
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, sender, voice);

        AlertDeliveryBatchResult result = await service.DeliverAsync(new[] { CreateAlert() }, AlertSettings.Default);

        Assert.Equal(0, result.Attempted);
        Assert.Empty(store.Logs);
        Assert.Equal(0, sender.SendCount);
        Assert.Equal(0, voice.PlayCount);
    }

    [Fact]
    public async Task Delivery_NormalAlertWithVoiceEnabledPlaysVoice()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Ok());
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, sender, voice);

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { CreateAlert() },
            AlertSettings.Default with { VoiceEnabled = true });

        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Delivered);
        Assert.Equal(0, sender.SendCount);
        Assert.Equal(1, voice.PlayCount);
        Assert.Null(store.Logs[0].WechatSentAt);
        Assert.NotNull(store.Logs[0].VoicePlayedAt);
    }

    [Fact]
    public async Task Delivery_SevereAlertWithVoiceEnabledPlaysVoice()
    {
        var store = new FakeAlertStore();
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, new FakeAlertSender(AlertChannelResult.Ok()), voice);
        AlertEvent alert = CreateAlert() with { Severity = AlertSeverity.Severe };
        alert = alert.WithStableHash();

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { alert },
            AlertSettings.Default with { VoiceEnabled = true });

        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Delivered);
        Assert.Equal(1, voice.PlayCount);
        Assert.Equal("成功", store.Logs[0].VoiceStatus);
    }

    [Fact]
    public async Task Delivery_RecordsPushPlusFailureWithoutThrowing()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Failed("network failed"));
        var service = new AlertDeliveryService(store, sender, new FakeVoicePlayer(AlertChannelResult.Disabled()));

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { CreateAlert() },
            AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token" });

        Assert.Equal(1, result.Attempted);
        Assert.Equal(0, result.Delivered);
        Assert.Single(store.Logs);
        Assert.Equal("失败", store.Logs[0].WechatStatus);
        Assert.Equal("network failed", store.Logs[0].WechatError);
    }

    [Fact]
    public async Task Delivery_RecordsVoiceFailureWithoutThrowing()
    {
        var store = new FakeAlertStore();
        var voice = new FakeVoicePlayer(AlertChannelResult.Failed("voice failed"));
        var service = new AlertDeliveryService(store, new FakeAlertSender(AlertChannelResult.Disabled()), voice);

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { CreateAlert() },
            AlertSettings.Default with { VoiceEnabled = true });

        Assert.Equal(1, result.Attempted);
        Assert.Single(store.Logs);
        Assert.Equal("失败", store.Logs[0].VoiceStatus);
        Assert.Equal("voice failed", store.Logs[0].VoiceError);
    }

    [Fact]
    public async Task Delivery_PushPlusFailureDoesNotBlockVoice()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Failed("network failed"));
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, sender, voice);

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { CreateAlert() },
            AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token", VoiceEnabled = true });

        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Delivered);
        Assert.Equal(1, voice.PlayCount);
        Assert.Equal("失败", store.Logs[0].WechatStatus);
        Assert.Equal("成功", store.Logs[0].VoiceStatus);
    }

    [Fact]
    public async Task Delivery_VoiceFailureDoesNotBlockPushPlus()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Ok());
        var voice = new FakeVoicePlayer(AlertChannelResult.Failed("voice failed"));
        var service = new AlertDeliveryService(store, sender, voice);

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { CreateAlert() },
            AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token", VoiceEnabled = true });

        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Delivered);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal("成功", store.Logs[0].WechatStatus);
        Assert.Equal("失败", store.Logs[0].VoiceStatus);
    }

    [Fact]
    public async Task TestWechat_BypassesDedupeLimit()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Ok());
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, sender, voice);

        AlertSettings settings = AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token" };
        await service.SendTestWechatAsync(settings);
        await service.SendTestWechatAsync(settings);

        Assert.Equal(2, sender.SendCount);
        Assert.Equal(0, voice.PlayCount);
        Assert.Equal(2, store.Logs.Count);
        Assert.All(store.Logs, log =>
        {
            Assert.Equal("成功", log.WechatStatus);
            Assert.Equal("不适用", log.VoiceStatus);
        });
    }

    [Fact]
    public async Task TestVoice_DoesNotSendPushPlusAndMarksWechatNotApplicable()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Ok());
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, sender, voice);

        await service.PlayTestVoiceAsync(AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token", VoiceEnabled = true });

        Assert.Equal(0, sender.SendCount);
        Assert.Equal(1, voice.PlayCount);
        Assert.Single(store.Logs);
        Assert.Equal("不适用", store.Logs[0].WechatStatus);
        Assert.Equal("成功", store.Logs[0].VoiceStatus);
    }

    [Fact]
    public async Task VoiceDisabled_DoesNotPlay()
    {
        var store = new FakeAlertStore();
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, new FakeAlertSender(AlertChannelResult.Ok()), voice);

        await service.DeliverAsync(new[] { CreateAlert() }, AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token" });

        Assert.Equal(0, voice.PlayCount);
        Assert.Null(store.Logs[0].VoicePlayedAt);
    }

    [Fact]
    public async Task Delivery_SuppressesRepeatedMarketAlertWithoutWritingLogOrCallingChannels()
    {
        var store = new FakeAlertStore();
        var sender = new FakeAlertSender(AlertChannelResult.Ok());
        var voice = new FakeVoicePlayer(AlertChannelResult.Ok());
        var service = new AlertDeliveryService(store, sender, voice);
        AlertEvent alert = CreateMarketAlert();
        store.States[alert.DedupeKey] = new AlertDeliveryStateRecord
        {
            DedupeKey = alert.DedupeKey,
            LastContentHash = "older-dynamic-hash",
            LastSentAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        AlertDeliveryBatchResult result = await service.DeliverAsync(
            new[] { alert },
            AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token", VoiceEnabled = true, MarketIntervalMinutes = 30 });

        Assert.Equal(0, result.Attempted);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(store.Logs);
        Assert.Equal(0, sender.SendCount);
        Assert.Equal(0, voice.PlayCount);
    }

    [Fact]
    public async Task Delivery_UpdatesStateWhenPushPlusFailsButVoiceSucceeds()
    {
        var store = new FakeAlertStore();
        var service = new AlertDeliveryService(
            store,
            new FakeAlertSender(AlertChannelResult.Failed("network failed")),
            new FakeVoicePlayer(AlertChannelResult.Ok()));
        AlertEvent alert = CreateAlert();

        await service.DeliverAsync(
            new[] { alert },
            AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token", VoiceEnabled = true });

        Assert.True(store.States.ContainsKey(alert.DedupeKey));
    }

    [Fact]
    public async Task Delivery_UpdatesStateWhenWechatDisabledAndVoiceSucceeds()
    {
        var store = new FakeAlertStore();
        var service = new AlertDeliveryService(
            store,
            new FakeAlertSender(AlertChannelResult.Ok()),
            new FakeVoicePlayer(AlertChannelResult.Ok()));
        AlertEvent alert = CreateAlert();

        await service.DeliverAsync(
            new[] { alert },
            AlertSettings.Default with { VoiceEnabled = true });

        Assert.True(store.States.ContainsKey(alert.DedupeKey));
    }

    [Fact]
    public async Task Delivery_UpdatesStateWhenVoiceDisabledAndWechatSucceeds()
    {
        var store = new FakeAlertStore();
        var service = new AlertDeliveryService(
            store,
            new FakeAlertSender(AlertChannelResult.Ok()),
            new FakeVoicePlayer(AlertChannelResult.Ok()));
        AlertEvent alert = CreateAlert();

        await service.DeliverAsync(
            new[] { alert },
            AlertSettings.Default with { PushPlusEnabled = true, PushPlusToken = "token" });

        Assert.True(store.States.ContainsKey(alert.DedupeKey));
    }

    private static AlertEvent CreateAlert()
    {
        var alert = new AlertEvent
        {
            CreatedAt = new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero),
            AlertType = AlertTypes.StrategyDecision,
            Severity = AlertSeverity.Normal,
            StrategyCode = "159941",
            Title = "\u3010\u4f5c\u6218\u6307\u4ee4\u3011159941 \u5168\u6e05\u6362\u73b0\u91d1(\u7559\u5e95)",
            Content = "content",
            DedupeKey = AlertEvent.BuildDedupeKey(AlertTypes.StrategyDecision, "159941", "\u5168\u6e05\u6362\u73b0\u91d1(\u7559\u5e95)", "\u6781\u7aef\u6ea2\u4ef7"),
            Source = "strategy_decision_state",
            Action = "\u5168\u6e05\u6362\u73b0\u91d1(\u7559\u5e95)",
            Reason = "\u6781\u7aef\u6ea2\u4ef7"
        };
        return alert.WithStableHash();
    }

    private static AlertEvent CreateMarketAlert()
    {
        var alert = new AlertEvent
        {
            CreatedAt = new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero),
            AlertType = AlertTypes.MarketRuntime,
            Severity = AlertSeverity.Market,
            Title = "market failed",
            Content = "secid=251.NDXTMC url=https://push2his.eastmoney.com",
            DedupeKey = string.Join("|", AlertTypes.MarketRuntime, "EASTMONEY_HISTORY", "HISTORY_KLINE_UNAVAILABLE"),
            Source = "EASTMONEY_HISTORY",
            Action = "COOLDOWN",
            Reason = "HISTORY_KLINE_UNAVAILABLE"
        };
        return alert.WithStableHash();
    }

    private sealed class FakeAlertStore : IAlertDeliveryStore
    {
        public List<AlertLogRecord> Logs { get; } = new();
        public Dictionary<string, AlertDeliveryStateRecord> States { get; } = new(StringComparer.OrdinalIgnoreCase);

        public AlertDeliveryStateRecord? ReadAlertDeliveryState(string dedupeKey)
            => States.GetValueOrDefault(dedupeKey);

        public void SaveAlertDeliveryState(AlertDeliveryStateRecord record)
            => States[record.DedupeKey] = record;

        public void SaveAlertLog(AlertLogRecord record)
            => Logs.Add(record);

        public void WriteRuntimeLog(string level, string module, string message, string? detail = null)
        {
        }
    }

    private sealed class FakeAlertSender : IAlertSender
    {
        private readonly AlertChannelResult _result;

        public FakeAlertSender(AlertChannelResult result) => _result = result;
        public int SendCount { get; private set; }

        public Task<AlertChannelResult> SendAsync(AlertEvent alert, string token, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeVoicePlayer : IVoiceAlertPlayer
    {
        private readonly AlertChannelResult _result;

        public FakeVoicePlayer(AlertChannelResult result) => _result = result;
        public int PlayCount { get; private set; }

        public Task<AlertChannelResult> PlayAsync(AlertEvent alert, CancellationToken cancellationToken = default)
        {
            PlayCount++;
            return Task.FromResult(_result);
        }
    }
}
