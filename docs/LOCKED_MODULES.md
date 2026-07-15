# 已收口模块锁定清单

本文档用于约束后续 Codex 任务：任何新修复开始前，都应先阅读本文件。除非用户明确授权，后续任务不得修改本文档列出的已收口行为。

最近锁定任务：`TASK-TRADELOG-UI-018`

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

### 1.20 LOCK-PNL-BEIJING-NATURAL-DAY-001：今日/当日盈亏北京时间自然日口径锁定

Locked commit:
- `b28ec48347b9972b0e298407cbe450974d59ea47`
- `Fix daily PnL Beijing natural day basis`

Superseding clarification:
- This lock keeps the Beijing natural-day boundary, but its PnL inclusion rules are corrected and supplemented by `LOCK-PNL-VALUATION-DATE-FILTER-001` and `LOCK-PNL-NATURAL-DAY-EVENT-FILTER-001`.
- If this lock conflicts with `LOCK-PNL-NATURAL-DAY-EVENT-FILTER-001` about which PnL items are eligible, the newer event-filter lock is authoritative.
- It must not be interpreted as excluding all OTC or fund daily PnL.
- It must not forbid broker-compatible exchange ETF daily PnL display such as `(current real price - previous close) * current holding quantity`.
- It must not force ETF table `当日盈亏` to display `--` only because no single-symbol `00:00` baseline exists.
- It must not exclude a fund/NAV daily PnL update whose real valuation update time is inside today's Beijing natural-day interval.
- Yesterday's OTC/NAV update is not counted today; today's OTC/NAV update is counted today; tomorrow must not repeat today's update.

Accepted behavior:
- `今日盈亏` and `当日盈亏` must use the Beijing natural-day accounting boundary.
- The unified interval is the half-open Beijing-time range `[today 00:00:00, tomorrow 00:00:00)`.
- Today `00:00:00` is included in today.
- Today `23:59:59` is included in today.
- Tomorrow `00:00:00` is not included in today.
- Yesterday `23:59:59` is not included in today.
- A-share trading-day boundaries must not replace this natural-day basis.
- US trading-day boundaries must not replace this natural-day basis.
- Market-source `当日涨跌`, `change_value`, `change_percent`, or equivalent quote-day fields must not directly replace account or holding daily PnL.
- `current price - previous close` must not directly replace account or holding daily PnL.
- The top-card `今日盈亏` must be calculated from account replay snapshots inside the Beijing natural-day range.
- If the calculation falls back to `total_assets`, same-day deposits and withdrawals must be excluded so cash flow is not treated as investment PnL.
- Deposits and withdrawals must not be counted as daily investment profit or loss.
- Buys, sells, fees, dividends, and other same-day items continue to follow the existing account replay rules.
- When no `00:00` snapshot exists, the first real snapshot inside the Beijing natural-day interval may be used as the safe degraded baseline.
- If the day has no usable snapshot, the app must not fabricate daily PnL.
- Position-level `DailyPnl` should remain empty or safe when no natural-day holding baseline exists; it must not be miscalculated from quote-day movement.
- US-market data after Beijing `21:30` belongs to the same Beijing natural day.
- US-market data after Beijing `00:00` belongs to the new Beijing natural day, not the previous US trading session.
- A-share post-`15:00` close quote or intraday catch-up still belongs to the same Beijing natural day before midnight.
- TradeLog remains the accounting fact source.
- This lock must not write TradeLog automatically.
- This lock must not change order-draft execution boundaries.
- This lock must not change strategy buy/sell rules.
- This lock must not add a main-window manual refresh button.

Implementation notes:
- `BeijingNaturalDayRangeProvider` provides the shared natural-day range.
- `StartInclusive = 今日北京时间 00:00:00`.
- `EndExclusive = 明日北京时间 00:00:00`.
- Code must use `< 明日 00:00:00`; do not use `<= 23:59:59` as a database or timestamp upper bound.
- The top-card `今日盈亏` no longer prioritizes `position_replay_state.daily_pnl` aggregation.
- Quote-derived `daily_pnl` from `current price - previous close` is a quote trading-day basis and must not replace account `今日盈亏`.
- Account/holding daily PnL should follow Beijing natural-day replay changes, not market-source quote-day movement.

Current test baseline:
- `812/812`

Protection tests:
- `00:00:00` is included in the current Beijing natural day.
- Next-day `00:00:00` is excluded from the previous Beijing natural day.
- `23:59:59` is included in the current Beijing natural day.
- Yesterday `23:59:59` is excluded from the current Beijing natural day.
- US-market `21:30` data is counted in the current Beijing day.
- US-market cross-`00:00` data is counted in the new Beijing day.
- A-share post-`15:00` quote/catch-up remains in the current Beijing day.
- Deposits and withdrawals do not amplify daily PnL.
- Quote last close, quote change value, and quote change percent are not used as account/position natural-day PnL.
- TradeLog is not written.
- `order_draft_state` is not changed.
- No main-window manual refresh button is added.

### 1.21 LOCK-PNL-VALUATION-DATE-FILTER-001：今日/当日盈亏按估值更新时间自然日过滤锁定

Locked commit:
- `6ad6705621f48739849302b47a2fea3cbac3f137`
- `Fix daily PnL valuation date filtering`

Superseding clarification:
- This lock is corrected and supplemented by `LOCK-PNL-NATURAL-DAY-EVENT-FILTER-001`.
- Do not interpret this lock as saying `received_at` alone is sufficient, `quote_time == today` alone is sufficient, or a fixed `20:00` rule is the business definition.
- The authoritative rule is whether the PnL event belongs to the target Beijing natural day `[D 00:00:00, D+1 00:00:00)`.

