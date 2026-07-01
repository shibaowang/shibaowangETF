# 已收口模块锁定清单

本文档用于约束后续 Codex 任务：任何新修复开始前，都应先阅读本文件。除非用户明确授权，后续任务不得修改本文档列出的已收口行为。

最近锁定任务：`TASK-UI-INTERACTION-039`

## 1. 已锁定模块

### 1.1 TradeLog 事实源

收口状态：
- TradeLog 是账户、资金、持仓、成本、盈亏回放的事实源。
- 行情刷新、图表打开、策略决策、委托草案、预警触发均不得自动写 TradeLog。
- 只有用户在交易日志/手动录入中明确保存，才允许写 TradeLog。

禁止改动：
- 禁止自动成交写入 TradeLog。
- 禁止图表、行情、预警、策略、委托草案刷新时写 TradeLog。
- 禁止清空或重写历史 TradeLog。

### 1.2 委托草案

收口状态：
- 委托草案只生成建议，不代表成交。
- 定稿只冻结草案，不下单、不接券商、不写 TradeLog。

禁止改动：
- 禁止把定稿当成交。
- 禁止自动下单。
- 禁止绕过用户确认写 TradeLog。

### 1.3 策略 VBA 口径

收口状态：
- T1-T6 已执行狙击金额采用 VBA 全局口径。
- 底仓未完成时战略底仓优先。
- 场外没有场内溢价交易概念。
- 场内 ETF 溢价不得触发场外基金卖出。
- `buySource` 口径按“是否有启用 OTC 通道 + prem > addPremLim”判断。

禁止改动：
- 禁止擅自修改 `StrategyDecisionService` 的 VBA 对齐逻辑。
- 禁止修改 T 档、底仓优先、极端溢价优先级、场外替代、收益止盈/溢价止盈口径。

### 1.4 行情数据源路由

收口状态：
- ETF 标的：腾讯优先。
- 指数：东方财富。
- 场外基金净值：新浪。
- 正式路径 `UseProxy=false`，不依赖系统代理。

禁止改动：
- 禁止 ETF 图表默认回退 EastMoney trends2/push2his。
- 禁止指数误走腾讯。
- 禁止 ETF/指数互相冒充数据。
- 禁止 system proxy fallback。

### 1.5 腾讯 ETF 图表数据源

收口状态：
- ETF quote：`qt.gtimg.cn`。
- ETF 分时：腾讯 `minute/query`。
- ETF 日K：腾讯 `fqkline daily_qfq`，只接受 DailyLike。
- ETF 周K/月K由本地 DailyLike 聚合。
- ETF 分时成交量/成交额按真实字段处理，累计字段必须差分。
- quote 不得复制成分时历史。

禁止改动：
- 禁止用 quote 补全天分时。
- 禁止用 MonthlyLike 冒充 DailyLike。
- 禁止高频请求 `minute/query` 或 `daily_qfq`。

### 1.6 两个回撤指数图表数据源

收口状态：
- `251.NDXTMC` / `100.NDX100` 使用 EastMoney。
- 指数 quote 与指数分时是不同 endpoint，quote 成功不代表 trends2 成功。
- 指数分时使用美东 `09:30-16:00` 固定轴。
- 指数 quote 尾点只独立显示，不参与主折线，不写缓存。
- 指数日K使用 EastMoney DailyLike；周K/月K本地聚合。
- 成交量不可用不影响主图价格线。

禁止改动：
- 禁止把 quote 尾点强行连接到主线。
- 禁止用 quote 伪造指数分时历史。
- 禁止把指数路由改到腾讯。

### 1.7 K线新鲜度

收口状态：
- ETF 日K使用 `TENCENT_DAILY_QFQ` 最新 DailyLike。
- 指数日K使用 EastMoney DailyLike。
- DailyLike 优先于 MonthlyLike；同质量下 `updated_at` 最新优先。
- 周K/月K跟随最新 DailyLike 本地聚合。
- MonthlyLike 不得冒充日K。

禁止改动：
- 禁止用 MonthlyLike 冒充 DailyLike。
- 禁止为周K/月K单独联网。
- 禁止高频请求 daily/push2his。

