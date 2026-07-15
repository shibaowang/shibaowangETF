# CrossETF.Terminal.UiShell.Reference

## 2026-06-30 TASK-MARKET-US-OPEN-QUOTE-RELEASE-042 US-open index quote throttle release

- Fixed the US-open transition where a pre-open `IndexQuote` request could leave a 5-minute non-trading `nextAllowedAt` in memory and delay the first `ulist.np` quote refresh after 21:30 Beijing time.
- The release applies only to `MarketRequestKind.IndexQuote` on the `quote / push2.eastmoney.com / ulist.np` lane when entering US trading hours.
- Failure cooldowns from `ResponseEnded`, `RemoteDisconnected`, `HTTP 403`, `HTTP 429`, and similar transient block errors are not released.
- `IndexIntraday / trends2`, `IndexDailyHistory / push2his`, ETF Tencent quote/intraday/daily routes, Sina fund NAV, strategy logic, order drafts, TradeLog, alerts, charts, K-lines, and drawdown chart behavior are unchanged.

## 2026-06-30 TASK-UI-INTERACTION-LOCK-RELEASE-040 left dialog interaction lock and V8 republish

- `TASK-UI-INTERACTION-039` is locked in `docs/LOCKED_MODULES.md` after manual acceptance.
- Left navigation dialogs keep dark initial backgrounds, dark root containers, main-window ownership, `CenterOwner`, `ShowInTaskbar=false`, and lightweight fade-in opening.
- Covered entries are `溢价决策`, `交易日志`, `系统设置`, and `风险中心`.
- This release step only locks the accepted interaction behavior and regenerates the V8.0.0 local publish package. It does not change strategy logic, order drafts, account replay logic, holdings replay logic, TradeLog, market sources, scheduler, alerts, charts, K-lines, index intraday, drawdown charts, or broker connectivity.

## 2026-06-30 TASK-PROJECT-LOCKDOWN-031 index intraday full-session locks

- `TASK-INDEX-INTRADAY-CATCHUP-027`, `TASK-INDEX-INTRADAY-CLOSE-QUOTE-ALIGN-028`, `TASK-INDEX-INTRADAY-US-SESSION-DATE-029`, and `TASK-INDEX-INTRADAY-MACD-VOLUME-030` are locked in `docs/LOCKED_MODULES.md`.
- The locked contract covers low-frequency non-trading-hours index intraday catch-up, display-only `QUOTE_CLOSE_DISPLAY`, US Eastern trading-date ownership across Beijing midnight, full-session index intraday MACD, and source-only index volume handling.
- Future work must not change these accepted behaviors without explicit user confirmation and must keep ETF Tencent intraday, top index quote lanes, drawdown charts, global scheduler anti-IP-ban limits, alerts, orders, and TradeLog untouched.

## 2026-06-30 TASK-INDEX-INTRADAY-MACD-VOLUME-030 index intraday MACD and volume closeout

- `251.NDXTMC` and `100.NDX100` intraday MACD now uses the same full US Eastern regular-session display sequence as the main intraday chart. Index MACD is no longer tail-trimmed by the ETF `MaxIntradayDisplayPoints=260` limit, so it keeps the `09:30-16:00 ET` sequence after the normal EMA warm-up.
- `QUOTE_CLOSE_DISPLAY` remains display-only and can participate as the final close display point for index intraday MACD, but it is never persisted to `chart_intraday_cache` and does not fill missing middle-minute points.
- Index volume display continues to depend strictly on real source volume fields. Current diagnostics show `251.NDXTMC` EastMoney trends payload has volume fields but all values are zero, so it correctly remains `成交量数据不可用`; `100.NDX100` has real non-zero volume and remains drawable.
- ETF Tencent intraday, ETF K-lines, top index quote lane, drawdown quote refresh, global scheduling/rate limiting, alerts, orders, and TradeLog behavior are unchanged.

## 2026-06-30 TASK-INDEX-INTRADAY-US-SESSION-DATE-029 index intraday US session date ownership

- `251.NDXTMC` and `100.NDX100` intraday snapshots now preserve the full US regular session across Beijing midnight. A US session such as `09:30-16:00 ET` can span `21:30-04:00` Beijing time and must remain one displayed sequence.
- Index intraday display no longer applies the ETF/A-share `MaxIntradayDisplayPoints=260` tail trim. Full EastMoney `REAL_TRENDS2` index sessions are kept on the fixed `09:30-16:00` Eastern axis so the chart does not start at noon.
- Index cache completeness now requires the first real point to reach the morning session and the last real point to reach the close area. A `12:00-16:00 ET` half-session is not considered complete.
- `QUOTE_CLOSE_DISPLAY` remains display-only and only calibrates the final close point; it does not remove morning points, does not write `chart_intraday_cache`, and does not add network requests. ETF Tencent intraday and ETF K-lines are unchanged.

## 2026-06-30 TASK-INDEX-INTRADAY-CLOSE-QUOTE-ALIGN-028 index intraday close quote display alignment

- `251.NDXTMC` and `100.NDX100` intraday chart snapshots now calibrate the display-side final close point with the latest index quote only after the US regular session is complete.
- The alignment requires a complete real EastMoney intraday cache for the latest completed US trading day and a same-day after-close quote. The display point is marked `PointSource=QUOTE_CLOSE_DISPLAY` and `IsQuoteCloseDisplayPoint=true`.
- The aligned close point is in-memory display data only: it does not write `chart_intraday_cache`, does not fill middle-minute data, does not smooth the curve, and does not create network requests.
- During US trading hours or when the cache is partial, the quote remains independent display information and the real intraday line is not forcibly connected to it. ETF Tencent intraday and ETF K-line behavior are unchanged.

## 2026-06-30 TASK-INDEX-INTRADAY-CATCHUP-027 index intraday after-hours catch-up