Accepted behavior:
- `今日盈亏` and `当日盈亏` remain based on the Beijing natural-day interval.
- The interval is the half-open Beijing-time range `[today 00:00:00, tomorrow 00:00:00)`.
- Today `00:00:00` is included.
- Today `23:59:59` is included.
- Tomorrow `00:00:00` is excluded.
- Yesterday `23:59:59` is excluded.
- A holding row `daily_pnl` may be included only when the matched quote/NAV valuation update time is inside today's Beijing natural-day interval.
- OTC and fund daily PnL belongs to account `今日盈亏` / `当日盈亏`; do not exclude it by asset type.
- Yesterday `20:00` OTC/NAV updates must not be counted in today's PnL.
- Today `20:00` OTC/NAV updates must be counted in today's PnL.
- Tomorrow must not repeat today's `20:00` OTC/NAV update.
- Exchange ETF `daily_pnl` is counted after today's real quote update.
- Exchange ETF `daily_pnl` may keep the broker-compatible display basis `(current real price - previous close) * current holding quantity`.
- The ETF decision table `当日盈亏` must not show `--` for all rows only because there is no single-symbol `00:00` snapshot baseline.
- Do not revert to excluding all OTC/fund PnL.
- Do not revert to hiding exchange ETF daily PnL when no single-symbol `00:00` baseline exists.
- Do not use `calculated_at` to treat an old NAV valuation as today's update.
- SINA_FUND valuation matching must prefer `market_quote_cache.received_at`.
- `quote_time` is only a fallback when `received_at` is unavailable.
- If no reliable valuation update time exists, the daily PnL value must not be included.
- Duplicate records for the same `strategy_code|actual_code` across `position_replay_state` and `otc_position_replay_state` must be de-duplicated.
- This lock must not change TradeLog, order drafts, strategy buy/sell rules, Sina fund source, OTCMap, or OtcPositionReplayState core replay behavior.
- This lock must not add a main-window manual refresh button.

Implementation notes:
- The top-card `今日盈亏` aggregates eligible `position_replay_state` and `otc_position_replay_state` daily PnL values.
- Eligibility is decided by matched quote/NAV update time, with `received_at` first and `quote_time` fallback.
- The ETF decision table `当日盈亏` aggregates the current strategy row's exchange ETF daily PnL plus eligible off-exchange substitute daily PnL.
- Off-exchange substitute matching uses the actual fund code to match SINA_FUND quote/NAV records.
- `calculated_at` is not used as the valuation-day inclusion timestamp.
- `AccountReplayService.cs`, the Sina fund source, OTCMap, OtcPositionReplayState, TradeLog, order drafts, and strategy buy/sell logic remain unchanged by this lock.

Current test baseline:
- `822/822`

Protection tests:
- ETF quote `received_at` inside today includes ETF `daily_pnl` in top-card and table PnL.
- OTC/SINA_FUND update from yesterday `20:00` is excluded from today's PnL.
- OTC/SINA_FUND update from today `20:00` is included in top-card and row PnL.
- Tomorrow does not repeat today's OTC/SINA_FUND update.
- Off-exchange positions match SINA_FUND quote/NAV by actual fund code.
- SINA_FUND matching prefers `received_at` before `quote_time`.
- `calculated_at` does not make an old NAV valuation count as today.
- Missing valuation update time excludes that daily PnL value.
- TradeLog is not written.
- `order_draft_state` is not changed.
- OTCMap, OtcPositionReplayState, Sina fund source, and strategy buy/sell rules are not changed.
- No main-window manual refresh button is added.

### 1.22 LOCK-PNL-NATURAL-DAY-EVENT-FILTER-001：今日/当日盈亏任意自然日事件过滤锁定

Locked commit:
- `e910e6cd0ae1215461df32c745d3a0b9cf95b57f`
- `Fix daily PnL natural day event filtering`

Conflict handling:
- This lock corrects and supplements `LOCK-PNL-BEIJING-NATURAL-DAY-001` and `LOCK-PNL-VALUATION-DATE-FILTER-001`.
- The old locks remain valid for the Beijing natural-day boundary, half-open interval, no TradeLog write, no order-draft side effect, no strategy-rule change, and no main-window manual refresh button.
- If an older lock implies OTC/fund PnL is excluded by asset type, `received_at` alone is enough, `quote_time == today` alone is enough, no single-symbol `00:00` baseline means all ETF table rows must show `--`, today's NAV update should be excluded, yesterday's NAV update may count today, or today's NAV update may repeat tomorrow, this lock supersedes that interpretation.

Accepted behavior:
- Daily PnL / today PnL is filtered by Beijing natural-day PnL events for any natural day `D`.
- The interval is `[D 00:00:00, D+1 00:00:00)`.
- `D 00:00:00` is included in day `D`.
- `D 23:59:59` is included in day `D`.
- `D+1 00:00:00` is excluded from day `D`.
- `D-1 23:59:59` is excluded from day `D`.
- Previous-day PnL events must not be counted in today.
- Today's PnL events must be counted today.
- Tomorrow must not repeat PnL events that already happened today.
- The same rule applies to every date; do not add Monday, Friday, holiday, July 3, or other date-specific branches.
- `20:00` is not a business rule. It may appear only as a data-time example for evening NAV publication.
- OTC/fund/NAV PnL is account PnL and must be eligible when the new NAV/PnL event happens inside the current natural day.
- OTC/fund/NAV PnL from yesterday is not counted today.
- OTC/fund/NAV PnL from today is not repeated tomorrow.
- Exchange ETF daily PnL remains broker-compatible for table display and may use `(current real price - previous close) * current holding quantity` from real quote data.
- Exchange ETF daily PnL must still be filtered so a stale quote from day `D` is not counted again on `D+1`.
- The top-card today PnL and the ETF decision table daily PnL must use the same eligible-item path in `EtfDecisionTableMetrics`.
- A row that is ineligible and displays `--` in the table must not be counted by the top-card through a looser path.
- The top-card must not fall back to an older `AccountTrendMetrics` snapshot-difference path that produces a different eligible-item set.
- Do not use A-share trading-day or US trading-day boundaries to replace the Beijing natural-day event filter.
- Do not use market-source change percent, change value, or `current price - previous close` as a replacement for account-level PnL.