允许例外：
- 若用户明确要求，可在图表显示层增加“当前交易日显示用临时K线”。
- 该临时K线只能来自真实 quote 的 OHLC 字段。
- 该临时K线只用于显示，不写入 `market_history_cache`，不替代真实 DailyLike。

### 1.8 K线盘中显示用临时K线

收口状态：
- 日K/周K/月K显示层使用 DailyLike 历史K + 可选 quote 临时K线。
- 临时K线只在 quote 具备真实 open/high/low/last 时生成。
- 临时K线只用于显示，不得写入 `market_history_cache`。
- 临时K线不得替代真实 DailyLike。
- 临时K线必须标记 `IsDisplayOnly=true`、`PointSource=QUOTE_INTRADAY_BAR`。
- quote 缺少 OHLC 时不得用当前价造K。
- 周K/月K只本地聚合，不联网；显示层可以包含临时日K。
- 该模块已人工验收通过：`159509` 日K能显示当前交易日；周K/月K包含当前交易日所在周/月；`251.NDXTMC`、`100.NDX100` 能显示当前交易日临时K线。

禁止改动：
- 禁止把临时K线写入 `market_history_cache`。
- 禁止用 quote 替代或覆盖真实 DailyLike。
- 禁止 quote 缺少 open/high/low/last 时用当前价造K。
- 禁止周K/月K为了实时显示单独联网。
- 禁止改变 ETF 腾讯和指数东方财富的数据源路由。

允许改动条件：
- 只有用户明确授权修复图表显示层时，才允许调整临时K线显示逻辑。
- 调整前必须说明是否影响 `TradeLog` / `order_draft_state` / `alert_log` / `runtime_log` / `market_history_cache`。
- 调整后必须验证临时K线仍仅为显示用，不持久化。

### 1.9 顶部行情专业显示

收口状态：
- 顶部行情卡片不显示以下状态词：`过期`、`缓存`、`未连接`、`交易中`、`收盘`、`午休`、`盘后缓存`、`休市`。
- 有 quote 缓存就显示数值和涨跌。
- 无数据才显示 `--`。
- 右上角全局连接状态可以保留。

禁止改动：
- 禁止重新在单个顶部行情卡片中显示状态词。

### 1.10 分时首屏缓存

收口状态：
- 打开走势图后，首屏必须先读内存 `ChartCache`。
- 内存没有时再读 SQLite `chart_intraday_cache`。
- 有真实缓存点应立即绘图。
- live 请求只作为后台补充。
- live 失败不得清空当前图表。

禁止改动：
- 禁止打开图表时等待 live 成功才绘制。
- 禁止 live 失败后清空已有曲线。

### 1.11 后台分时防封更新

收口状态：
- 后台分时更新必须走 `GlobalMarketRequestScheduler`。
- 每轮最多 1 个非 quote 图表请求。
- 活动窗口标的不被后台重复请求。
- ETF 分时只在 A股交易时段请求。
- 指数分时只在美东 `09:30-16:00` 请求。
- 非交易时段只读缓存。

禁止改动：
- 禁止绕过调度器直连。
- 禁止高频请求 minute/query、trends2、daily_qfq、push2his。

### 1.12 全局行情请求调度

收口状态：
- 全局随机 2-4 秒 tick 保留。
- tick 只唤醒统一调度器，不等于所有接口联网。
- 调度按 host + endpoint + symbol 限频。
- `ResponseEnded`、`RemoteDisconnected`、`curl_exit=56`、`403`、`429` 进入 10/30/60 分钟冷却。
- 周K/月K不联网。
- 历史高点可由实时 quote 本地抬升，不为高点单独打接口。

允许批量：
- ETF quote。
- 指数 quote。
- 新浪基金净值。

禁止高频批量：
- `minute/query`。
- `daily_qfq`。
- `trends2`。
- `push2his`。
- 历史高点。

### 1.13 Probe 与 smoke mode

收口状态：
- TencentProbe 默认 dry-run。
- `-Live` 必须显式指定。
- 批量 live 必须同时指定 `-AllowBatch -Yes`。
- `CROSSETF_SMOKE_MODE=1` 禁用后台行情刷新、图表预热、真实预警投递。