- `251.NDXTMC` and `100.NDX100` intraday charts now detect whether the latest real EastMoney intraday cache covers the latest completed US regular session. A cache is treated as complete when its last real point maps to about `15:55-16:00` Eastern Time for the latest completed US trading day.
- When an index chart is opened outside US trading hours and the latest cache is missing, older than the latest completed session, or partial, the coordinator may attempt one low-frequency EastMoney `trends2` catch-up through `GlobalMarketRequestScheduler`.
- Catch-up success writes only parseable real `EASTMONEY_INTRADAY / REAL_TRENDS2` payloads to `chart_intraday_cache`. Catch-up failure keeps the existing cache and keeps the latest quote as an independent display marker; it never fills middle-minute data or connects quote into the real intraday line.
- ETF Tencent intraday, ETF K-line, top index quote lanes, and index drawdown realtime quote behavior are unchanged.

## 2026-06-29 TASK-PROJECT-LOCKDOWN-026 index quote and drawdown realtime locks

- Accepted index quote behavior is locked for `TASK-MARKET-INDEX-QUOTE-LANE-023`: `IndexQuote / ulist.np` stays isolated from `IndexIntraday / trends2` and `IndexDailyHistory / push2his`. `IndexQuote` remains protected at 2-4 seconds during US trading, while intraday/history remain low-frequency lanes.
- Accepted drawdown refresh behavior is locked for `TASK-INDEX-DRAWDOWN-QUOTE-REFRESH-024`: when local quote cache changes for `251.NDXTMC` or `100.NDX100`, the app refreshes `_marketQuotes`, the top quote cards, and both drawdown charts without adding drawdown-chart network requests.
- Accepted latest-point behavior is locked for `TASK-INDEX-DRAWDOWN-LATEST-POINT-025`: latest quote is display-only, same-date latest quote replaces the display tail, newer-date latest quote appends a display tail, and `market_history_cache` is not written.
- Any future change to these paths must update `docs/LOCKED_MODULES.md`, keep `GlobalMarketRequestScheduler` anti-IP-ban protections, and preserve the protection tests named there.

## 2026-06-29 TASK-PROJECT-LOCKDOWN-018 accepted module locks

- `docs/LOCKED_MODULES.md` is the active guardrail for accepted modules. New work must read it before touching TradeLog, order drafts, strategy VBA parity, market routing, chart cache, global scheduling, alerts, probe/smoke safety, or K-line live-bar display behavior.
- Locked chart K-line behavior: Daily/Weekly/Monthly chart snapshots may include `QUOTE_INTRADAY_BAR` only as a display-only quote OHLC bar; it must not be written to `market_history_cache`, must not replace DailyLike history, and must not be created when quote OHLC is incomplete.
- Any task that needs to modify a locked module must first state the affected lock, reason, file/test impact, data-count impact, request-frequency impact, scheduler impact, and wait for user confirmation.

## 2026-06-29 TASK-CHART-KLINE-LIVE-BAR-017 chart live K bar display

- ETF and the two index chart snapshots may append a current trading-day display-only K bar when the latest DailyLike cache is older and the current quote contains real open/high/low/last fields.
- The display-only K bar uses `PointSource=QUOTE_INTRADAY_BAR`, participates in Daily/Weekly/Monthly chart rendering and MACD, and is never persisted to `market_history_cache`.
- Missing quote OHLC keeps the real DailyLike cache unchanged. Weekly/Monthly charts still aggregate locally from DailyLike plus the optional display-only bar and do not perform extra network requests.

## 2026-06-26 TASK-INDEX-CHART-PERIOD-FIDELITY-012 index period closeout

- `251.NDXTMC` and `100.NDX100` index quote/history/intraday data remain on EastMoney; ETF Tencent quote/intraday/daily routing is unchanged.
- EastMoney index quote requests include real OHLC fields `f15/f16/f17/f18`, parsed with `/100` scaling. When US trading is in progress and DailyLike history only has the last completed session, a real-OHLC quote can create a display-only current-session K bar (`QUOTE_INTRADAY_BAR`) for Daily/Weekly/Monthly chart snapshots.
- The display-only index K bar is never persisted to `market_history_cache`, never replaces DailyLike history, and is not created when quote OHLC is missing. Index quote tails also remain display-only markers and are not written to `chart_intraday_cache` or used to fill missing middle-minute intraday data.

## 2026-06-25 TASK-INDEX-INTRADAY-AXIS-009 index intraday US Eastern axis

- `251.NDXTMC` and `100.NDX100` intraday charts use a fixed US regular-session axis: `09:30-16:00` Eastern Time. EastMoney timestamps are converted from China time to Eastern Time through `TimeZoneInfo`, so US daylight saving time is handled by the operating system time-zone rules.
- Partial-session index data is no longer stretched to fill the chart width. A point at 10:15 Eastern maps to `45 / 390` of the plot width and the right side remains empty until later real points arrive.
- Index axis labels show `09:30`, `16:00`, and `美东时间`. ETF intraday charts are unchanged and still use the A-share compressed `09:30-11:30` and `13:00-15:00` axis.
- Quote tail points for indexes are display-only and use the same Eastern-time mapping; they are not forced to the right edge and are not persisted as intraday history.

## 2026-06-25 TASK-READONLY-INDEX-CHART-DATA-007 index chart route closeout

- `251.NDXTMC` and `100.NDX100` chart quote, intraday, and DailyLike history stay on EastMoney. ETF Tencent quote/intraday/daily routing is unchanged.
- Index intraday points from EastMoney can arrive in the overseas session, so they must not be filtered by the ETF/A-share `09:30-11:30` and `13:00-15:00` axis. Index intraday charts now keep the real EastMoney point sequence and draw it by point order with dynamic time labels.
- ETF intraday charts still use the standard A-share trading-time axis and continue to ignore out-of-session points.
- `index_chart_data_trace.json` records the two index diagnostics. It is a diagnostic artifact only and does not write TradeLog, order drafts, alerts, or trading data.