SINA_FUND / NAV event rules:
- SINA_FUND and other NAV records must identify whether the NAV/PnL event is newly effective in the target natural day.
- Do not decide eligibility from `received_at` alone.
- Do not decide eligibility from `quote_time == today` alone.
- Do not count an old NAV record merely because it was received again today.
- Do not count a today-received batch if the actual NAV event belongs to an older date.
- Do not repeat yesterday evening's NAV event in today's PnL.
- Do not repeat today's NAV event in tomorrow's PnL.
- If the system receives a newly published NAV batch on day `D` and the PnL event belongs to day `D`, it is eligible for day `D`.
- If the event time or event ownership cannot be determined reliably, skip that PnL item instead of fabricating eligibility.

Implementation boundaries:
- Keep the calculation in the decision-table / display metrics path; do not modify TradeLog facts.
- Do not write TradeLog automatically.
- Do not modify order-draft execution boundaries.
- Do not modify strategy buy/sell rules.
- Do not modify `AccountReplayService.cs`.
- Do not modify the Sina fund source.
- Do not modify OTCMap.
- Do not modify OtcPositionReplayState core replay behavior.
- Do not modify XAML or white-flash UI files for this lock.
- Do not add a main-window manual refresh button.

Current test baseline:
- `843/843`

Protection tests:
- Generic natural day `D` includes events from `D 00:00:00` through before `D+1 00:00:00`.
- `D-1 23:59:59` is excluded from day `D`.
- `D+1 00:00:00` is excluded from day `D`.
- A normal workday morning does not count the previous evening's NAV event.
- A normal workday evening counts the same-day newly effective NAV event.
- The next morning does not repeat the previous evening's NAV event.
- Monday morning does not count the previous Friday evening's NAV event.
- Monday evening counts the Monday newly effective NAV event.
- Tuesday morning does not repeat the Monday evening NAV event.
- Exchange ETF quote/PnL for day `D` is counted in day `D` and is not repeated on `D+1`.
- Top-card today PnL and ETF table daily PnL use the same eligible-item path.
- Table `--` rows are not counted by the top-card.
- Effective rows are counted; ineffective rows are skipped.
- TradeLog is not written.
- `order_draft_state` is not changed.
- No main-window manual refresh button is added.

### 1.23 TASK-DIAG-006：风险中心运行诊断模块

锁定基线：

- 版本：`v8.1.1`
- 主功能提交：`82562e1`（`Add risk center runtime diagnostics`）
- 历史孤立行情缓存补丁提交：`666d351`（`Ignore orphan quote cache in diagnostics health`）
- 测试基线：`893/893`

锁定行为：

1. 运行诊断只能嵌入现有 `RiskCenterWindow` 风险中心。
2. 不得新增左侧一级诊断模块。
3. 不得新增独立 `MarketDiagnosticsWindow` 顶层窗口。
4. 必须保留“诊断总览、行情与缓存、今日盈亏审计、运行日志、程序环境”五个诊断页签。
5. “重新读取本地状态”只能读取 SQLite、进程及程序集信息，并重新组装只读诊断快照。
6. 运行诊断不得联网、不得触发行情刷新、不得写数据库、不得写 TradeLog。
7. 今日盈亏审计不得复制第二套今日/当日盈亏规则。
8. 诊断有效项合计必须与主界面今日盈亏业务入口保持一致。
9. 当前行情健康列表只能展示和统计当前活动行情。
10. 活动行情集合必须按以下通用来源构建：
    - 启用策略 ETF；
    - 启用策略的 `index_sec_id`；
    - 属于启用策略且已启用的 OTC 通道；
    - `MarketSymbolNormalizer.DefaultTopBarItems()` 固定行情；
    - `position_state` 中仍需估值的场内 ETF 实际代码。
11. 已删除或停用配置的历史孤立缓存：
    - 可以继续保留在 SQLite；
    - 不进入当前行情健康列表；
    - 不计入 `StaleQuoteCount`；
    - 不影响 `OverallStatus`；
    - 不触发行情请求；
    - 不得通过具体证券代码硬编码排除。
12. 当前活动行情自身过期时仍必须显示过期状态并正常产生警告。
13. 不得自动删除或清空 `market_quote_cache`。
14. 不得修改行情路由、parser、router 或 scheduler 来掩盖诊断展示问题。
15. 风险中心公共关闭按钮必须在风险概览和运行诊断中始终可见；“重新读取本地状态”“刷新日志”“清空日志”三个操作按钮的深色完整视觉状态保持锁定。
16. 本模块不得影响 TradeLog、账户回放、策略、委托、`ManualDataEntryWindow`、白闪或标题栏逻辑，也不得新增主界面手动刷新按钮。

只读边界：

- 历史孤立行情缓存只在诊断视图和健康统计中排除，数据库记录继续保留。
- `MarketDiagnosticsSnapshotService` 不得调用 save、write、delete、行情 refresh、live probe 或网络客户端。
- 当前数据源异常、数据库读取失败、今日盈亏审计不一致和活动行情过期仍必须如实显示。
- 历史 `runtime_log` 可以保留展示，但旧错误不得永久污染当前整体状态。

### 1.24 TASK-CHART-ANALYSIS-008：K线专业分析增强

锁定基线：

- 版本：`v8.2.0`
- 功能提交：`986056024d1df5fd89b356d87a9e3e7394e6a70e`
- 测试基线：`960/960`
- 包含补丁：`TASK-CHART-ANALYSIS-008-PATCH`、`TASK-CHART-ANALYSIS-008-PATCH-CANCEL`

#### A. Viewport

1. 缩放只适用于日K、周K、月K。
2. 分时图不使用本轮 viewport。
3. 日K默认显示最近 120 根。
4. 周K默认显示最近 104 根。
5. 月K默认显示最近 60 根。
6. 最少可见 20 根；数据不足时显示全部真实数据。
7. 鼠标滚轮以鼠标所在K线为锚点。
8. 横向拖动按K线索引移动。
9. 主图、成交量、MACD 共用同一 viewport。
10. 每个周期的 viewport 状态相互独立，且只保存在窗口内存中。
11. 查看历史时，后台刷新不得强制跳回最新位置。
12. 位于最新边缘时，新K线可以继续跟随。
13. 复位只影响当前周期。