禁止改动：
- 禁止 probe 默认联网。
- 禁止 smoke mode 写 alert_log 或触发真实投递。

### 1.14 预警系统

收口状态：
- PushPlus + 系统语音共用 AlertEvent。
- 测试微信不调用语音。
- 测试语音不调用微信。
- 清空风险中心日志只清 `alert_log`，不清 `alert_delivery_state`。
- `runtime_log` 历史游标不回放旧日志。
- SecurityChart 默认不推微信/语音。

禁止改动：
- 禁止个人微信 Hook、模拟登录、抓包微信客户端。
- 禁止图表打开触发策略预警。

### 1.15 Index quote and drawdown realtime locks

Locked tasks:
- `TASK-MARKET-INDEX-QUOTE-LANE-023`
- `TASK-INDEX-DRAWDOWN-QUOTE-REFRESH-024`
- `TASK-INDEX-DRAWDOWN-LATEST-POINT-025`

Accepted behavior:
- `IndexQuote / ulist.np`, `IndexIntraday / trends2`, and `IndexDailyHistory / push2his` must stay in separate scheduler lanes.
- `IndexQuote`: `push2.eastmoney.com / quote / ulist.np`, protected at 2-4 seconds during US trading hours.
- At the US open transition, `IndexQuote` may release a stale non-trading 5-minute throttle so the first trading-session quote can refresh immediately.
- The US-open release must not clear failure cooldowns from `ResponseEnded`, `RemoteDisconnected`, `HTTP 403`, `HTTP 429`, or similar transient block errors.
- `IndexIntraday`: `push2.eastmoney.com / intraday / trends2`, protected at 60-120 seconds.
- `IndexDailyHistory`: `push2his.eastmoney.com / history / kline/get`, protected as a low-frequency history lane.
- `trends2` or `push2his` rate limits must not slow top index quote refresh.
- Global scheduler rate limits must not be bypassed, removed, or weakened.
- Anti-IP-ban protection must not be reduced.

Drawdown refresh contract:
- After background market refresh succeeds, quote price/time changes for `251.NDXTMC` or `100.NDX100` must refresh `_marketQuotes`, top quote cards, and both index drawdown charts.
- Drawdown charts must read only local quote cache and history cache.
- Drawdown charts must not add any network request.

Latest point display contract:
- `IndexDrawdownChartSeriesBuilder` must use latest quote as the display-side latest point.
- If `latestPoint.Date > lastHistoryDate`, append `latestPoint`.
- If `latestPoint.Date == lastHistoryDate`, replace the display-side history tail with `latestPoint`.
- Current drawdown, series tail, and floating label must use latest quote.
- Current drawdown formula is `latestQuotePrice / historicalHigh - 1`.
- Same-date replacement uses latest quote for `Close`; `High` and `Low` preserve reasonable extrema from the history point and latest quote point.
- Latest quote display points must not write `market_history_cache`.
- Latest quote display points must not trigger any network request.

Index isolation contract:
- `251.NDXTMC` drawdown chart must use only `251.NDXTMC` quote.
- `100.NDX100` drawdown chart must use only `100.NDX100` quote.
- The two index quote streams must not cross-contaminate each other.

Protection tests:
- `GlobalMarketRequestSchedulerTests`: `IndexQuote` is not blocked by `trends2/history`; `IndexQuote` remains rate-limited; `IndexIntraday` remains low-frequency; `IndexDailyHistory` remains low-frequency.
- `GlobalMarketRequestSchedulerTests`: `IndexQuote` releases only the stale non-trading 5-minute throttle at US open, then returns to the 2-4 second trading lane; failure cooldowns are not released.
- `IndexDrawdownQuoteRefreshHelperTests`: quote price/time changes trigger refresh; unchanged quotes do not; `251.NDXTMC` and `100.NDX100` are evaluated independently.
- `IndexDrawdownChartSeriesBuilderTests`: latest quote changes current drawdown; same-date latest point replaces display tail; series tail uses latest quote; index streams remain isolated.

### 1.16 Index intraday full-session display locks

