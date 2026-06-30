# TASK-ALERT-001: PushPlus 微信预警与系统语音提示基础版

## 实现范围

- 新增统一预警事件 `AlertEvent`，作为微信预警和系统语音提示的共同输入。
- 新增 `AlertRuleEvaluator`，只从当前系统已有状态派生预警事件，不重算策略。
- 新增 `AlertDedupService`，按 `alert_type + strategy_code + action + reason` 形成去重键。
- 新增 `AlertDeliveryService`，统一处理 PushPlus 和系统语音投递、日志和去重状态。
- 新增 `PushPlusAlertSender`，调用 PushPlus 官方 `http://www.pushplus.plus/send`。
- 新增 `VoiceAlertPlayer`，通过 Windows 本机 SAPI 播放本地语音提示。
- 新增 `alert_log` 和 `alert_delivery_state` 两张通知表。
- 在系统设置中新增 `预警设置`，支持启用微信、填写 PushPlus Token、测试微信、启用系统语音、测试语音、重复提醒间隔、严重风险间隔、行情异常间隔。
- 系统设置内容区支持纵向滚动，窗口高度不足时也能看到完整预警设置和快捷键设置。
- 左侧 `风险中心` 接入只读 `风险中心 / 预警日志` 窗口，读取最近 `alert_log`。
- 风险中心滚动条、表格边框和确认弹窗保持深色主题。
- 风险中心提供 `刷新日志`，只重新读取 `alert_log` 最近 100 条并刷新当前表格，不触发行情刷新、策略刷新、账户回放、微信重发或语音重播。
- 风险中心提供 `清空日志`，确认后只清空 `alert_log`，保留 `alert_delivery_state`，不重置预警去重限频状态。
- 系统语音提示改为串行同步播报，避免 SAPI 异步播报对象提前释放导致中途停止。
- 预警日志通道状态统一为 `成功`、`失败`、`未启用`、`不适用` 或 `--`。真实预警按微信/语音开关投递；测试微信只走 PushPlus，语音状态记为 `不适用`；测试语音只走本机语音，微信状态记为 `不适用`。

## 设置项

预警设置保存到既有 `app_settings`，不新增设置表：

```text
alert_pushplus_enabled
alert_pushplus_token
alert_voice_enabled
alert_repeat_interval_minutes
alert_severe_interval_minutes
alert_market_interval_minutes
```

默认值：

```text
微信预警：关闭
PushPlus Token：空
系统语音：关闭
重复提醒间隔：30 分钟
严重风险间隔：5 分钟
行情异常间隔：10 分钟
```

Token 为空时不会发起 PushPlus 请求，也不会把 Token 写入运行日志。测试微信不受去重限制，但仍要求 Token 非空。

## 触发来源

第一版预警不重新实现策略规则，只读取以下现有结果：

- `strategy_decision_state`
- `order_draft_state`
- `market_source_status`
- `account_replay_state`
- `runtime_log`

VBA 迁移来的策略关键字：

```text
全清换现金
止盈减仓
溢价达标减仓
逢低吸筹
战略底仓
狙击一档
狙击二档
狙击三档
狙击四档
狙击五档
狙击六档
```

WPF 第一版增强关键字和来源：

```text
极端溢价
禁止建仓
委托不可执行
行情异常
账户回放异常
```

## 去重与限频

去重键：

```text
alert_type + strategy_code + action + reason
```

同一去重键在对应间隔内不重复投递。行情异常使用 `alert_market_interval_minutes`，且优先级高于严重风险间隔；同一行情源的同类异常会归一为稳定 key，例如 `行情异常|EASTMONEY_HISTORY|HISTORY_KLINE_UNAVAILABLE`，不会把 `secid`、URL、日期、失败次数等动态错误详情写进去。行情异常在限频窗口内即使内容哈希变化也不会再次投递；测试微信和测试语音仍仅作为用户手动测试绕过去重。默认间隔为普通预警 30 分钟、严重风险 5 分钟、行情异常 10 分钟。

## 边界

本功能只做通知提醒：

- 不接个人微信 Hook。
- 不模拟登录微信。
- 不抓包微信客户端。
- 不接企业微信、Server 酱或多平台通知。
- 不自动写 TradeLog。
- 不自动交易。
- 不接券商。
- 不修改策略计算。
- 不修改委托草案计算。
- 不修改行情接口。
- 不做模拟行情或模拟交易。

## 验收要点

1. 系统设置中可见 `预警设置`、`PushPlus Token`、`测试微信`、`启用系统语音`、`测试语音` 和三个间隔设置。
2. Token 为空时点击测试微信提示未配置，不发起 HTTP 请求。
3. 单元测试使用 fake sender / fake voice / fake handler，不真实调用 PushPlus 网络。
4. 预警开启后，后台刷新只按现有状态派生通知事件，不阻塞 UI。
5. 投递结果写入 `alert_log`，去重状态写入 `alert_delivery_state`。
6. 点击左侧 `风险中心` 可打开真实只读预警日志；无日志时显示 `暂无预警日志`。
7. 测试语音和真实预警语音应完整播报，多条语音串行播放，互不打断。
8. 点击 `刷新日志` 后只刷新 `alert_log` 显示列表，不触发微信、语音、TradeLog、行情或策略流程。
9. 点击 `清空日志` 后必须确认；确认后只清空 `alert_log`，不清空 `alert_delivery_state`、TradeLog、runtime_log 或行情缓存。
# 2026-06-22 TASK-ALERT-007 runtime_log market alert closeout

- Runtime-log-derived market alerts now use `app_settings.alert_runtime_log_last_processed_id` as a persistent cursor.
- On first upgrade, the cursor is initialized to the current `runtime_log` max id so old WARN/ERROR rows are not replayed.
- Later evaluations only read `runtime_log.id > cursor` in ascending id order, and the cursor advances to the newest inspected row even when rows are skipped or downgraded.
- Runtime-log-derived `AlertEvent.CreatedAt` uses the original `runtime_log.time` instead of the current refresh time.
- `SecurityChart` runtime WARN/ERROR rows are kept as chart/window runtime status and do not generate PushPlus or voice alerts.
- `EASTMONEY_HISTORY` and `TENCENT_QT` runtime rows are downgraded when the current `market_source_status` is `OK` and `last_success_at` is newer than the log time. `SKIP_HISTORY_DOWNGRADE` rows are also skipped.
- Current unrecovered source failures still generate market alerts and continue to use the existing market interval dedupe.