#### B. 均线

1. MA5、MA10、MA20、MA60 均为简单移动平均线（SMA）。
2. 均线使用完整周期K线的 Close 计算。
3. 必须先计算完整序列，再裁剪到可视区。
4. 数据不足时不绘制，不得以 0 填充。
5. `QUOTE_INTRADAY_BAR` 可以参与显示端计算。
6. `QUOTE_INTRADAY_BAR` 不得持久化。
7. 均线只用于显示，不写数据库。

#### C. 十字光标

1. 十字光标只适用于日K、周K、月K。
2. 十字光标吸附当前可见K线。
3. 主图和副图使用相同索引。
4. 鼠标移动不访问数据库。
5. 鼠标移动不触发网络请求。

#### D. B/S

1. B 只来自 TradeLog 真实买入。
2. S 只来自 TradeLog 真实卖出。
3. 图表只显示字母 B 和 S。
4. 不显示箭头、数量、金额、圆圈或 Tooltip。
5. ETF 优先按 `ActualCode` 匹配。
6. 旧场内记录仅在 `ActualCode` 为空且不是场外替代时，才按 `StrategyCode` 回退匹配。
7. 场外替代不得画到 ETF 价格轴。
8. 指数使用 `index_sec_id` 关系显示交易日期事件。
9. ETF 成交价不得映射到指数 Y 轴。
10. 同周期相同动作去重。
11. 不显示策略建议。
12. 不把委托草案或定稿当成交。
13. 图表不得写入、修改或删除 TradeLog。

#### E. 真实历史深度

1. ETF 继续使用腾讯 qfq `DailyLike` 真实日K。
2. 指数继续使用东方财富 `DailyLike` 真实日K。
3. 周K和月K继续由日K在本地聚合。
4. 不新增周K或月K接口。
5. 腾讯 ETF 深历史可以使用同一日K接口串行分页。
6. 分页必须经过现有 `GlobalMarketRequestScheduler`。
7. 分页不得并发。
8. 分页必须可以取消。
9. 页面重叠和复权一致性必须验证。
10. 无重叠、复权冲突、无效 OHLC 或解析失败时，不得采用结果。
11. 半成品不得写入缓存。
12. 短缓存不得覆盖更长的有效 `DailyLike` 缓存。
13. `MonthlyLike`、`Sparse`、`Invalid` 不得替代 `DailyLike`。
14. 新上市标的只显示上市以来的真实数据。
15. 不生成上市前历史。
16. 不拼接其它证券或指数数据。
17. 数据源不足 60 个月时，月 MA60 保持不可用。
18. 深历史失败时继续保留原缓存。
19. 不清空原缓存。
20. 来源穷尽只能在真实成功确认后记录。

#### F. 窗口生命周期取消

1. 每个图表窗口拥有独立的 linked `CancellationTokenSource`。
2. 窗口 Token 与应用生命周期 Token 联动。
3. 关闭窗口必须取消对应深历史请求。
4. 关闭一个窗口不得影响其它窗口。
5. 主程序退出必须取消全部图表任务。
6. 页面间 Delay、scheduler 等待、HTTP、解析和合并均使用窗口 Token。
7. 最终保存缓存、写检查点和发布 Snapshot 前必须检查取消。
8. 取消不得写半成品缓存。
9. 取消不得写来源穷尽检查点。
10. 取消不得写成功检查点。
11. 取消不得调用 breaker 失败记录。
12. 取消不得调用 scheduler 失败记录。
13. 取消不得记录成功。
14. 取消不得解除 host 或 endpoint 防封冷却。
15. 取消后必须清理 `InProgress` 状态。
16. 取消后重新打开同一证券时允许再次尝试。
17. 已关闭窗口不得继续收到 Snapshot。

#### G. 不得影响

- TradeLog 事实源。
- `AccountReplayService`。
- `StrategyDecisionService`。
- `OrderDraftService`。
- `MarketDiagnosticsSnapshotService`。
- `ManualDataEntryWindow`。
- 主界面随机 2-4 秒刷新。
- 主界面无手动刷新按钮。
- 现有行情路由和防封逻辑。
- 白闪、标题栏和 `WindowChrome`。

### 1.25 TASK-CHART-WINDOW-MAXIMIZE-009：走势图窗口最大化与还原

锁定基线：

- 版本：`v8.2.1`
- 窗口功能提交：`4d592eaae75b8145b6165e8cc53c04984f4060b7`
- 测试基线：`971/971`

锁定行为：

1. `SecurityChartWindow` 标题栏按钮顺序固定为“最小化、最大化/还原、关闭”。
2. 普通窗口状态显示最大化图标和“最大化”提示。
3. 最大化窗口状态显示还原图标和“还原”提示。
4. 最大化和还原必须使用 WPF `SystemCommands` 切换，不手工写入窗口尺寸或屏幕坐标。
5. 双击标题栏必须在普通状态和最大化状态之间切换；双击不得继续执行 `DragMove`。
6. 最大化必须使用当前显示器可用工作区，不得覆盖当前屏幕任务栏。
7. 最大化和还原不得重置日K、周K或月K viewport、缩放比例或历史位置。
8. 最大化和还原不得改变当前周期、MA 开关、B/S 标记或成交量/MACD 副图选择。
9. 最大化和还原不得改变窗口 `LifetimeToken`，不得取消、重启或重新订阅深历史请求。
10. `WindowStyle=None`、`ResizeMode=CanResize` 和既有 `WindowChrome` 配置保持不变。
11. 本任务不得修改其它窗口及其标题栏行为。

### 1.26 TASK-RELEASE-PACKAGING-010：发布文件中文命名与桌面快捷方式

锁定基线：

- 版本：`v8.2.1`
- 窗口功能提交：`4d592eaae75b8145b6165e8cc53c04984f4060b7`
- 发布规范提交：`c917517fe610632a17f84c7f0b9afe5700e96eb3`
- 测试基线：`971/971`