Locked tasks:
- `TASK-INDEX-INTRADAY-CATCHUP-027`
- `TASK-INDEX-INTRADAY-CLOSE-QUOTE-ALIGN-028`
- `TASK-INDEX-INTRADAY-US-SESSION-DATE-029`
- `TASK-INDEX-INTRADAY-MACD-VOLUME-030`

Catch-up contract:
- When a non-trading-hours `251.NDXTMC` or `100.NDX100` index intraday chart opens and the latest real intraday cache is missing, stale, or partial, the app may do one low-frequency catch-up for the latest completed US trading day through `GlobalMarketRequestScheduler`.
- Catch-up must use only real EastMoney `trends2` data for these indexes.
- Catch-up must not bypass `GlobalMarketRequestScheduler`, must not retry at high frequency, and must keep the per `symbol + latestCompletedUsTradeDate` cooldown.
- Catch-up failure must not clear existing real cache.
- Latest quote remains an independent display marker unless the cache is complete enough for `QUOTE_CLOSE_DISPLAY`.
- Catch-up must not fill middle-minute points, must not copy quote into intraday history, and must not write quote-generated points into `chart_intraday_cache`.

Quote close display contract:
- Outside US trading hours, when real `EASTMONEY_INTRADAY / REAL_TRENDS2` cache covers the latest completed US trading day and its tail reaches about `15:55-16:00 ET`, a same-day latest quote may create a display-side `16:00` close point.
- That point must be marked `PointSource=QUOTE_CLOSE_DISPLAY` and `IsQuoteCloseDisplayPoint=true`.
- The displayed final price may align with the top quote, but the point is display-only.
- `QUOTE_CLOSE_DISPLAY` must not write `chart_intraday_cache`, must not fill middle minutes, must not generate volume, and must not connect a partial intraday cache directly to quote.
- During US trading hours, quote still remains an independent tail marker.

US session date ownership contract:
- Index intraday display must be owned by the US Eastern trading date, not by Beijing `Date`.
- The US regular session is `09:30-16:00 Eastern Time`.
- A single US session can span `21:30-23:59` Beijing time and `00:00-04:00` Beijing time; those points must be merged into one displayed session.
- `251.NDXTMC` and `100.NDX100` charts must preserve the full `09:30-16:00 ET` sequence when the real cache contains it.
- Cache completeness must be evaluated by Eastern time: first point near `09:30`, last point near `15:55-16:00`.
- `QUOTE_CLOSE_DISPLAY` must not drop morning-session points.
- ETF intraday remains on the A-share time axis and must not use the US cross-date logic.

MACD and volume contract:
- Index intraday MACD for `251.NDXTMC` and `100.NDX100` must use the complete US Eastern trading-day display sequence.
- Index intraday MACD must not be truncated by `TakeLast(260)`.
- MACD warm-up may exist, but MACD must not be reduced to only the afternoon or later half of the session.
- `QUOTE_CLOSE_DISPLAY` may participate as the final display-side MACD point, but must not write `chart_intraday_cache`.
- ETF intraday MACD keeps its existing ETF behavior.
- Current `251.NDXTMC` EastMoney `trends2` payload has volume fields but all values are zero; this is treated as no real source volume and should display `成交量数据不可用`.
- Quote and price changes must not be used to generate index volume.
- `100.NDX100` real non-zero source volume may display normally.
- `100.NDX100` volume must not be used as `251.NDXTMC` volume.

Forbidden without explicit user confirmation:
- Do not change `IndexIntradayCacheCompletenessService` completeness rules.
- Do not change index intraday US Eastern trading-date ownership.
- Do not split a US index session at Beijing `00:00`.
- Do not change `QUOTE_CLOSE_DISPLAY` close-point display rules.
- Do not truncate index intraday MACD to the ETF display limit.
- Do not treat all-zero `251.NDXTMC` source volume as drawable volume.
- Do not create intraday history or volume from quote.
- Do not persist display-only quote points into `chart_intraday_cache`.