## 2026-06-25 TASK-MARKET-KLINE-FRESHNESS-006 K-line freshness

- ETF chart Daily/Weekly/Monthly freshness is unified around real DailyLike data. Active ETF daily subscriptions still route to Tencent qfq daily, and active index daily subscriptions still route to EastMoney. Existing DailyLike cache no longer suppresses a chart-window refresh attempt forever; requests remain rate-limited and circuit-breaker protected.
- Daily K-line selection now compares the in-memory chart DailyLike cache with SQLite `market_history_cache` DailyLike records by last K-line date first and cache/update time second. A stale in-memory 5/23 cache can no longer hide a newer SQLite DailyLike cache. Newer MonthlyLike/Sparse/Invalid rows are still ignored for daily display.
- Weekly and monthly views continue to aggregate from the selected real DailyLike daily sequence, so they follow the same latest trading date as Daily. They do not read old week/month cache and do not use MonthlyLike data as a substitute for DailyLike.
- `kline_freshness_trace.json` records interface/cache/chart freshness diagnostics for enabled ETFs plus `251.NDXTMC` and `100.NDX100`. It is a diagnostic artifact only and does not write TradeLog, order drafts, alerts, or trading data.

## 2026-06-25 TASK-MARKET-TENCENT-ETF-CHART-005 Tencent ETF chart data

Official market data paths must not depend on the system proxy. The existing market HTTP clients keep `UseProxy=false`, so Clash or Windows proxy settings are not used by the ETF application market-data requests.

Current routing is explicit by instrument type:

- ETF realtime quote: Tencent first through the existing `qt.gtimg.cn` quote path.
- ETF intraday chart: Tencent `minute/query` is the official real intraday path. The parser uses real minute price and converts Tencent cumulative volume/amount fields into per-minute volume/amount before drawing or caching.
- ETF daily chart: Tencent `fqkline/get?param={code},day,,,320,qfq` is the official DailyLike path. Tencent qfqday rows are normalized into the existing DailyLike payload shape before writing `market_history_cache` with `source=TENCENT_DAILY_QFQ`.
- Index quote, intraday, and daily history for `251.NDXTMC` and `100.NDX100`: EastMoney remains the official source.
- OTC fund NAV: Sina fund NAV remains the official source.

DailyLike protection is still mandatory. MonthlyLike, Sparse, Invalid, failed responses, generated points, random points, quote-derived points, and simulated data are not allowed to replace DailyLike chart data. Weekly and monthly views continue to aggregate only from real DailyLike OHLCV data.

Tencent ETF chart requests are still deduped, rate-limited, and protected by the chart circuit breaker. Successful Tencent intraday payloads are persisted only when parseable into real intraday points, using `source=TENCENT_INTRADAY` and `quality=REAL_TENCENT_INTRADAY`. Failed responses, empty payloads, quote tails, generated points, random points, and simulated data are never written.

## 2026-06-23 TASK-CHART-013 background intraday cache closeout

During the trading session, the main refresh loop now also feeds all enabled ETF strategy codes into `ChartDataRefreshCoordinator` as background intraday cache targets. This path is separate from chart-window display subscriptions: closing `SecurityChartWindow` cancels only the display subscription, while enabled symbols can still refresh real Tencent intraday cache in the background. The background updater is serial, dedupes symbols, limits requests per tick, and uses a 60-second per-symbol interval with the existing per-symbol circuit breaker. It skips lunch and other non-trading periods, and allows a single after-close catch-up pull within the first 15 minutes after 15:00.

Background intraday refresh writes only parseable real Tencent `minute/query` payloads to `chart_intraday_cache` with `quality=REAL_TENCENT_INTRADAY` and `source=TENCENT_INTRADAY`. It never writes quote tail points, failed responses, empty payloads, generated points, random points, or simulated data. If a request fails, existing in-memory or SQLite real intraday cache is kept for display fallback; if no cache exists, the chart status remains explicit instead of drawing fake data.

## 2026-06-22 TASK-CHART-012 chart intraday/daily fallback closeout

`SecurityChartWindow` continues to use only real market data. ETF intraday data is fetched from Tencent `minute/query`; after a successful real response, the raw Tencent payload is persisted to the chart-only SQLite table `chart_intraday_cache(strategy_code, actual_code, trade_date, updated_at, payload, quality, source)`. The table stores only payloads that can be parsed into real intraday points, with `quality=REAL_TENCENT_INTRADAY` and `source=TENCENT_INTRADAY`; it does not store quote tail points, failed responses, empty payloads, generated points, or simulated data.

When Tencent intraday fails, is rate-limited, or enters the per-symbol circuit breaker `TENCENT_INTRADAY:<strategy_code>`, the chart first keeps the in-memory real intraday cache, then falls back to `chart_intraday_cache`. The cache reader remains compatible with older `EASTMONEY_INTRADAY/REAL_TRENDS2` rows, but new ETF intraday writes use Tencent source and quality. If no real intraday cache exists, the chart status is explicit: `腾讯分时接口失败，无可用分时缓存` or `腾讯分时接口熔断中，无可用分时缓存`. Realtime ETF quote may still be appended as a display-only tail point, but it is never written to intraday history cache and never copied repeatedly to fabricate a line.

Daily K-line display now accepts only DailyLike data. The selected DailyLike sequence is the freshest available result across in-memory chart cache and SQLite `market_history_cache`, comparing last K-line date first and updated time second. A newer MonthlyLike/Sparse/Invalid record no longer blocks an older valid DailyLike cache. If no DailyLike data exists, the status is `无可用DailyLike日K缓存`; monthly-like data is not used as daily K-line data.

## 2026-06-22 TASK-ALERT-007 runtime_log market alert closeout