锁定行为：

1. `V8.2.1` 及后续正式版本的用户启动文件统一命名为 `跨境ETF.exe`。
2. 不得为中文 EXE 名称改变程序集 `AssemblyName` 或程序集身份。
3. 必须由 `scripts/Publish-CrossEtfRelease.ps1` 在 `dotnet publish` 成功后重命名 AppHost EXE。
4. 正式发布目录不得保留 `CrossETF.Terminal.UiShell.Reference.exe`。
5. 当前用户桌面快捷方式统一命名为 `跨境ETF.lnk`。
6. 快捷方式必须指向当前最新正式版本的 `跨境ETF.exe`。
7. 快捷方式 `WorkingDirectory` 必须为对应正式发布目录。
8. 快捷方式图标必须使用对应正式 `跨境ETF.exe,0`。
9. 正式发布包必须来自最终版本标签对应的 detached worktree。
10. `ProductVersion/InformationalVersion` 必须包含最终锁定提交完整哈希。
11. 更新快捷方式前必须真实启动正式 EXE 至少 8 秒，并通过正常关闭验证。
12. 必须先创建和验证临时快捷方式，再替换正式快捷方式；失败时不得破坏原有可用快捷方式。
13. `artifacts` 和桌面 `.lnk` 不得提交到 Git。
14. `v8.2.0` 及更早发布目录保持不变，不追溯改名。
15. 后续正式版本必须继续使用统一发布脚本。

### 1.27 TASK-DATA-BACKUP-RESTORE-011：本地数据库安全备份与恢复

锁定基线：

- 版本：`v8.3.0`
- 功能提交：`ef64e3033880aea36762badeba1d4cd32770570c`
- 测试基线：`1047/1047`

#### A. 路径与程序集

1. 正式数据库路径固定为 `%LocalAppData%\CrossETF.Terminal.UiShell.Reference\cross_etf_terminal.db`。
2. 备份目录固定为 `%LocalAppData%\CrossETF.Terminal.UiShell.Reference\backups`。
3. 恢复暂存目录固定为 `%LocalAppData%\CrossETF.Terminal.UiShell.Reference\restore`。
4. 中文 EXE 名称不得改变程序集 `AssemblyName` 或程序集身份。
5. 不迁移数据库目录，不改变正式数据库文件名。
6. 不改变 `LocalDatabase` 的 `Pooling=false` 策略。

#### B. 备份方式

1. 活动 SQLite 数据库必须使用 `SqliteConnection.BackupDatabase`。
2. 不得直接复制正在使用的数据库主文件制作备份。
3. 临时备份只有校验成功后才能原子改名为正式 `.db`。
4. 每份备份必须使用只读连接执行完整 `PRAGMA integrity_check`。
5. `integrity_check` 结果必须严格为单行 `ok`。
6. 备份必须包含 `strategy_config`、`trade_log`、`app_settings` 基础表。
7. 无效备份不得进入可恢复状态。
8. 备份不得修改活动数据库、WAL 模式或用户表。
9. 不得删除活动数据库的 WAL/SHM 来制作备份。

#### C. 备份类型

1. 备份类型只允许 `daily`、`manual`、`preupgrade`、`prerestore`。
2. 备份文件名必须包含毫秒时间、程序短版本和备份类型。
3. 备份文件名必须唯一，不得覆盖已有备份。
4. 手动备份只包含已经保存到 SQLite 的数据，不自动保存界面编辑。

#### D. 升级前备份

1. `preupgrade` 在 `App.OnStartup` 中执行。
2. 必须早于 `base.OnStartup`、`MainWindow`、`LocalDataRepository` 和 `LocalDatabase.Initialize`。
3. 数据库不存在时不创建空备份。
4. 记录版本与当前版本不同，或版本键不存在时，必须创建 `preupgrade`。
5. `preupgrade` 创建并校验成功后才允许继续初始化。
6. `preupgrade` 失败必须阻断数据库初始化和主窗口创建。
7. 不得先写入当前版本再创建升级前备份。

#### E. 每日备份

1. 每日备份按本机自然日判断。
2. 当天已有有效 `daily` 或 `preupgrade` 时不得重复创建自动备份。
3. `manual` 不受每日一次限制，也不能代替当天自动备份。
4. 自动备份失败不得记录为已备份。
5. 自动备份失败允许继续启动，但必须明确记录并提示。
6. 自动备份不得触发网络请求。

#### F. 保留策略和并发

1. 最多保留最近 30 份有效受控备份。
2. 只有新备份校验成功后才能清理旧备份。
3. 非受控文件、pending 文件、当前数据库和已选待恢复备份不得删除。
4. 旧备份清理失败不得使新备份失败。
5. 同进程备份和恢复暂存操作通过 `SemaphoreSlim` 串行。
6. 跨进程使用 `backups\.backup.lock` 和 `FileShare.None`。
7. 无法获得跨进程锁时不得重复执行或删除其它进程文件。
8. 备份服务不得增加全局数据库长连接。

#### G. 恢复暂存

1. 程序运行中不得直接替换活动数据库。
2. 恢复必须经过两次明确确认。
3. 只允许恢复受控 `backups` 目录中的有效备份。
4. 备份源文件和暂存副本必须校验 SHA-256。
5. `pending_restore.db` 必须先写临时文件、校验后再原子改名。
6. `pending_restore.json` 必须在 pending 数据库完成后最后写入。
7. marker 不得包含可由用户控制的任意恢复目标路径。
8. 恢复目标只能是固定正式数据库。
9. 取消任意一次确认时，不得生成 pending 文件、关闭程序或修改数据库。

#### H. 启动恢复

1. 恢复在 `MainWindow` 及业务数据库连接创建前执行。
2. marker、pending、SHA-256、`integrity_check` 和基础表必须全部验证。
3. 替换前必须创建并校验 `prerestore` 安全备份。
4. `prerestore` 失败不得替换当前数据库。
5. 替换前必须安全隔离旧 WAL/SHM。
6. 替换后必须立即重新执行完整性和基础表校验。
7. 成功后清理 `pending_restore.db`、`pending_restore.json` 及临时候选文件。
8. `prerestore` 备份必须保留。

