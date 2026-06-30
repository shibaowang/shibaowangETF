using System.Globalization;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Services;

public sealed class AlertDeliveryService
{
    private readonly IAlertDeliveryStore _store;
    private readonly IAlertSender _pushPlusSender;
    private readonly IVoiceAlertPlayer _voicePlayer;
    private readonly AlertDedupService _dedupService;

    public AlertDeliveryService(
        IAlertDeliveryStore store,
        IAlertSender pushPlusSender,
        IVoiceAlertPlayer voicePlayer,
        AlertDedupService? dedupService = null)
    {
        _store = store;
        _pushPlusSender = pushPlusSender;
        _voicePlayer = voicePlayer;
        _dedupService = dedupService ?? new AlertDedupService();
    }

    public async Task<AlertDeliveryBatchResult> DeliverAsync(
        IEnumerable<AlertEvent> alerts,
        AlertSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(settings);

        AlertSettings normalized = AlertSettings.Normalize(settings);
        if (!normalized.PushPlusEnabled && !normalized.VoiceEnabled)
        {
            return new AlertDeliveryBatchResult(0, 0, 0);
        }

        int attempted = 0;
        int delivered = 0;
        int skipped = 0;
        foreach (AlertEvent rawAlert in alerts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AlertEvent alert = string.IsNullOrWhiteSpace(rawAlert.ContentHash)
                ? rawAlert.WithStableHash()
                : rawAlert;
            AlertDeliveryStateRecord? state = _store.ReadAlertDeliveryState(alert.DedupeKey);
            DateTimeOffset now = DateTimeOffset.Now;
            if (!_dedupService.ShouldDeliver(alert, state, normalized, now))
            {
                skipped++;
                continue;
            }

            attempted++;
            AlertDeliveryResult result = await DeliverOneAsync(alert, normalized, false, cancellationToken).ConfigureAwait(false);
            if (result.IsAnySuccess)
            {
                delivered++;
            }
        }

        return new AlertDeliveryBatchResult(attempted, delivered, skipped);
    }

    public async Task<AlertDeliveryResult> SendTestWechatAsync(AlertSettings settings, CancellationToken cancellationToken = default)
    {
        AlertSettings normalized = AlertSettings.Normalize(settings) with { PushPlusEnabled = true, VoiceEnabled = false };
        return await DeliverOneAsync(AlertRuleEvaluator.CreateTestWechatEvent(), normalized, true, cancellationToken, voiceApplicable: false).ConfigureAwait(false);
    }

    public async Task<AlertDeliveryResult> PlayTestVoiceAsync(AlertSettings settings, CancellationToken cancellationToken = default)
    {
        AlertSettings normalized = AlertSettings.Normalize(settings) with { PushPlusEnabled = false, VoiceEnabled = true };
        return await DeliverOneAsync(AlertRuleEvaluator.CreateTestVoiceEvent(), normalized, true, cancellationToken, wechatApplicable: false).ConfigureAwait(false);
    }

    private async Task<AlertDeliveryResult> DeliverOneAsync(
        AlertEvent alert,
        AlertSettings settings,
        bool bypassLimit,
        CancellationToken cancellationToken,
        bool wechatApplicable = true,
        bool voiceApplicable = true)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        AlertChannelResult wechat = !wechatApplicable
            ? AlertChannelResult.NotApplicable()
            : settings.PushPlusEnabled
            ? await _pushPlusSender.SendAsync(alert, settings.PushPlusToken, cancellationToken).ConfigureAwait(false)
            : AlertChannelResult.Disabled();

        AlertChannelResult voice = !voiceApplicable
            ? AlertChannelResult.NotApplicable()
            : settings.VoiceEnabled
            ? await _voicePlayer.PlayAsync(alert, cancellationToken).ConfigureAwait(false)
            : AlertChannelResult.Disabled();

        var log = new AlertLogRecord
        {
            CreatedAt = FormatTime(alert.CreatedAt),
            AlertType = alert.AlertType,
            Severity = alert.Severity,
            StrategyCode = alert.StrategyCode,
            ActualCode = alert.ActualCode,
            Title = alert.Title,
            Content = alert.Content,
            DedupeKey = alert.DedupeKey,
            WechatEnabled = settings.PushPlusEnabled,
            WechatStatus = wechat.Status,
            WechatError = wechat.Error,
            WechatSentAt = wechat.Success ? FormatTime(now) : null,
            VoiceEnabled = settings.VoiceEnabled,
            VoiceStatus = voice.Status,
            VoiceError = voice.Error,
            VoicePlayedAt = voice.Success ? FormatTime(now) : null,
            Source = alert.Source,
            ContentHash = alert.ContentHash
        };

        try
        {
            _store.SaveAlertLog(log);
            _store.SaveAlertDeliveryState(new AlertDeliveryStateRecord
            {
                DedupeKey = alert.DedupeKey,
                LastAlertType = alert.AlertType,
                LastStrategyCode = alert.StrategyCode,
                LastAction = alert.Action,
                LastReason = alert.Reason,
                LastContentHash = alert.ContentHash,
                LastSentAt = FormatTime(now),
                LastStatus = log.WechatStatus == "成功" || log.VoiceStatus == "成功" ? "成功" : "失败",
                LastTitle = alert.Title,
                LastContent = alert.Content
            });
        }
        catch (Exception ex)
        {
            _store.WriteRuntimeLog("ERROR", "AlertDelivery", "预警日志或去重状态写入失败", ex.Message);
        }

        return new AlertDeliveryResult(alert, wechat, voice, bypassLimit);
    }

    private static string FormatTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

public interface IAlertDeliveryStore
{
    AlertDeliveryStateRecord? ReadAlertDeliveryState(string dedupeKey);
    void SaveAlertDeliveryState(AlertDeliveryStateRecord record);
    void SaveAlertLog(AlertLogRecord record);
    void WriteRuntimeLog(string level, string module, string message, string? detail = null);
}

public interface IAlertSender
{
    Task<AlertChannelResult> SendAsync(AlertEvent alert, string token, CancellationToken cancellationToken = default);
}

public interface IVoiceAlertPlayer
{
    Task<AlertChannelResult> PlayAsync(AlertEvent alert, CancellationToken cancellationToken = default);
}

public sealed record AlertDeliveryBatchResult(int Attempted, int Delivered, int Skipped);

public sealed record AlertDeliveryResult(AlertEvent Alert, AlertChannelResult Wechat, AlertChannelResult Voice, bool BypassedLimit)
{
    public bool IsAnySuccess => Wechat.Success || Voice.Success;
}

public sealed record AlertChannelResult(bool Success, string Status, string? Error = null)
{
    public static AlertChannelResult Ok() => new(true, "成功");
    public static AlertChannelResult Failed(string error) => new(false, "失败", error);
    public static AlertChannelResult Disabled() => new(false, "未启用");
    public static AlertChannelResult NotApplicable() => new(false, "不适用");
}