Runtime-log-derived market alerts use `app_settings.alert_runtime_log_last_processed_id` as a persistent cursor. On first upgrade, the cursor is initialized to the current `runtime_log` max id so old WARN/ERROR rows are not replayed. Later refreshes only inspect `runtime_log.id > cursor` in ascending order, advance the cursor to the newest inspected row, and keep the original `runtime_log.time` as the alert event time. `SecurityChart` runtime WARN/ERROR rows stay as chart/window status only. `EASTMONEY_HISTORY` and `TENCENT_QT` runtime rows are downgraded when the current source status is `OK` and `last_success_at` is newer than the runtime log time, while unrecovered current failures still use the existing market alert dedupe and interval.

## 2026-06-21 TASK-CHART-004C intraday volume field closeout

The ETF security chart uses real Tencent `minute/query` intraday data. Tencent minute rows provide price plus cumulative volume/amount; the parser converts those cumulative fields to real per-minute volume/amount before drawing. Intraday volume bars are scaled by `minuteVolume / maxVisibleMinuteVolume * chartHeight`; missing or zero volume does not create a fake bar, and quote tail points only update display price without generating quote-derived volume.

## 2026-06-22 TASK-CHART-006 chart change percent closeout

The ETF security chart header no longer reuses the same realtime quote change percent for every period. Intraday displays the real day change from the ETF quote or from latest price versus previous close. Daily, weekly, and monthly display the selected period's current close versus the previous period close. If a realtime quote updates the current K bar, only the current K close/high/low display is adjusted; the change percent still uses the previous daily/weekly/monthly close as the base. Missing previous-period close displays `--`. Display formatting is fixed to `+0.00%`, `-0.00%`, `0.00%`, or `--`, with rounded negative zero normalized to `0.00%`.

## 2026-06-19 ETF 标的走势图窗口
主界面“跨境ETF监控与决策”表支持双击 ETF 行打开只读走势图窗口。同一标的只保留一个窗口，重复双击会激活已有窗口；不同标的可同时打开，但统一复用行情缓存、走势图订阅、限频和熔断链路，不为每个窗口创建独立行情请求计时器。

走势图窗口提供 `分时 / 日K / 周K / 月K` 与 `成交量 / MACD` 视图。ETF 分时数据使用腾讯真实 `minute/query` 分时接口；ETF 日K使用腾讯 qfq DailyLike 历史 K 线或真实 DailyLike 缓存；周K、月K只从真实日K OHLCV 聚合生成。当前 K 棒允许用真实 ETF quote 更新显示端的收盘、高低点，但不写入历史缓存、不伪造成交量。日K在内存 `ChartCache` 和 SQLite `market_history_cache` 的 DailyLike 记录之间选择最新真实序列，再按路由尝试真实腾讯 qfq 新请求；接口失败或返回 MonthlyLike / Sparse / Invalid 时不会清空已有 DailyLike 缓存。成交量和 MACD 均基于真实分时或 K 线字段计算；分时 MACD 使用真实 `IntradayPoint.Price` 序列，并与分时主图、分时成交量共用 `09:30-11:30`、`13:00-15:00` 有效交易分钟 X 轴，最新数据未到 `15:00` 时不会用索引铺满右侧；日K / 周K / 月K MACD 使用真实 K 线收盘价，缺失或点数不足时显示不可用状态。

系统不会使用假分时、假K线、假成交量、假 MACD、随机曲线或用月线冒充日线。走势图刷新跟随主界面 2-4 秒 UI 节奏触发，但网络请求按标的统一去重、缓存、限频和熔断；关闭窗口会取消订阅。走势图打开、刷新和关闭都不会触发 PushPlus 微信、系统语音、TradeLog 写入、委托草案、模拟交易或券商下单。

分时图 X 轴固定使用场内 ETF 标准交易时间：`09:30-11:30` 与 `13:00-15:00`。坐标边界不再使用第一条分时数据、最后一条数据、当前时间或最新 quote 时间；午休断档不按 90 分钟空档拉长，`09:00`、`12:00`、`16:14` 等非交易时段点会被忽略。主图按有效交易分钟把上午最后点和下午第一点连续连接，不再把午休拆成两段折线；分时主图使用真实昨收价绘制 0% 零轴白线，并按昨收上下对称映射价格区间，缺少真实昨收时不画假线。分时成交量按真实相邻分时价格涨跌显示红柱 / 绿柱，平价继承上一柱颜色，缺量不补假柱。走势图窗口使用自定义深色 `WindowChrome`，主图和副图容器保持深色背景、深色边框和深色按钮，不回退到系统默认白框。

日K / 周K / 月K 成交量只使用腾讯 qfq 真实 K 线字段。腾讯 ETF 日K当前提供日期、开盘、收盘、最高、最低、成交量；成交额缺失时保留为空，不伪造。周K、月K由真实日K聚合生成，成交量按区间求和，不使用最后一天成交量代替。K线成交量颜色按该 K 线 `Close >= Open` 显示红柱，`Close < Open` 显示绿柱；缺量或零量不补假柱。

## 2026-06-17 顶部资产曲线口径收口
顶部“总资产（CNY）”和“持仓盈亏”小曲线只按真实快照变化推进：读取时优先使用 `account_replay_snapshot`，只有该表为空时才用 `account_replay_state` 兜底。曲线先压缩连续重复财务值，再把压缩后的真实变化点序列均匀铺展为迷你趋势图；无新快照变化时点集合和曲线形态不变，不复制最后值、不用刷新时间生成节点。单点仍显示数据不足，不生成假点、补点、随机曲线或模拟资产曲线。

## 2026-06-16 历史 K 线缓存保护补充

