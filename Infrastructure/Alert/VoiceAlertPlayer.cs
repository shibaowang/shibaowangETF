using System.Runtime.InteropServices;
using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Alert;

public sealed class VoiceAlertPlayer : IVoiceAlertPlayer, IDisposable
{
    private readonly IVoiceAlertEngine _engine;
    private readonly SemaphoreSlim _playLock = new(1, 1);
    private bool _disposed;

    public VoiceAlertPlayer()
        : this(new SapiVoiceAlertEngine())
    {
    }

    public VoiceAlertPlayer(IVoiceAlertEngine engine)
    {
        _engine = engine;
    }

    public async Task<AlertChannelResult> PlayAsync(AlertEvent alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (_disposed)
        {
            return AlertChannelResult.Failed("系统语音组件已释放");
        }

        await _playLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string text = BuildVoiceText(alert);
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _engine.Speak(text, cancellationToken);
                return AlertChannelResult.Ok();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AlertChannelResult.Failed($"语音播放失败：{ex.Message}");
        }
        finally
        {
            _playLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.Dispose();
        _playLock.Dispose();
    }

    public static string BuildVoiceText(AlertEvent alert)
    {
        if (alert.AlertType == AlertTypes.Test)
        {
            return "跨境ETF智能投资决策系统，语音提示测试成功。";
        }

        if (alert.AlertType == AlertTypes.MarketRuntime)
        {
            return "跨境ETF预警，行情异常，请检查数据源。";
        }

        string code = string.IsNullOrWhiteSpace(alert.StrategyCode) ? "系统" : alert.StrategyCode.Trim();
        string action = string.IsNullOrWhiteSpace(alert.Action) ? alert.AlertType : alert.Action.Trim();
        return $"跨境ETF预警，{code} 触发 {action}，请查看作战指令。";
    }

    private sealed class SapiVoiceAlertEngine : IVoiceAlertEngine
    {
        private object? _voice;
        private bool _disposed;

        public void Speak(string text, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SapiVoiceAlertEngine));
            }

            cancellationToken.ThrowIfCancellationRequested();
            object voice = GetVoice();
            dynamic dynamicVoice = voice;
            dynamicVoice.Speak(text, 0);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_voice is not null && Marshal.IsComObject(_voice))
            {
                Marshal.FinalReleaseComObject(_voice);
            }

            _voice = null;
        }

        private object GetVoice()
        {
            if (_voice is not null)
            {
                return _voice;
            }

            Type? voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (voiceType is null)
            {
                throw new InvalidOperationException("系统语音组件不可用");
            }

            _voice = Activator.CreateInstance(voiceType) ?? throw new InvalidOperationException("系统语音组件不可用");
            return _voice;
        }
    }
}

public interface IVoiceAlertEngine : IDisposable
{
    void Speak(string text, CancellationToken cancellationToken);
}