#### I. 失败和回滚

1. 数据库替换后校验失败必须回滚。
2. 回滚必须使用当次 `prerestore` 备份。
3. 回滚完成后必须再次执行完整栧和基础表校验。
4. 回滚成功时允许使用原数据库继续启动。
5. 回滚失败必须阻断主窗口和数据库初始化。
6. 回滚失败不得删除恢复证据、marker、pending 或安全备份。
7. 不得伪造恢复成功状态。

#### J. 数据真实性

恢复不得：

- 修改 TradeLog ID、时间、动作或金额。
- 自动新增或删除 TradeLog。
- 清空 `market_history_cache`。
- 把恢复操作当作交易。
- 生成委托定稿或模拟行情。

#### K. UI 和结果提示

1. 备份恢复功能保留在 `ManualDataEntryWindow` 的“系统维护”页。
2. 不新建独立备份或恢复主窗口。
3. 不修改 `ManualDataEntryWindow` 标题栏、`WindowChrome` 或白闪逻辑。
4. 未选中有效备份时恢复按钮必须禁用。
5. 无效备份可显示但不得恢复。
6. 页面不得显示 PushPlus Token 或 TradeLog 明细内容。
7. `restore_result.json` 的结果只显示一次，确认后清理。
8. 成功和失败提示不得泄露数据库内部内容。

#### L. 严格不得影响

- TradeLog 事实源。
- `AccountReplayService`。
- `StrategyDecisionService`。
- `OrderDraftService` 和 `OrderFinalizationService`。
- `MarketDataRefreshService`、`MarketDataClient` 和 `GlobalMarketRequestScheduler`。
- `ChartDataRefreshCoordinator`、`ChartWindowManager` 和 `ChartWindowLifetime`。
- MA、B/S、viewport 和十字光标。
- 风险中心诊断逻辑。
- PushPlus 和语音预警逻辑。
- 全局快捷键逻辑。
- 主界面随机 2-4 秒刷新。
- `TASK-RELEASE-PACKAGING-010` 的发布规范。

#### M. 本地边界

1. 本模块只处理当前 Windows 用户的本地备份。
2. 不上传云端，不使用 FTP、网盘或任何外部网络。
3. 不要求管理员权限，不增加 Windows 服务或常驻进程。
4. 用户数据库、备份、恢复文件和锁文件不得提交到 Git。

### TASK-RUNTIME-STABILITY-012：长时间运行稳定性与资源健康监测

锁定基线：

- 版本：`v8.4.0`
- 功能提交：`5491c7a50603db24100716d79f974b94f1e150b8`
- 测试基线：`1152/1152`
- 人工运行基线：连续运行超过 2 小时；250 个采样；Normal 250；Warning 0；Critical 0；写入错误 0；无异常退出证据。

#### A. 目录与存储边界

1. 健康目录固定为 `%LocalAppData%\CrossETF.Terminal.UiShell.Reference\health`。
2. 日志文件格式固定为 `runtime-health-yyyyMMdd-pid<PID>.jsonl`。
3. 单文件超过 20 MB 后使用 `-partN` 滚动。
4. 最多保留最近 7 个自然日。
5. 多进程必须通过 PID 隔离。
6. 健康采样不得写入 SQLite。
7. 健康目录、JSONL 和报告不得提交 Git。
8. 不得写入 `backups` 或 `restore` 目录。
9. 不得上传网络。

#### B. 采样频率

1. 完整资源采样每 30 秒一次。
2. Dispatcher 探测每 5 秒一次。
3. 不得改变主界面随机 2-4 秒行情刷新。
4. 上一轮未完成时不得堆积下一轮。
5. 程序关闭时必须停止采样。
6. 不得使用 `Thread.Sleep` 阻塞 UI 线程。
7. 不得留下前台线程。

#### C. 采样指标

锁定只读指标：

- WorkingSet。
- PrivateMemory。
- ManagedHeap。
- ThreadCount。
- HandleCount。
- GC 次数。
- CPU 时间。
- Dispatcher 延迟。
- 主刷新状态和耗时。
- 窗口数量。
- 运行时长。
- 退出状态。

禁止采集：

- TradeLog 内容。
- 交易金额。
- 账户余额。
- 持仓数量。
- 策略参数明细。
- Token。
- 行情 payload。
- SQL 内容。
- 用户输入内容。
- 截图。

#### D. Dispatcher 探测

1. 使用异步 Dispatcher 投递。
2. 不得使用无限等待的同步 `Invoke`。
3. 回调中不得读取数据库、刷新 UI 或写文件。
4. Dispatcher 关闭时安全取消。
5. 探测异常不得导致程序崩溃。

#### E. 主刷新监测

1. 只允许在现有刷新入口和出口增加观测。
2. 不得改变刷新内容、顺序或异常处理。
3. 不得改变随机计划、限频、熔断或缓存。
4. 不得额外触发行情请求。
5. 结束状态必须在 `finally` 中完成。
6. 网络失败本身不得直接判定 Critical。

#### F. 健康状态阈值

状态只允许 `Normal`、`Warning`、`Critical`。

- Dispatcher：连续 2 次达到或超过 2000 ms 为 Warning；单次达到或超过 8000 ms 为 Critical。
- 私有内存：达到或超过 1.5 GB 为 Warning；达到或超过 3 GB 为 Critical。
- 30 分钟增长：达到或超过 512 MB 为 Warning；达到或超过 1 GB 为 Critical。
- 线程：达到或超过 250 为 Warning；达到或超过 500 为 Critical。
- 句柄：达到或超过 10000 为 Warning；达到或超过 20000 为 Critical。
- 当前主刷新：超过 30 秒为 Warning；超过 90 秒为 Critical。

#### G. 防抖规则