Protection tests:
- `SecurityChartServiceTests`: partial non-trading-hours index cache triggers catch-up; complete cache does not catch up; catch-up failure keeps cache; catch-up goes through `GlobalMarketRequestScheduler`.
- `SecurityChartServiceTests`: quote remains independent and does not connect partial index cache; complete cache can use `QUOTE_CLOSE_DISPLAY`; intraday quote remains independent.
- `SecurityChartServiceTests`: index intraday merges Beijing-midnight split points by US Eastern trading date; morning points are preserved; `QUOTE_CLOSE_DISPLAY` does not drop morning points.
- `SecurityChartServiceTests`: `251.NDXTMC` and `100.NDX100` index MACD keep the full US session sequence; all-zero `251.NDXTMC` source volume remains unavailable; real non-zero `100.NDX100` source volume remains drawable.
- `ChartDataSourceRoutingTests`: ETF intraday remains Tencent; index intraday remains EastMoney.
- `LockedModulesDocumentationTests`: this lock section and its forbidden side effects are asserted in documentation tests.

### 1.17 Left navigation dialog interaction locks

Locked task:
- `TASK-UI-INTERACTION-039`

Accepted behavior:
- Left navigation dialogs must use a dark initial window background.
- Dialog root containers must also use a dark background and must not fall back to the default white WPF background.
- Dialog owners must be set to the main window.
- Dialog `WindowStartupLocation` must be `CenterOwner`.
- Dialog `ShowInTaskbar` must be `false`.
- Dialog opening may use only a lightweight fade-in animation.
- Exaggerated animation, blocking sleeps, artificial loading delays, and `Thread.Sleep` are forbidden.
- The interaction polish must not change save, query, replay, market-data, alert, order-draft, TradeLog, or strategy behavior.

Covered entries:
- `溢价决策` -> `ManualDataEntryWindow`
- `交易日志` -> `ManualDataEntryWindow`
- `系统设置` -> `ManualDataEntryWindow`
- `风险中心` -> `RiskCenterWindow`

Protection tests:
- `DialogInteractionEffectsTests`: left navigation dialogs use owner/center-owner/taskbar rules, dark initial backgrounds, and lightweight smooth-open fade.

Forbidden without explicit user confirmation:
- Do not reintroduce white/default dialog roots.
- Do not remove owner/center-owner behavior for left navigation dialogs.
- Do not add heavy animation or blocking sleeps.
- Do not change any business logic while adjusting dialog interaction behavior.

### 1.18 LOCK-CHART-POST-CLOSE-INTRADAY-REFRESH-001：收盘后 ETF 真实分时补齐与 quote 独立显示锁定

Locked commit:
- `072bb0d418aea3e3cc61a37916d57b4f3888f3c3`
- `Fix post-close intraday refresh for ETF charts`

Accepted behavior:
- After 15:00, reopening the app or opening an ETF `SecurityChartWindow` must attempt a real intraday catch-up when the current same-day real intraday cache is missing or incomplete.
- ETF intraday catch-up must use the real ETF intraday source, currently Tencent `minute/query`.
- If the same-day `chart_intraday_cache` latest real intraday point is earlier than `14:57` and the current time is after A-share close, the chart path must schedule one low-frequency real intraday catch-up check.
- Catch-up must go through the existing `GlobalMarketRequestScheduler` and the existing per-symbol cooldown/circuit-breaker path.
- Bypassing global rate limits is forbidden.
- High-frequency repeated intraday requests are forbidden.
- When catch-up succeeds, the ETF main intraday chart must render the real intraday curve through the close area using real source points.
- Volume bars may use only real intraday volume fields.
- Fake volume is forbidden.
- Fake middle-minute prices are forbidden.
- Interpolating from `quote price` to generate a fake `14:20-15:00` path is forbidden.
- Connecting a close quote directly to the latest real intraday point as a diagonal line is forbidden.
- `QUOTE_CLOSE_DISPLAY` may be used only as a degraded display marker, dot, or label.
- `QUOTE_CLOSE_DISPLAY` must not be written to `chart_intraday_cache`.
- `QUOTE_CLOSE_DISPLAY` must not participate in the continuous ETF main intraday polyline.
- `QUOTE_CLOSE_DISPLAY` must not participate in ETF intraday MACD calculation.
- If real intraday refresh fails or still returns incomplete data, the only allowed degradation is cache plus an independent quote marker.
- In degraded display, the chart must not connect, interpolate, fill middle minutes, or create volume.
- `251.NDXTMC` must continue to follow the no-fake-volume rule.
- Existing index chart locks remain unchanged.
- This lock must not affect TradeLog, order drafts, account replay, holdings replay, or strategy decisions.
- This lock must not add a main-window manual refresh button.

