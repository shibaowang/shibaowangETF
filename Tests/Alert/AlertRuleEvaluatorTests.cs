using CrossETF.Terminal.UiShell.Reference.Core.Models;
using CrossETF.Terminal.UiShell.Reference.Core.Services;

namespace CrossETF.Terminal.UiShell.Reference.Tests.Alert;

public class AlertRuleEvaluatorTests
{
    [Theory]
    [InlineData("全清换现金(留底)")]
    [InlineData("止盈减仓(留底)")]
    [InlineData("溢价达标减仓(留底)")]
    [InlineData("战略底仓")]
    [InlineData("狙击一档")]
    [InlineData("狙击六档")]
    public void StrategyWhitelist_GeneratesAlertEvent(string action)
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            StrategyDecisions = new[]
            {
                new StrategyDecisionStateRecord
                {
                    StrategyCode = "159941",
                    ActionInstruction = action,
                    StrategyStatus = "逢低吸筹",
                    SuggestedPrice = 1.23,
                    Premium = 0.12
                }
            }
        }, new DateTimeOffset(2026, 6, 18, 9, 30, 0, TimeSpan.Zero));

        AlertEvent alert = Assert.Single(alerts);
        Assert.Equal(AlertTypes.StrategyDecision, alert.AlertType);
        Assert.Equal("159941", alert.StrategyCode);
        Assert.Contains(action, alert.Title);
        Assert.False(string.IsNullOrWhiteSpace(alert.DedupeKey));
        Assert.False(string.IsNullOrWhiteSpace(alert.ContentHash));
    }

    [Theory]
    [InlineData("正常趋势", "持股待涨")]
    [InlineData("√ 持股待涨", "正常趋势")]
    [InlineData("等待建仓", "空仓观察")]
    public void StrategyNonWhitelist_DoesNotGenerateAlert(string action, string status)
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            StrategyDecisions = new[]
            {
                new StrategyDecisionStateRecord
                {
                    StrategyCode = "159509",
                    ActionInstruction = action,
                    StrategyStatus = status
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Theory]
    [InlineData("极端溢价", "禁止建仓")]
    [InlineData("--", "禁止建仓")]
    public void WpfEnhancedKeywords_GenerateAlert(string action, string status)
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            StrategyDecisions = new[]
            {
                new StrategyDecisionStateRecord
                {
                    StrategyCode = "513100",
                    ActionInstruction = action,
                    StrategyStatus = status
                }
            }
        });

        Assert.Single(alerts);
        Assert.Equal(AlertSeverity.Severe, alerts[0].Severity);
    }

    [Fact]
    public void OrderDraftReason_GeneratesNotExecutableAlert()
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            OrderDrafts = new[]
            {
                new OrderDraftStateRecord
                {
                    StrategyCode = "159941",
                    ActionInstruction = "全清换现金(留底)",
                    Side = "卖出",
                    Source = "场内ETF",
                    Reason = "底仓保护",
                    IsExecutable = false
                }
            }
        });

        AlertEvent alert = Assert.Single(alerts);
        Assert.Equal(AlertTypes.OrderNotExecutable, alert.AlertType);
        Assert.Contains("底仓保护", alert.Content);
    }

    [Fact]
    public void MarketStatusError_GeneratesMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "EastMoneyHistory",
                    Status = "ERROR",
                    FailureCount = 2,
                    LastError = "ResponseEnded"
                }
            }
        });

        AlertEvent alert = Assert.Single(alerts);
        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Equal(AlertSeverity.Market, alert.Severity);
    }

    [Theory]
    [InlineData("RATE_LIMIT", "scheduler rate_limited; next=2026-06-29T15:07:49+08:00")]
    [InlineData("COOLDOWN", "cooldown until 2026-06-29T15:07:49+08:00")]
    [InlineData("ERROR", "host endpoint symbol 预算不足")]
    public void MarketStatusSchedulerRateLimit_DoesNotGenerateMarketAlert(string status, string error)
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "EASTMONEY_HISTORY",
                    Status = status,
                    FailureCount = 0,
                    LastError = error
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Theory]
    [InlineData("HTTP 429 from push2his")]
    [InlineData("HTTP 403 from push2his")]
    public void MarketStatusRealHttpThrottle_GeneratesMarketAlert(string error)
    {
        var evaluator = new AlertRuleEvaluator();
        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "EASTMONEY_HISTORY",
                    Status = "ERROR",
                    FailureCount = 1,
                    LastError = error
                }
            }
        }));

        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Contains("HISTORY_KLINE_UNAVAILABLE", alert.DedupeKey);
    }

    [Fact]
    public void MarketStatusSinaFundSingleTimeoutWithCache_DoesNotGenerateMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "SINA_FUND",
                    Status = "ERROR",
                    FailureCount = 1,
                    LastSuccessAt = "2026-06-29 20:55:00",
                    LastError = "The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing. | The operation was canceled. | Unable to read data from the transport connection"
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void MarketStatusSinaFundThreeTimeouts_GeneratesMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();
        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "SINA_FUND",
                    Status = "ERROR",
                    FailureCount = 3,
                    LastSuccessAt = "2026-06-29 20:55:00",
                    LastError = "The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing."
                }
            }
        }));

        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Contains("SINA_FUND_NAV_UNAVAILABLE", alert.DedupeKey);
        Assert.Contains("失败次数：3", alert.Content);
        Assert.Contains("HttpClient.Timeout", alert.Content);
    }

    [Fact]
    public void MarketStatusSinaFundSingleTimeoutWithoutCache_GeneratesMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();
        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "SINA_FUND",
                    Status = "ERROR",
                    FailureCount = 1,
                    LastSuccessAt = null,
                    LastError = "The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing."
                }
            }
        }));

        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Contains("SINA_FUND_NAV_UNAVAILABLE", alert.DedupeKey);
    }

    [Fact]
    public void MarketStatusDedupeKey_NormalizesEastMoneyHistoryDynamicDetails()
    {
        var evaluator = new AlertRuleEvaluator();
        var firstInput = new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "EASTMONEY_HISTORY",
                    Status = "COOLDOWN",
                    FailureCount = 2,
                    LastError = "EastMoney history failed. secid=0.159509; ResponseEnded; url=https://push2his.eastmoney.com/api/qt/stock/kline/get?secid=0.159509&end=20260619"
                }
            }
        };
        var secondInput = new AlertRuleEvaluationInput
        {
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "EASTMONEY_HISTORY",
                    Status = "COOLDOWN",
                    FailureCount = 3,
                    LastError = "EastMoney history failed. secid=251.NDXTMC; ResponseEnded; url=https://push2his.eastmoney.com/api/qt/stock/kline/get?secid=251.NDXTMC&end=20260620"
                }
            }
        };

        AlertEvent first = Assert.Single(evaluator.Evaluate(firstInput));
        AlertEvent second = Assert.Single(evaluator.Evaluate(secondInput));

        Assert.Equal(first.DedupeKey, second.DedupeKey);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.DoesNotContain("159509", first.DedupeKey);
        Assert.DoesNotContain("NDXTMC", first.DedupeKey);
        Assert.DoesNotContain("20260619", first.DedupeKey);
        Assert.DoesNotContain("push2his", first.DedupeKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secid", first.DedupeKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeLogMarketAlert_UsesOriginalRuntimeLogTime()
    {
        var evaluator = new AlertRuleEvaluator();
        DateTimeOffset now = new(2026, 6, 22, 11, 0, 0, TimeSpan.Zero);

        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 12,
                    Time = "2026-06-22 10:01:02",
                    Level = "ERROR",
                    Module = "TENCENT_QT",
                    Message = "Tencent quote unavailable",
                    Detail = "qt.gtimg.cn timeout"
                }
            }
        }, now));

        Assert.Equal(new DateTimeOffset(2026, 6, 22, 10, 1, 2, TimeSpan.Zero), alert.CreatedAt);
        Assert.Contains("TENCENT_QT", alert.DedupeKey);
        Assert.Contains("TENCENT_REALTIME_UNAVAILABLE", alert.DedupeKey);
    }

    [Fact]
    public void RuntimeLogSecurityChart_DoesNotGenerateMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 1,
                    Time = "2026-06-22 10:00:00",
                    Level = "WARN",
                    Module = "SecurityChart",
                    Message = "history kline unavailable",
                    Detail = "window data is temporarily unavailable"
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void RuntimeLogEastMoneyHistory_RecoveredSourceDoesNotGenerateMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 21,
                    Time = "2026-06-22 10:00:00",
                    Level = "ERROR",
                    Module = "EASTMONEY_HISTORY",
                    Message = "push2his kline failed",
                    Detail = "ResponseEnded"
                }
            },
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "EASTMONEY_HISTORY",
                    Status = "OK",
                    LastSuccessAt = "2026-06-22 10:05:00"
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void RuntimeLogEastMoneyHistory_CurrentFailureGeneratesMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 22,
                    Time = "2026-06-22 10:00:00",
                    Level = "ERROR",
                    Module = "EASTMONEY_HISTORY",
                    Message = "push2his kline failed",
                    Detail = "ResponseEnded"
                }
            }
        }));

        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Contains("EASTMONEY_HISTORY", alert.DedupeKey);
        Assert.Contains("HISTORY_KLINE_UNAVAILABLE", alert.DedupeKey);
        Assert.Contains("详情：ResponseEnded", alert.Content);
    }

    [Theory]
    [InlineData("scheduler rate_limited; next=2026-06-29T15:07:49+08:00")]
    [InlineData("cooldown until 2026-06-29T15:07:49+08:00")]
    [InlineData("host endpoint symbol budget exhausted")]
    public void RuntimeLogEastMoneyHistory_SchedulerRateLimitDoesNotGenerateMarketAlert(string detail)
    {
        var evaluator = new AlertRuleEvaluator();

        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 24,
                    Time = "2026-06-29 14:46:35",
                    Level = "ERROR",
                    Module = "EASTMONEY_HISTORY",
                    Message = "历史高点刷新失败",
                    Detail = detail
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void RuntimeLogEastMoneyHistory_InvalidJsonStillGeneratesMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 25,
                    Time = "2026-06-22 10:00:00",
                    Level = "ERROR",
                    Module = "EASTMONEY_HISTORY",
                    Message = "push2his kline failed",
                    Detail = "invalid JSON: 'f' is an invalid start of a property name"
                }
            }
        }));

        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Contains("HISTORY_KLINE_UNAVAILABLE", alert.DedupeKey);
    }

    [Theory]
    [InlineData("HTTP 429 from push2his")]
    [InlineData("HTTP 403 from push2his")]
    public void RuntimeLogEastMoneyHistory_RealHttpThrottleStillGeneratesMarketAlert(string detail)
    {
        var evaluator = new AlertRuleEvaluator();

        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 26,
                    Time = "2026-06-22 10:00:00",
                    Level = "ERROR",
                    Module = "EASTMONEY_HISTORY",
                    Message = "push2his kline failed",
                    Detail = detail
                }
            }
        }));

        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Contains("HISTORY_KLINE_UNAVAILABLE", alert.DedupeKey);
    }

    [Fact]
    public void RuntimeLogSinaFundSingleTimeoutWithCache_DoesNotGenerateMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 27,
                    Time = "2026-06-29 20:58:15",
                    Level = "ERROR",
                    Module = "SINA_FUND",
                    Message = "行情请求失败",
                    Detail = "The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing. | The operation was canceled. | Unable to read data from the transport connection"
                }
            },
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "SINA_FUND",
                    Status = "ERROR",
                    FailureCount = 1,
                    LastSuccessAt = "2026-06-29 20:55:00",
                    LastError = "The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing."
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void RuntimeLogHistoryDowngradeMarker_DoesNotGenerateMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 23,
                    Time = "2026-06-22 10:00:00",
                    Level = "WARN",
                    Module = "EASTMONEY_HISTORY",
                    Message = "history cache downgrade skipped",
                    Detail = "SKIP_HISTORY_DOWNGRADE old cache retained"
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void RuntimeLogTencentQt_RecoveredSourceDoesNotGenerateMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 31,
                    Time = "2026-06-22 10:00:00",
                    Level = "ERROR",
                    Module = "TENCENT_QT",
                    Message = "Tencent quote unavailable",
                    Detail = "qt.gtimg.cn timeout"
                }
            },
            MarketStatuses = new[]
            {
                new MarketSourceStatusRecord
                {
                    Source = "TENCENT_QT",
                    Status = "OK",
                    LastSuccessAt = "2026-06-22 10:05:00"
                }
            }
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void RuntimeLogTencentQt_CurrentFailureGeneratesMarketAlert()
    {
        var evaluator = new AlertRuleEvaluator();

        AlertEvent alert = Assert.Single(evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            RuntimeLogs = new[]
            {
                new RuntimeLogRecord
                {
                    Id = 32,
                    Time = "2026-06-22 10:00:00",
                    Level = "ERROR",
                    Module = "TENCENT_QT",
                    Message = "Tencent quote unavailable",
                    Detail = "qt.gtimg.cn timeout"
                }
            }
        }));

        Assert.Equal(AlertTypes.MarketRuntime, alert.AlertType);
        Assert.Contains("TENCENT_QT", alert.DedupeKey);
        Assert.Contains("TENCENT_REALTIME_UNAVAILABLE", alert.DedupeKey);
    }

    [Fact]
    public void AccountReplayFinancialError_GeneratesSevereAlert()
    {
        var evaluator = new AlertRuleEvaluator();
        IReadOnlyList<AlertEvent> alerts = evaluator.Evaluate(new AlertRuleEvaluationInput
        {
            AccountReplayState = new AccountReplayStateRecord
            {
                ReplayStatus = "财务异常",
                ReplayError = "现金审计失败"
            }
        });

        AlertEvent alert = Assert.Single(alerts);
        Assert.Equal(AlertTypes.AccountReplay, alert.AlertType);
        Assert.Equal(AlertSeverity.Severe, alert.Severity);
    }

    [Fact]
    public void DedupeKey_UsesTypeStrategyActionAndReason()
    {
        string first = AlertEvent.BuildDedupeKey(AlertTypes.StrategyDecision, "159941", "全清换现金(留底)", "极端溢价");
        string second = AlertEvent.BuildDedupeKey(AlertTypes.StrategyDecision, "159941", "全清换现金(留底)", "极端溢价");
        string changed = AlertEvent.BuildDedupeKey(AlertTypes.StrategyDecision, "159941", "战略底仓", "逢低吸筹");

        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }
}