东方财富 `push2his` 历史 K 线接口可能偶发 `ResponseEnded` / `The response ended prematurely`。程序对历史 K 线继续使用 no-proxy `HttpClient`，并保护已有高质量日线缓存：`251.NDXTMC` 与 `100.NDX100` 已有 DailyLike 缓存时，`klt=103` 月线 fallback 或点数显著缩水的低质量 payload 不会覆盖原日线缓存。无旧缓存时允许保存真实月线 fallback 作为降级缓存，并写入 `runtime_log` 标记 `HISTORY_DEGRADED_MONTHLY_FALLBACK`。历史源短暂失败但实时行情正常、核心日线缓存有效时，右上角主连接状态不再误判为整体“部分连接”。系统仍不使用假 K 线、假高点或随机曲线。

跨境 ETF 智能投资决策系统 WPF 桌面端参考实现。

当前版本已完成本地数据录入、模块 2 真实行情联网、模块 3 TradeLog 账户回放和模块 4 策略决策收口：主界面保持既有金融终端布局，本地配置、账户、持仓、OTCMap、TradeLog 从 SQLite 读取，行情数据从真实接口异步刷新并写入本地缓存。系统不使用假行情、假高点、假曲线、假资产或假 TradeLog 冒充真实运行结果。

策略决策与源 VBA 的底仓优先口径保持一致：前置异常和行情缺失之后，只要账户底仓未完成，就优先输出 `战略底仓 / 逢低吸筹`，不会被极端溢价、普通溢价止盈、收益止盈、底仓保护、纯场外持股观察或禁建仓提前截断。底仓完成后，极端溢价、普通溢价止盈、收益止盈、T1-T6、空仓观察和持股待涨再按后续规则计算。T1-T6 已执行狙击金额按源 VBA `GetTotalGridExecutedAmt()` 的当前周期全局口径汇总，不按单只 ETF 过滤。溢价只属于场内 ETF，场外 A/C 基金、场外替代持仓和场外替代买入没有“溢价交易”概念；场内 ETF 溢价不能禁止场外替代补底仓，也不能触发场外替代持仓卖出。委托草案仍由模块 5 按真实现金、整手、OTCMap 日限额、净值和通道状态量化，主表委托金额显示真实可执行结果，不用不可执行的理论缺口金额冒充委托。

左侧导航已做入口真实化：`溢价决策` 打开复用 `ManualDataEntryWindow` 的“溢价决策配置”范围，只显示 `策略配置`、`OTCMap` 和 `底仓基准设置`；`交易日志` 打开“交易日志录入”范围，只显示 `TradeLog`；`系统设置` 和右上角齿轮打开“系统设置 / 数据维护”范围，只显示只读 `系统维护` 说明页和 `界面快捷键设置`，不再提供账户状态或持仓手动维护入口。内部 `All` 范围仍保留给测试或开发排查使用，正式 UI 不再暴露完整手动录入入口；所有入口均复用现有保存、删除、校验、深色 DataGrid、列顺序持久化和原 `app_settings` 底仓基准保存逻辑，不复制业务保存逻辑。

系统设置中的 `界面快捷键` 使用简洁版单行设置：`显示/隐藏窗口 [ Alt+1 × ]` 和 `恢复默认设置`。默认启用 `Alt+1`，内部保存为 `ui_hotkey_modifiers=Alt`、`ui_hotkey_key=D1`；用户点击快捷键胶囊后直接按新组合，注册成功即写入 `app_settings` 并立即生效，无复选框、无主键下拉、无保存按钮。支持 `Ctrl`、`Alt`、`Shift`、`Win` 至少一个修饰键，主键支持 `A-Z`、`0-9` 和 `F1-F12`。程序继续使用 Windows 原生 `RegisterHotKey` / `UnregisterHotKey` 和窗口消息 Hook，不使用全局键盘钩子。快捷键触发时只切换主窗口 `Hide()` / `Show()` 并激活到前台；注册冲突时提示“快捷键冲突，未保存”，不覆盖旧设置，不写 TradeLog。

系统设置中已加入 `预警设置`，提供 PushPlus 微信预警和本机系统语音提示的基础版配置。配置保存到既有 `app_settings`：`alert_pushplus_enabled`、`alert_pushplus_token`、`alert_voice_enabled`、`alert_repeat_interval_minutes`、`alert_severe_interval_minutes`、`alert_market_interval_minutes`。预警事件由当前系统已经生成的 `strategy_decision_state`、`order_draft_state`、`market_source_status`、账户回放状态和运行日志派生，不重新计算策略、不修改委托草案、不自动写 TradeLog。PushPlus 发送只调用官方 `http://www.pushplus.plus/send`，Token 为空时不发送并提示；系统语音使用 Windows 本机 SAPI，失败只记录状态，不影响程序运行。

行情异常预警使用 `alert_market_interval_minutes` 独立限频，并优先于严重风险间隔。`EASTMONEY_HISTORY` 等行情源的同类异常按稳定 key 去重，例如 `行情异常|EASTMONEY_HISTORY|HISTORY_KLINE_UNAVAILABLE`，不会把 `secid`、请求 URL、日期、失败次数等动态详情写入 key；这些详情只保留在日志正文中用于排查。同一 key 在行情异常间隔内不会重复发送微信、不会重复播放语音，也不会追加投递型 `alert_log`。

左侧 `风险中心` 已接入真实只读入口，打开 `风险中心 / 预警日志` 窗口并读取最近 `alert_log` 记录。窗口展示时间、类型、级别、标的、标题、微信状态、语音状态、错误信息和来源，不编辑、不重新发送预警，不读取 TradeLog 冒充风险日志。风险中心提供 `刷新日志` 和 `清空日志`：`刷新日志` 只重新读取 `alert_log` 最近 100 条并刷新表格，不触发行情、策略、账户回放、微信或语音；`清空日志` 确认后只执行 `DELETE FROM alert_log`，不清空 `alert_delivery_state`，因此不会重置预警去重限频状态，也不影响 TradeLog、交易数据、runtime_log、行情缓存或系统设置。系统设置页已支持纵向滚动，预警设置中的重复提醒、严重风险、行情异常三个间隔均可操作。系统语音提示串行播放，后一条不会打断前一条；微信失败不影响语音，语音失败不影响微信。