1. Warning 必须连续 2 次确认。
2. Critical 立即生效。
3. 恢复 Normal 必须连续 3 次正常采样。
4. 相同状态不得重复产生转换事件。
5. 状态不得自动微信、语音、重启或结束进程。
6. 状态不得写 TradeLog。

#### H. 内存趋势

1. 保存最近至少 2 小时内存样本。
2. 缺少 30 分钟样本时不判断 30 分钟增长。
3. 不得调用 `GC.Collect` 或 `WaitForPendingFinalizers`。
4. 不得清理缓存、图表数据或业务对象。
5. 不得根据单个峰值结束程序。

#### I. JSONL 写入

1. 必须先完整序列化，再一次追加完整行。
2. 不得写半个 JSON。
3. 写入失败不得使主程序退出。
4. 成功写入新采样后才清理过期文件。
5. 只允许删除受控健康日志。
6. 不得删除 `reports` 目录或其它系统文件。
7. 写入和清理失败不得影响行情和数据库。

#### J. UI 边界

1. 功能保留在 `ManualDataEntryWindow` 系统维护页。
2. 不新增独立主窗口。
3. 不修改 `ManualDataEntryWindow` 标题栏、`WindowChrome` 或白闪逻辑。
4. 三个按钮固定为“刷新状态”“打开健康日志目录”“导出最近 24 小时报告”。
5. 刷新状态不得触发行情刷新。
6. UI 不得显示 Token、TradeLog 或账户金额。
7. 不得增加强制 GC、自动重启、结束进程或清理数据库按钮。
8. 窗口关闭时必须解除订阅。
9. 服务不得通过静态事件强引用窗口。

#### K. 报告边界

1. 导出 JSON 和 TXT。
2. 统计最近 24 小时。
3. 包含样本、状态计数、资源最大值、Dispatcher、刷新耗时、状态转换和异常退出证据。
4. 不包含任何敏感业务数据。
5. 导出失败不得停止健康监测。

#### L. 生命周期

1. `MainWindow` 构造时创建服务。
2. `Loaded` 后启动且只能启动一次。
3. `Closed` 时请求停止。
4. 停止最长等待 3 秒。
5. `Dispose` 可重复调用。
6. 关闭后不得继续投递 Dispatcher。
7. 不得残留进程或前台线程。

#### M. 严格不得影响

- `TASK-DATA-BACKUP-RESTORE-011`。
- TradeLog 事实源。
- `AccountReplayService`。
- `StrategyDecisionService`。
- `OrderDraftService`。
- `OrderFinalizationService`。
- `MarketDataRefreshService`。
- `MarketDataClient`。
- `GlobalMarketRequestScheduler`。
- `ChartDataRefreshCoordinator`。
- `ChartWindowManager`。
- `ChartWindowLifetime`。
- K 线分页。
- MA、B/S、viewport、十字光标。
- 风险中心诊断逻辑。
- PushPlus 和语音预警。
- 全局快捷键。
- 随机 2-4 秒刷新。
- `TASK-RELEASE-PACKAGING-010`。

#### N. Git 边界

1. 根目录运行输出 `diagnostics` 保持忽略。
2. `Infrastructure/Diagnostics` 源码必须被 Git 正常跟踪。
3. 不得依赖 `git add -f` 提交正式源码。
4. `.gitignore` 规则固定使用 `/diagnostics/`。
5. `health` 目录和报告不得提交。

### TASK-SETTINGS-CENTER-UI-016：系统设置中心UI结构与视觉锁定

锁定基线：

- 正式版本：`v8.5.0`
- 功能 commit：`5049fe06d5610af122d238f833e0eef8b91bc36e`
- 自动化测试：`1182/1182`
- Release build：`0 warning / 0 error`
- 人工最大化视觉验收：通过

#### A. 一级分类

系统设置中心固定为 4 项：

1. 通用设置
2. 预警与通知
3. 数据安全
4. 运行与诊断

未经用户明确授权，不得：

- 恢复原 7 项平铺导航；
- 新增一级分类；
- 新增二级模块；
- 新增设置窗口；
- 新增 Tab；
- 新增设置卡片功能；
- 新增按钮入口。

#### B. 通用设置

固定包含：

- 界面快捷键；
- 软件信息；
- 本地数据目录；
- 系统边界说明。

快捷键与软件信息采用约 40% / 60% 布局。

软件信息用户可见项固定精简为：

- 产品名称；
- 当前版本；
- FileVersion；
- 构建标识。

本地目录路径使用省略显示和完整 ToolTip。

#### C. 预警与通知

固定包含：

- 微信通知；
- 系统语音；
- 重复提醒间隔；
- 严重风险间隔；
- 行情异常间隔；
- 保存预警设置。

微信和语音卡片同高，三个频率项三等分排列。测试微信和测试语音为次级按钮，保存预警设置为主按钮。不得改变 PushPlus、语音、频率保存和测试行为。

#### D. 数据安全

页面顺序固定为：

1. 状态摘要；
2. 备份记录；
3. 数据库位置与维护边界；
4. 恢复数据库危险区。

摘要固定包含：

- 数据库状态；
- 最近有效备份；
- 有效备份数量；
- 自动备份状态。

备份记录表格固定使用横向可用宽度。“立即备份”为主按钮，“恢复选中备份”为危险按钮。切换到数据安全页面时滚动位置回到顶部，`ScrollToTop` 仅影响 UI，不得触发业务刷新。不得修改备份、恢复、完整性验证、双重确认、恢复标记或回滚逻辑。

#### E. 运行与诊断

固定包含：

- 综合状态；
- 行情概要；
- 私有内存；
- 界面延迟；
- 系统诊断；
- 运行健康；
- 日志与报告。

顶部 4 张摘要卡同宽同高，系统诊断和运行健康使用双栏布局，日志与报告保持紧凑，长路径使用 `TextTrimming` 和 ToolTip。不得为了视觉效果篡改真实诊断状态。

#### F. 页面统一规范

锁定：