Covered symbols:
- ETFs: `159509`, `159660`, `159941`, `513100`, `513300`, `159501`, `159513`, `159659`.
- Indexes: `251.NDXTMC` keeps the existing index route and no-fake-volume behavior; `100.NDX100` keeps the existing index route and uses only real source volume.
- Other: `311513` does not enter the ETF intraday catch-up path, does not fabricate intraday points, and does not fabricate volume.

Current test baseline:
- `806/806`

Protection tests:
- ETF cache whose latest real same-day point is earlier than `14:57` triggers a post-15:00 real intraday catch-up check.
- Successful real intraday catch-up makes the main chart use the real intraday curve.
- Failed real intraday catch-up degrades to cache plus an independent quote marker.
- `QUOTE_CLOSE_DISPLAY` does not connect to the main polyline.
- `QUOTE_CLOSE_DISPLAY` does not participate in ETF MACD.
- `QUOTE_CLOSE_DISPLAY` does not generate volume bars.
- `251.NDXTMC` still does not fabricate volume.
- TradeLog is not written.
- `order_draft_state` is not changed.
- No main-window manual refresh button is added.

### 1.19 LOCK-INDEX-US-OPEN-FIRST-QUOTE-001：美股开盘指数数据首刷锁定

Accepted behavior:
- During the US market open window, the app must be able to fetch index quote data immediately through the accepted index quote lane.
- Manual acceptance confirmed that after the US market opened at 21:30 Beijing time, index data refreshed promptly.
- This lock belongs to the index quote lane and global market request scheduling behavior.
- Covered indexes are `251.NDXTMC` and `100.NDX100`.
- Index quote data must come from a real market source, currently the EastMoney index quote source.
- Simulated index data is forbidden.
- Fake fallback data is forbidden.
- Fabricating index price, change percent, or volume for display movement is forbidden.
- `251.NDXTMC` must continue to follow the strict no-fake-volume rule.
- The US-open first quote refresh must use the existing global scheduler and anti-IP-ban throttling.
- Bypassing `GlobalMarketRequestScheduler` is forbidden.
- High-frequency repeated requests are forbidden.
- Depending on the system proxy is forbidden.
- After a successful real fetch, `market_source_status` should update normally.
- If the real source fails, status or runtime logging must record the failure; the app must not silently pretend success.
- Index first quote refresh must not write TradeLog.
- Index first quote refresh must not generate order fills.
- Index first quote refresh must not modify account replay, holdings replay, or strategy fact sources.
- Index first quote refresh must not add a main-window manual refresh button.
- Index first quote refresh must remain compatible with Beijing-date cross-day scenarios.
- Future daylight-saving-time or standard-time handling must follow US trading-session or existing trading-calendar rules. Do not hardcode Beijing `21:30` as the only year-round open time; the current accepted manual verification is the daylight-saving Beijing `21:30` open.

Current test baseline:
- `806/806`

Protection notes:
- This is a documentation lock. It should not add tests and should not change the test count.
- Existing scheduler tests protect the index quote lane separation, US-open stale-throttle release, failure cooldown retention, and low-frequency intraday/history lanes.

## 2. 后续任务解锁流程

后续任何 Codex 任务如果需要修改锁定模块，必须先输出：

1. 要修改哪一个锁定模块。
2. 为什么必须修改。
3. 不修改会造成什么问题。
4. 影响哪些文件。
5. 影响哪些测试。
6. 是否会影响 `TradeLog` / `order_draft_state` / `alert_log` / `runtime_log` / `market_history_cache`。
7. 是否会改变数据源请求频率。
8. 是否会绕过 `GlobalMarketRequestScheduler`。
9. 修改范围。
10. 等用户确认后才能继续。

## 3. 违反锁定的风险