预警日志通道状态统一为 `成功`、`失败`、`未启用`、`不适用` 或 `--`。真实预警按用户开关投递：微信或语音关闭时显示 `未启用`，启用后显示实际 `成功` / `失败`；测试微信只测试 PushPlus，语音状态显示 `不适用`；测试语音只测试本机语音，微信状态显示 `不适用`。

## 运行

```powershell
dotnet restore .\CrossETF.Terminal.UiShell.Reference.sln
dotnet build .\CrossETF.Terminal.UiShell.Reference.sln -c Debug
dotnet run --project .\CrossETF.Terminal.UiShell.Reference.csproj
```

## 正式发布

从 `V8.2.1` 开始，正式发布必须使用 `scripts/Publish-CrossEtfRelease.ps1` 从最终标签 worktree 生成。程序集名称保持不变，脚本只在 publish 后将用户启动 AppHost 命名为 `跨境ETF.exe`，并可验证后更新当前用户桌面的 `跨境ETF.lnk`；`artifacts` 和快捷方式不得提交到 Git。`v8.2.0` 及以前发布目录不追溯改名。

数据库位置：

从 `V8.3.0` 测试版开始，系统设置的“系统维护”页提供本地 SQLite 安全备份与恢复。活动数据库使用 SQLite `BackupDatabase` 生成包含 WAL 已提交数据的一致性快照，并执行 `integrity_check` 和基础表校验；升级前及每日首次启动自动保护，手动恢复采用双确认、受控暂存、下次启动前替换、恢复前安全备份和失败回滚。备份仅保存在 `%LocalAppData%\CrossETF.Terminal.UiShell.Reference\backups`，不上传网络，不改变正式数据库路径，不自动写 TradeLog。详细约束见 `docs/TASK_DATA_BACKUP_RESTORE_011.md`。

`V8.4.0` 测试版在“系统维护”页增加只读运行稳定性面板：每 30 秒记录进程资源和主刷新状态，每 5 秒探测 Dispatcher 响应，健康记录保存在 `%LocalAppData%\CrossETF.Terminal.UiShell.Reference\health`，并可导出最近 24 小时 JSON/TXT 报告。该监测不写 SQLite、不触发行情或交易、不强制 GC，也不改变主界面原 2 至 4 秒随机刷新规则。详细边界见 `docs/TASK_RUNTIME_STABILITY_012.md`。

`V8.5.0` 独立测试版将现有“系统设置”范围重构为固定左侧二级菜单和缓存右侧页面，统一承载数据维护、备份恢复、预警、快捷键、只读诊断摘要、运行健康及版本信息。入口仍复用 `ManualDataEntryWindow`，不新增一级模块或独立窗口，不修改备份、预警、快捷键、诊断、运行健康及交易业务服务。详细边界见 `docs/TASK_UI_SETTINGS_CENTER_017.md`。

`V8.6.0` 独立测试版仅优化现有 TradeLog 页面的信息层级、四操作工具栏、编辑状态提示和数据表格视觉。TradeLog 仍是账户与持仓回放的唯一事实源，只有用户点击“保存全部”才写入数据库；保存、账务推演、账户回放、字段、下拉选项和用户列顺序持久化口径均未改变。详细边界见 `docs/TASK_TRADELOG_UI_018.md`。

`V8.7.0` 独立测试版将左侧既有“行情监控”入口接入只读行情监控中心。窗口复用固定顶部标的、启用策略、现有场内持仓、启用场外通道及本地行情缓存，显示缓存新鲜度和行情源状态；窗口只读取本地 SQLite，2 秒自动重读，不新增联网链路、手动刷新按钮或业务写入。详细边界见 `docs/TASK_MARKET_MONITOR_019.md`。

`V8.8.0` 独立测试版接通左侧既有“资金仓位”入口，新增单实例、纯本地只读的资金仓位中心。页面只读取已持久化的账户回放、场内/场外持仓回放、策略决策和真实行情缓存元数据，不重新回放 TradeLog、不重新估值、不联网、不写数据库，并以 2 秒固定间隔重读同一只读快照。详细边界见 `docs/TASK_CAPITAL_POSITION_020.md`。

```text
%LocalAppData%\CrossETF.Terminal.UiShell.Reference\cross_etf_terminal.db
```

日志位置：

```text
%LocalAppData%\CrossETF.Terminal.UiShell.Reference\logs\crash-yyyyMMdd.log
%LocalAppData%\CrossETF.Terminal.UiShell.Reference\logs\runtime-yyyyMMdd.log
```

程序启动后以 2 到 4 秒随机间隔刷新本地数据库、当前时间、行情缓存和 UI 状态。真实行情请求由后台服务按来源限频执行，不跟随 UI tick 高频请求。

## 模块 2 收口状态

- ETF 实时行情：已接入腾讯 `http://qt.gtimg.cn/q=...`，GB18030 解码。
- 指数实时行情：已接入东方财富 `https://push2.eastmoney.com/api/qt/ulist.np/get?...`。
- 场外基金净值：已接入新浪 `http://hq.sinajs.cn/list=f_基金代码`，GB18030 解码。
- 历史高点：代码已接入东方财富 `https://push2his.eastmoney.com/api/qt/stock/kline/get?...`。已确认 Clash Verge / 系统代理可能导致 `push2his.eastmoney.com` 出现 `ERR_EMPTY_RESPONSE` / `ResponseEnded`；关闭代理后同一日线接口可返回真实 `data.klines`。程序对东方财富历史 K 线使用独立 no-proxy `HttpClient`（`UseProxy=false`），用户可以继续开启 Clash 供 GPT / Codex 使用，ETF 软件国内行情接口不依赖代理。
- 防封限频、缓存、熔断、运行日志：已完成。