- 页面标题 `24px`；
- 页面说明 `14px`；
- 页面顶部边距 `28px`；
- 内容最大宽度保持统一；
- 四页标题起点一致；
- 卡片间距一致；
- 卡片边框和圆角一致；
- 左侧导航宽度 `226`；
- 导航项高度 `66`；
- 图标 `19`；
- 主标题 `15`；
- 副标题 `12`；
- 选中项保留 `3px` 蓝色指示条。

未经明确授权，不得重新大改信息架构。

#### G. 按钮层级

主按钮：

- 保存预警设置；
- 立即备份。

危险按钮：

- 恢复选中备份。

其余现有按钮均为次级按钮。不得改变任何 Click 事件和业务行为。

#### H. 严格禁止影响

本任务不得影响：

- TradeLog 事实源；
- `AccountReplayService`；
- `StrategyDecisionService`；
- `OrderDraftService`；
- 行情数据源；
- `GlobalMarketRequestScheduler`；
- 图表；
- 风险中心；
- 备份恢复业务；
- 运行健康业务；
- PushPlus 业务；
- 系统语音业务；
- 随机 2-4 秒刷新；
- `WindowChrome`；
- 标题栏；
- 白闪处理；
- 正式发布规范。

#### I. UI 新增边界

后续默认：

- 不新增设置中心模块；
- 不新增设置中心窗口；
- 不新增 Tab；
- 不新增导航项；
- 不新增按钮。

任何新增必须获得用户明确授权。

#### J. 测试保护

保留设置中心 UI 保护测试，至少保护：

- 一级分类数量和名称；
- 不新增窗口和 Tab；
- 页面标题和布局结构；
- 数据安全顺序；
- `ScrollToTop` 行为；
- 原 Click 绑定；
- `WindowChrome` 和标题栏未改变；
- 业务服务文件未被设置 UI 任务修改。

### TASK-TRADELOG-UI-018：TradeLog页面与表格视觉锁定

锁定基线：

- 正式版本：v8.6.0
- 功能 commit：`b26e0ec30355c18c16c969aa7a38854c6e5bc060`
- 自动化测试：1205/1205
- Release build：0 warning / 0 error
- 人工视觉验收：通过

#### A. 页面范围

TradeLog 独立窗口固定使用：

- 原生标题栏“交易日志录入”；
- 页面内部主标题“交易日志”；
- 页面说明；
- TradeLog 事实源提示；
- 专属工具栏；
- 保存状态区；
- TradeLog DataGrid。

TradeLog 独立 Scope 固定隐藏：

- 公共内容标题；
- 公共数据库路径行；
- TradeLog 标签头。

All Scope 中 TradeLog 标签必须保留。

#### B. 工具栏

固定只保留：

- 新增记录；
- 编辑选中；
- 删除选中；
- 保存全部。

不得增加搜索、筛选、导入、导出、自动保存、重新加载、批量修改、自动成交或复制交易。保存全部为主按钮，删除选中为危险按钮。

#### C. 字段与顺序

TradeLog 固定为 16 列：

1. ID
2. 时间
3. 策略代码
4. 实际代码
5. 动作
6. 来源
7. 档位
8. 数量
9. 价格
10. 金额
11. 手续费
12. 备注
13. 净现金流
14. 本金
15. 现金余额
16. 总资产

没有保存用户列布局时使用上述默认顺序；已有用户列布局时继续采用已有顺序。不得清除 `trade_log` 列布局。

#### D. 人工与系统字段

人工录入字段固定为时间、策略代码、实际代码、动作、来源、档位、数量、价格、金额、手续费和备注。

只读字段固定为 ID、净现金流、本金、现金余额和总资产。

系统计算列通过深色背景、灰蓝文字、只读属性和 ToolTip 进行区分。不得恢复净现金流前的橙色分隔线。

#### E. 金额显示

净现金流、本金、现金余额和总资产固定使用 N2 显示。该格式只影响显示，不得改变 `double` 底层值、数据库存储精度、账务推演精度、账户回放精度或排序字段。数量和价格不得被强制套用 N2 金额格式。

#### F. 最大化布局

TradeLog 页面及 DataGrid 固定使用 Stretch 布局。备注列固定作为弹性列：Width 使用 Star，MinWidth 约 180。不得新增空白填充列。普通窗口继续依靠 DataGrid 内部横向滚动，不得增加页面级横向滚动。

#### G. 编辑状态

页面允许显示已保存、有未保存修改、保存中和保存失败。ID 小于等于 0 的新记录允许使用低亮橙色未保存提示。验证错误使用单元格红色边框和现有 WPF Validation 信息。

不得增加自动保存、关闭自动保存、关闭窗口未保存确认或单元格按键日志。

#### H. 业务边界

不得修改：

- TradeLog 事实源；
- `SaveTradeLogs`；
- `SaveTradeLogsCore`；
- `SafeCommitTradeLogGridEdits`；
- `ValidateTradeLogForSave`；
- `TradeLogLedgerNormalizer`；
- `AccountReplayService`；
- `LocalDataRepository`；
- `SaveTradeLogsSnapshot`；
- 自动金额和人工覆盖金额；
- 删除逻辑和回放逻辑；
- 数据库结构；
- 动作、来源和档位选项。

#### I. 跨模块保护

不得影响设置中心四分类、策略配置、账户状态、持仓、OTCMap、底仓基准、图表、风险中心、WindowChrome、标题栏、白闪处理、主界面 2-4 秒随机刷新、备份恢复、运行健康及正式发布规范。

#### J. 测试保护

保留 TradeLog Workspace UI 测试，至少保护：

- 独立 Scope 标题结构；
- Headerless Tab 行为；
- All Scope 保留 TradeLog Tab；
- 工具栏仅四项；
- 16 列字段和默认列顺序；
- 用户列布局持久化；
- 只读字段；
- N2 财务格式，且数量和价格不套用 N2；
- 备注 Star 列；
- 无填充列和无橙色分隔线；
- 无自动保存；
- 保存、回放和仓储逻辑不变；
- 设置中心不变；
- WindowChrome 和标题栏不变。

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