- 破坏 VBA 对齐口径。
- 造成 TradeLog 事实源污染。
- 误下单或误把草案当成交。
- 触发真实预警刷屏。
- 增加封 IP 风险。
- 破坏缓存优先和图表首屏体验。
- 造成历史 K 线缓存退化。

## 4. 保护测试清单

当前保护测试至少覆盖：

- TradeLog 不被行情/图表/策略刷新自动写入。
- OrderDraft 定稿不代表成交。
- T档全局已执行金额口径。
- ETF 使用腾讯路由。
- 指数使用东方财富路由。
- 顶部卡片不显示过期/缓存/未连接等状态词。
- 分时首屏缓存不等待 live。
- 指数 quote 尾点不连接主线。
- DailyLike 优先，MonthlyLike 不冒充日K。
- 日K/周K/月K显示层使用 `QUOTE_INTRADAY_BAR` 临时K线且不写缓存。
- quote 缺 OHLC 不造K。
- 周K/月K包含显示用临时日K且不联网。
- `GlobalMarketRequestScheduler` 限频。
- TencentProbe 默认 dry-run。
- Smoke mode 不写 alert_log。
- 本文档存在并包含核心锁定项。

关键测试名称：

- `SecurityChartWindow_DoesNotWriteTradeLogOrAlertLog`
- `RepositoryFinalizationPersistsDraftAndDoesNotWriteTradeLog`
- `CumulativeTierRemainSubtractsGlobalExecutedGridAmountAcrossSymbols`
- `ChartDataSourceRoutingTests.EtfIntraday_UsesTencentMinuteQuery`
- `ChartDataSourceRoutingTests.EtfDailyWithoutDailyLike_UsesTencentQfqDaily`
- `ChartDataSourceRoutingTests.IndexIntraday_StillUsesEastMoney`
- `ChartDataSourceRoutingTests.IndexDaily_StillUsesEastMoneyHistory`
- `ChartDataSourceRoutingTests.WeeklyAndMonthly_DoNotRequestNetworkAndUseDailyLikeHistory`
- `ChartDataSourceRoutingTests.BackgroundEtfIntraday_RequestsAtMostOneSymbolPerRefresh`
- `ChartDataSourceRoutingTests.ActiveChartSymbol_IsNotRequestedAgainByBackgroundIntradayRefresh`
- `BuildSnapshot_FallsBackToSqliteDailyLikeWithExplicitDailyCacheStatus`
- `BuildSnapshot_EtfDailyAppendsDisplayOnlyQuoteBarWhenDailyLikeIsOlder`
- `BuildSnapshot_EtfWeeklyAndMonthlyIncludeDisplayOnlyQuoteBar`
- `BuildSnapshot_DisplayOnlyQuoteBarCoversConfiguredEtfsAndIndexes`
- `BuildSnapshot_DailyDoesNotCreateTemporaryBarWhenQuoteOhlcMissing`
- `BuildSnapshot_IndexDailyDoesNotCreateTemporaryBarWhenQuoteOhlcMissing`
- `BuildSnapshot_ShowsIntradayCacheWithQuoteTailDuringCircuit`
- `ChartCache_DoesNotReplaceDailyLikeWithFailedKLineStatus`
- `NonQuoteRequests_AreLimitedToOnePerGlobalTick`
- `TencentProbe_DefaultPathIsDryRun`
- `TencentProbe_LiveBatchRequiresExplicitFlags`
- `LockedModulesDocument_DefinesLiveKLineDisplayOnlyContract`

## 5. 最近一次验证结果

最近验证命令：

```powershell
dotnet restore .\CrossETF.Terminal.UiShell.Reference.sln
dotnet build .\CrossETF.Terminal.UiShell.Reference.sln -c Debug
dotnet test .\CrossETF.Terminal.UiShell.Reference.sln -c Debug --no-build
```

最近验证日期：2026-06-29。

最近验证结论：`restore`、`build`、`test` 通过；`CROSSETF_SMOKE_MODE=1` 启动验证通过。

说明：后续任务如修改锁定项，必须重新执行上述命令，并报告 `TradeLog`、`order_draft_state`、`alert_log`、`runtime_log`、`market_history_cache` 前后计数。