历史 K 线失败时，系统保持真实状态：不写假高点，不显示假曲线，不把失败标记为成功。`251.NDXTMC` 与 `100.NDX100` 指数历史 K 线优先请求真实日线 `klt=101`，失败后回退真实月线 `klt=103`；当天若只有月线级别稀疏缓存，程序会继续尝试重新获取真实日线。T1-T6 前置状态保持“未就绪”，只有 `251.NDXTMC` 和 `100.NDX100` 历史 K 线成功返回真实 `data.klines` 后，才允许变为“已就绪”并进入依赖历史高点的策略计算。

## 模块 4 收口状态

模块 4 已实现策略决策派生计算。系统读取 `strategy_config`、真实行情缓存、账户回放、持仓派生、OTCMap、TradeLog 已执行档位和真实历史高点缓存，生成 `strategy_decision_state`。ETF 表的“操作指令”“操作策略”“委托价格”读取该派生表；狙击资金池卡片读取主界面真实狙击资金池和 T1-T6 总权重份数；底部“场内 / 场外替代决策”显示场内优先、场外替代或前置未就绪。

策略决策优先级为：前置异常、极端溢价 / 禁建仓、普通溢价止盈 / 收益止盈、战略底仓基准、T1-T6 狙击、持仓观察。已有持仓但没有触发卖出、底仓补足或 T1-T6 继续买入时，显示 `√ 持股待涨 / 正常趋势`；空仓且无操作时才显示 `等待建仓 / 空仓观察`。底仓基准从 `app_settings` 读取：`base_position_mode=ratio|amount`，`base_position_ratio` 默认 `0.20`，`base_position_amount` 默认 `0`。比例输入支持 `20%`、`20`、`0.20`，统一按 `0.20` 口径保存；固定金额不能小于 0，超过本金时按本金封顶并保持狙击资金池非负。`extra_price` 为极端溢价阈值，`take_profit_price` 为溢价止盈阈值，`sell_ratio` 为收益止盈阈值，`add_premium_limit` 为补仓溢价限制。`cost_amount` 继续表示剩余总成本金额，`average_cost` 继续表示综合持仓成本单价。

底仓完成度卡片和 `strategy_decision_state` 使用同一派生口径：`base_target_amount` 为当前底仓目标金额，`base_current_cost` 为当前剩余持仓总成本，`base_completion_rate = base_current_cost / base_target_amount`。卖出底仓保护按账户级口径判断：账户级可卖超额成本为 `max(0, account_total_position_cost - base_target_amount)`，不再用单只 ETF 成本直接对比账户底仓目标。有持仓且触发极端溢价时，账户级可卖空间大于 0 才输出 `全清换现金(留底) / 极端溢价`；无持仓且触发极端溢价时直接输出 `极端溢价 / 禁止建仓`，不依赖 T1-T6 是否触发。普通溢价止盈或收益止盈命中但账户底仓未完成、可卖超额成本为 0 时，不再提前输出 `-- / 底仓保护`，而是继续进入战略底仓补齐、T1-T6 或持仓观察判断。纯场外替代持仓不会仅因场内 ETF 达到普通溢价止盈阈值就提前输出场外卖出；若当前触发 T 档，优先显示 `一档建仓完成 / 场外替代` 等场外替代持有状态，不继续生成场外买入建议。策略收益率优先采用 `position_replay_state` 聚合市值，只有没有聚合持仓时才读取 `otc_position_replay_state` 明细，避免场外持仓重复计入。

主界面 `real_sniper_pool` 定义为 `当前现金余额 - 未完成底仓缺口`，即 `max(0, available_cash - max(0, base_target_amount - base_current_cost))`；卡片保持原样，不额外显示现金余额、预算池或调试字段。T1-T6 内部档位预算基准仍按 VBA `GetRealSniperPoolBudget` 口径计算：优先使用最后一条 `tier=周期结束` 且现金余额大于 0 的现金余额，否则使用当前周期实际战略底仓买入金额，若无实际战略底仓买入则使用当前可配置底仓目标。周期结束只认 `tier=周期结束`，并作为本周期已执行狙击金额统计起点；内部预算基准不显示到主界面。本金占比、持仓盈亏、盈亏率仍使用 `cost_amount`，不会把平均成本单价当总成本，也不会用市值或现价冒充底仓基准。

T1-T6 仍受真实历史 K 前置限制。当前 `251.NDXTMC` 和 `100.NDX100` 历史 K 线未成功返回时，策略状态显示 `T1-T6前置未就绪`，不输出 T1-T6 狙击买入建议，不用手工高点、当前点位或假高点绕过前置。底仓、收益止盈、溢价止盈、极端溢价和持股待涨等不依赖历史 K 的建议仍可输出。

## 当前边界

当前预警功能只做通知提醒，不接个人微信 Hook、不模拟登录微信、不抓包微信、不接企业微信或 Server 酱，不自动交易、不自动写 TradeLog、不接券商下单。程序不做模拟行情、不做模拟交易，不提供手动刷新按钮，也没有“刷新行情”“重新拉取”“同步行情”等入口，避免绕过防封限频策略。

## 模块 3 收口状态

模块 3 已实现 TradeLog 本地财务回放。`trade_log` 是账户现金、持仓、成本和已实现盈亏的事实源；当 TradeLog 存在时，主界面账户卡片和 ETF 表格中的财务字段读取 `account_replay_state`、`position_replay_state`、`otc_position_replay_state` 派生结果，不再用手动 `account_state` / `position_state` 冒充运行结果。

回放会在程序启动、TradeLog 保存后、行情缓存更新后自动执行。缺少真实行情时只标记“估值不完整”，不使用成本价或假价格顶替市值；现金流不一致、净现金流错误、超卖、合并超过持仓等问题会显示“财务异常”并写入 `runtime_log`。策略决策已在模块 4 读取回放派生表生成建议，但 OTCMap A/C 类拆单、最终委托量化、模拟交易、券商下单和手动刷新按钮仍未实现。

TradeLog 手动录入只要求用户填写业务字段：时间、策略代码、实际代码、动作、价格、数量、金额、档位、来源、手续费、备注。`net_cash_impact`、`principal`、`cash_balance`、`total_assets` 属于系统账务字段，保存前由系统按 TradeLog 顺序自动推演并覆盖；用户不需要手动维护现金余额。买入/卖出在价格和数量有效时自动计算 `amount = price * quantity`，金额保留 2 位小数，小数数量会保留。删除 TradeLog 会同步删除数据库记录并触发回放重算；历史 ID 不连续属于正常审计行为。

TradeLog 录入链路已增加 WPF 全局异常捕获、崩溃文件日志和可恢复错误状态展示。DataGrid 编辑提交、小数输入中间态、金额自动计算、账务推演、删除同步、保存后重载和后台账户回放中的异常会记录到文件日志，并尽量写入 `runtime_log`；可恢复异常不应导致程序直接闪退。

策略配置页的收益止盈、溢价止盈、补仓溢价限制支持百分比输入。`40%`、`40`、`0.40` 都按 40% 处理并统一以 `0.40` 写入 SQLite；ETF 表按百分比显示。字段口径对齐 VBA：`sell_ratio` 为收益止盈，`take_profit_price` 为溢价止盈，`add_premium_limit` 为补仓溢价限制。

ETF 表的溢价率来自真实行情缓存中的 `price` 与 `iopv`，按 `(price - iopv) / iopv` 计算；无 IOPV 时显示 `--`，不回填现价制造假 0%。派生表中的 `cost_amount` 是剩余总成本金额，`average_cost` 是综合持仓成本单价；ETF 表“综合持仓成本”显示 `average_cost`，本金占比、持仓盈亏、盈亏率继续使用 `cost_amount`。缺行情时仍保持估值不完整，不用假行情或成本价冒充市值。

顶部“总资产（CNY）”小曲线优先读取真实 `account_replay_snapshot.total_assets` 快照；只有快照表为空时，才回退真实 `account_replay_state.total_assets` 历史记录。“持仓盈亏”小曲线读取真实 `total_pnl`，缺失时使用 `total_unrealized_pnl`。保存账户回放时会追加轻量快照，但只有关键财务字段发生真实变化才写入：总资产、总盈亏、未实现盈亏、现金、本金或估值完整性未变化时会跳过，不按 2-4 秒刷新时间制造新点；金额变化达到 0.01 会保留。曲线显示前会压缩连续重复值，并把真实变化点序列均匀铺展为迷你趋势图；没有真实回放点才显示空态，单点只显示“数据不足”，不补点、不复制最后值、不生成随机、插值或写死曲线。

总资产卡片的“今日盈亏”显示金额和百分比，统一按北京时间自然日 `[今日 00:00:00, 明日 00:00:00)` 统计。金额优先使用该自然日内真实账户快照的 `total_pnl` 差额，仍缺失时使用 `total_assets` 差额并剔除自然日内 `入金` / `出金` 净现金流，避免把入金当盈利或把出金当亏损；持仓 `daily_pnl` 纳入今日/当日盈亏时必须按行情或估值更新时间过滤，SINA_FUND 优先使用 `market_quote_cache.received_at`，`quote_time` 仅作兜底。今天场外基金/NAV 更新计入今天，昨天更新不计入今天，明天不重复计入今天；不得按资产类型排除场外基金。场内 ETF 当日盈亏保留券商兼容实时显示口径，可按 `(当前真实现价 - 昨收价) * 当前持仓数量` 展示，但不得把过期估值误计入今日。百分比按今日盈亏金额 / 今日起始总资产计算，分母不可用时使用最新总资产扣除今日盈亏后的金额；数据不足时显示 `--`。正数按 A 股口径显示红色，负数显示绿色，0 或无值为中性色。账户状态卡片使用“持仓市值”名称，ETF 表列名使用“ETF高点”。狙击资金池卡片显示主界面真实狙击资金池和可用档位份数；策略派生表未生成前显示“待策略模块”，不写死档位份数。

今日/当日盈亏的最新锁定口径以 `LOCK-PNL-NATURAL-DAY-EVENT-FILTER-001` 为准：任意自然日 `D` 均使用 `[D 00:00:00, D+1 00:00:00)` 事件过滤；前一天已经发生的盈亏不计入今天，今天发生的盈亏不在明天重复计入。SINA_FUND / NAV 不能仅凭 `received_at` 或仅凭 `quote_time == today` 判断，必须识别该 NAV/PnL 事件是否在目标自然日新发生。顶部今日盈亏和跨境 ETF 表格当日盈亏使用同一套 `EtfDecisionTableMetrics` 有效项口径。
## 模块 5 收口状态

模块 5 已新增 `order_draft_state`、`order_draft_leg_state`、`order_finalization_state`、`order_finalization_leg_state`。系统读取 `strategy_decision_state`、账户回放、场内/场外持仓回放、OTCMap、TradeLog 和真实行情缓存，生成委托草案；场内 ETF 买入按 100 股整手向下取整，受现金和真实狙击资金池约束，卖出受持仓数量和底仓保护约束；场外买入按启用通道、优先级、日限额、最低申购额拆单，场外卖出优先 C 类并要求真实净值。

主界面底部左侧显示委托草案预览和定稿状态。“定稿当前草案”只冻结当前可执行草案到定稿表，不是成交，不写入 `trade_log`，不会自动生成 TradeLog，也不会接券商下单。定稿与当前草案 `snapshot_key` 不一致时显示可能失效，需要用户重新核对。

模块 5 仍保持边界：不做模拟行情、不做模拟交易、不新增手动刷新按钮、不改策略口径、不改行情接口、不改账户回放、不改曲线逻辑、不接券商。
