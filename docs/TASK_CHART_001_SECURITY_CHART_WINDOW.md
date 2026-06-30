# TASK-CHART-001：ETF 标的走势图窗口

## 2026-06-30 TASK-INDEX-INTRADAY-MACD-VOLUME-030 index intraday MACD and volume closeout

- `251.NDXTMC` and `100.NDX100` intraday MACD now follows the full `09:30-16:00 ET` index display sequence. The ETF intraday display limit is still applied only to ETF snapshots, not to index MACD.
- MACD uses real intraday price points plus the existing display-only close quote point when that point is valid. It does not write quote points to `chart_intraday_cache` and does not synthesize middle-minute data.
- Index intraday volume is still shown only when real payload volume is non-zero. `251.NDXTMC` currently has all-zero EastMoney volume fields and therefore keeps `成交量数据不可用`; `100.NDX100` has real non-zero volume and remains available.
- This task does not alter ETF Tencent data sources, index quote lanes, drawdown charts, GlobalMarketRequestScheduler throttling, alerts, orders, or TradeLog.

## 2026-06-30 TASK-INDEX-INTRADAY-US-SESSION-DATE-029 index US session date ownership

- Index intraday points for `251.NDXTMC` and `100.NDX100` are displayed by US Eastern regular-session ownership, not by Beijing calendar day. Points from `21:30-23:59` Beijing and `00:00-04:00` Beijing can belong to the same Eastern trading date and must stay in one chart.
- The index intraday snapshot keeps the full real `REAL_TRENDS2` session instead of applying the ETF tail trim limit. This prevents the chart from dropping the `09:30-12:00 ET` morning segment.
- Completeness checks now require both morning-session reach and close-session reach. A cache starting around `12:00 ET` is partial even if it reaches `16:00 ET`.
- `QUOTE_CLOSE_DISPLAY` continues to be display-only final close calibration. It does not write `chart_intraday_cache`, does not fill middle minutes, and does not affect ETF Tencent intraday or ETF K-line behavior.

## 2026-06-30 TASK-INDEX-INTRADAY-CLOSE-QUOTE-ALIGN-028 index close quote display alignment

- After US regular-session close, `251.NDXTMC` and `100.NDX100` can replace the display-side final intraday point with the latest same-day quote when the real EastMoney intraday cache is complete for the latest completed US trading day.
- The display-only close point uses `PointSource=QUOTE_CLOSE_DISPLAY` and `IsQuoteCloseDisplayPoint=true`. It is connected as the final displayed close point because the cache already reaches the close session; it is not a middle-minute fill.
- The close alignment never writes `chart_intraday_cache`, never changes real `EASTMONEY_INTRADAY / REAL_TRENDS2` payloads, never requests the network, and never affects ETF Tencent intraday or ETF K-line paths.
- During US trading hours, missing quote, stale/partial cache, or nonmatching trade date, the chart keeps the existing real cache and does not align the close with quote.

## 2026-06-30 TASK-INDEX-INTRADAY-CATCHUP-027 index after-hours catch-up

- `251.NDXTMC` and `100.NDX100` index intraday cache completeness is evaluated on the US Eastern regular-session axis. The latest completed US trading day is considered complete only when the latest real point is near `15:55-16:00` Eastern Time.
- Outside US trading hours, a missing, stale, or partial index intraday cache may trigger one low-frequency EastMoney `trends2` catch-up through `GlobalMarketRequestScheduler`.
- Catch-up success persists only parseable real `EASTMONEY_INTRADAY / REAL_TRENDS2` payloads to `chart_intraday_cache`. Catch-up failure keeps the existing cache and keeps quote as an independent display marker.
- This does not change ETF Tencent intraday, ETF K-lines, top index quote lanes, index drawdown realtime locks, or any strategy/order/TradeLog behavior.

## 2026-06-29 TASK-CHART-KLINE-LIVE-BAR-017 live K bar display

- ETF charts and the two index charts can add a display-only current trading-day K bar when the latest DailyLike history is older than quote time and quote OHLC fields are complete.
- The display-only bar is marked with `PointSource=QUOTE_INTRADAY_BAR`; it is used only by Daily/Weekly/Monthly snapshots and is never written to `market_history_cache`.
- Weekly and Monthly continue to aggregate locally from DailyLike history plus the optional display-only bar. Missing quote OHLC means no temporary K bar is created.

## 2026-06-26 TASK-INDEX-CHART-PERIOD-FIDELITY-012 index period fidelity

- `251.NDXTMC` and `100.NDX100` keep the existing EastMoney route for index quote, intraday, and DailyLike history; ETF Tencent chart routing is unchanged.
- EastMoney index quote now requests `f15/f16/f17/f18` and parses them with the same `/100` scaling as `f2`, so true open/high/low/previous-close values can be used when the endpoint provides them.
- The formal index DailyLike cache still represents completed trading days. During an active US trading session, if the latest cache ends at the previous completed session and the quote contains real OHLC, `ChartDataService` adds a display-only current-session K bar with `PointSource=QUOTE_INTRADAY_BAR`.
- The display-only K bar participates in the current Daily/Weekly/Monthly chart snapshot and MACD calculation, but it is never written to `market_history_cache` and never replaces DailyLike history. If quote OHLC is missing, the chart keeps the completed-day DailyLike cache and reports that the intraday OHLC fields are insufficient.
- Index intraday quote tails remain independent display markers and are not connected as real intraday line segments, not written to `chart_intraday_cache`, and not used to fill missing middle-minute data.

## 2026-06-25 TASK-INDEX-INTRADAY-AXIS-009 index intraday US Eastern axis

- `251.NDXTMC` and `100.NDX100` intraday charts use a fixed `09:30-16:00` Eastern Time axis. EastMoney China-time timestamps are converted to Eastern Time with `TimeZoneInfo`.
- Index intraday data is not stretched by point count. A latest point at 10:15 Eastern occupies only `45 / 390` of the plot width and the remaining future session is blank.
- Index labels include `09:30`, `16:00`, and `美东时间`. ETF intraday labels and A-share compressed trading axis remain unchanged.
- Index quote tails are mapped by their real converted time and are never forced to 16:00 or used to fill future time.

## 2026-06-25 TASK-READONLY-INDEX-CHART-DATA-007 index chart route closeout

- Index chart data for `251.NDXTMC` and `100.NDX100` remains EastMoney-only for quote, intraday, and DailyLike history. ETF Tencent chart routing is unchanged.
- EastMoney index intraday points can be timestamped outside the A-share ETF trading window. `ChartDataService` and `SecurityChartWindow` now keep those real index points instead of applying the ETF `IntradayTradingTimeAxis` filter.
- Index intraday drawing uses the real point order with dynamic labels. ETF intraday drawing still uses the fixed standard trading-time axis.
- `index_chart_data_trace.json` is the diagnostic output for interface/cache/chart point counts and does not participate in runtime logic.

## 2026-06-25 TASK-MARKET-KLINE-FRESHNESS-006 K-line freshness

- ETF 日K / 周K / 月K 新鲜度统一走真实 DailyLike：ETF 继续腾讯 qfq daily 优先，指数继续东方财富。
- 日K选择不再简单“内存优先”。`ChartDataService` 会在内存 `ChartCache` DailyLike 和 SQLite `market_history_cache` DailyLike 之间比较最后 K 线日期；日期相同再比较缓存更新时间 / `received_at`。旧内存缓存不能压住更新的 SQLite DailyLike。
- `ChartDataRefreshCoordinator` 对处于日K / 周K / 月K 订阅中的标的会按路由和限频尝试刷新真实日K，即使已有 DailyLike 缓存也不会永久跳过网络刷新。失败、熔断、限频仍保留已有真实 DailyLike。
- 周K、月K只从上述选中的最新 DailyLike 日K序列聚合，不读旧周/月缓存，不用 MonthlyLike 冒充日K。
- 诊断文件 `kline_freshness_trace.json` 只用于排查接口层、缓存层、图表层日期一致性，不参与正式运行路径。

## 2026-06-25 TASK-MARKET-TENCENT-ETF-CHART-005 Tencent ETF chart source

- ETF realtime quote uses Tencent first through the existing `qt.gtimg.cn` path.
- ETF intraday chart data uses Tencent `minute/query` with `UseProxy=false`. The parser reads real minute price and converts Tencent cumulative volume/amount fields to per-minute values before drawing and before writing `chart_intraday_cache`.
- ETF daily chart data uses Tencent qfq daily `fqkline/get?param={code},day,,,320,qfq` with `UseProxy=false`. Tencent `qfqday` rows are normalized into the existing DailyLike payload format, then saved to `market_history_cache` with `source=TENCENT_DAILY_QFQ` only after DailyLike quality passes.
- Tencent intraday cache writes use `source=TENCENT_INTRADAY` and `quality=REAL_TENCENT_INTRADAY`.
- Index charts for `251.NDXTMC` and `100.NDX100` still use the existing EastMoney no-proxy routes for intraday and daily history.
- DailyLike protection remains unchanged: MonthlyLike, Sparse, Invalid, failed responses, quote tails, generated points, random points, and simulated points cannot replace real DailyLike chart data.

## 2026-06-23 TASK-CHART-013 交易时段后台真实分时缓存

- 主界面刷新时会把全部启用的策略代码作为后台分时缓存目标传入 `ChartDataRefreshCoordinator`，不再要求用户先打开某个 `SecurityChartWindow` 才能更新该标的分时缓存。
- 后台缓存目标和窗口显示订阅已分离：关闭走势图窗口只取消显示订阅，不会取消启用策略标的在交易时段的后台真实分时缓存刷新。
- 后台刷新只使用腾讯真实 `minute/query`，串行执行、按标的去重，每轮最多发起少量请求，并使用 60 秒每标的限频和既有 `TENCENT_INTRADAY:<strategy_code>` 熔断器；主界面 2-4 秒 tick 不会对全部标的高频打接口。
- 交易时段 `09:30-11:30`、`13:00-15:00` 执行后台更新；午休和非交易时段跳过；15:00 后 15 分钟内每个标的最多补拉一次收盘后最终真实分时缓存。
- `chart_intraday_cache` 仍只保存可解析的真实腾讯 `minute/query` payload，`quality=REAL_TENCENT_INTRADAY`、`source=TENCENT_INTRADAY`。失败响应、空 payload、实时 quote 尾点、补点、随机点或模拟点都不会写入该表。
- 打开走势图窗口时继续优先读取最新内存缓存，其次读取 SQLite `chart_intraday_cache`；接口失败时保留最近真实缓存，可叠加显示端 quote 尾点，但 quote 尾点不持久化。

## 2026-06-22 TASK-CHART-012 分时缓存回退与日K DailyLike 收口

- 分时仍只使用东方财富真实 `trends2` 数据。成功获取并能解析出真实分时点后，原始 payload 会写入图表专用缓存表 `chart_intraday_cache`，字段为 `strategy_code / actual_code / trade_date / updated_at / payload / quality / source`。
- `chart_intraday_cache` 读取端继续兼容旧 `quality=REAL_TRENDS2`、`source=EASTMONEY_INTRADAY` 真实 payload；新的 ETF 分时写入使用 `quality=REAL_TENCENT_INTRADAY`、`source=TENCENT_INTRADAY`。失败响应、空 payload、无法解析的 payload、实时 quote 尾点、补点或模拟点都不会写入该表。
- `trends2` 失败、限频或进入熔断时，走势图优先继续使用内存真实分时缓存；内存没有时回退 SQLite `chart_intraday_cache`；两者都没有时显示 `分时接口失败，无可用分时缓存` 或 `分时接口熔断中，无可用分时缓存`。非交易时段 `no data.trends` 不计入严重熔断失败。
- 实时 quote 尾点仍只用于当前窗口显示最新价，可以和真实分时缓存一起展示，但不会写入分时缓存，也不会重复复制生成假分时线或假成交量。
- 日K只接受 DailyLike。读取优先级为：内存 `ChartCache` DailyLike、SQLite `market_history_cache` 任意可用 DailyLike、真实 `push2his` DailyLike 请求。较新的 MonthlyLike / Sparse / Invalid 缓存不会挡住较旧的 DailyLike；完全没有 DailyLike 时显示 `无可用DailyLike日K缓存`。

## 2026-06-21 TASK-CHART-004C intraday volume field closeout

EastMoney `trends2` rows are requested with `fields2=f51,f52,f53,f54,f55,f56,f57,f58`. Real samples for `159941` and `159659` show the row layout is:

```text
f51 = time
f52-f55 = price/OHLC fields used by EastMoney intraday
f56 = real minute volume
f57 = real minute amount
f58 = average price
```

The `f56` value frequently decreases from one minute to the next, so it is minute volume, not cumulative volume. The parser preserves it directly and does not apply a second diff. The generic normalizer still supports cumulative inputs for tests or future endpoint variants, but the EastMoney `trends2` parser uses the minute-volume path. The volume sub-chart scales every bar with `minuteVolume / maxVisibleMinuteVolume * chartHeight`, so larger real minute volume produces a taller bar and missing or zero volume produces no fake bar. Realtime quote tail points may update the displayed price only; they do not create quote-derived volume bars.

## 当前范围

主界面“跨境ETF监控与决策”表支持双击任意 ETF 行打开该标的走势图窗口。窗口只做只读查看，不生成委托、不写 TradeLog、不触发 PushPlus 或系统语音。

## 数据来源

- 实时报价：复用主系统 2-4 秒随机刷新后写入的 `market_quote_cache`，图表窗口不重复请求 quote。
- 分时：通过统一 `ChartDataRefreshCoordinator` 调用东方财富真实 `trends2` 分时接口；请求使用现有 no-proxy `MarketDataClient`，按标的去重、限频和熔断。
- 分时尾点：当真实分时缓存存在或 quote 有新价格时，可显示一个由真实 quote 驱动的临时尾点；该点只用于窗口显示，不写入历史分时缓存，也不冒充接口返回数据。
- 日 K：优先使用内存 `ChartCache` 中已有的 DailyLike 真实日 K，其次回退 `market_history_cache` 中 DailyLike 的真实东方财富历史 K 线。若数据库只有月线或稀疏缓存，图表调度器会按低频策略拉取真实日线到内存 `ChartCache`，不覆盖数据库缓存，也不会用 MonthlyLike / Sparse / Invalid 覆盖已有 DailyLike。
- 周 K / 月 K：从真实日 K OHLCV 聚合，开盘取首日、最高取区间最高、最低取区间最低、收盘取末日、成交量和成交额按区间求和。
- 成交量：来自真实分时或真实 K 线中的 volume 字段。缺失时显示“成交量数据不可用”，不生成假柱。
- MACD：分时 MACD 基于真实 `IntradayPoint.Price` 序列本地计算，日 K / 周 K / 月 K MACD 基于真实 K 线收盘价本地计算；参数均为 EMA12 / EMA26 / DEA9，柱值为 `2 * (DIF - DEA)`。数据不足时显示“MACD数据不足”。

## 刷新与防封

图表显示跟随主系统 2-4 秒刷新 tick 重绘，但网络请求统一由 `ChartDataRefreshCoordinator` 调度：

- 同一标的重复打开只激活已有窗口。
- 多窗口共享 `ChartCache`。
- 分时接口最小请求间隔为 20 秒。
- chart-only 日 K 接口最小请求间隔为 5 分钟。
- 同一源连续失败 3 次进入 10 分钟熔断。
- 失败写入 `runtime_log`，不使用假数据兜底。
- 日 K 接口失败或返回非 DailyLike 时，已有 DailyLike 内存缓存继续保留并显示为“使用最近真实缓存”；完全没有 DailyLike 时才显示“日K数据暂不可用”。
- 窗口关闭后取消订阅，不再为该窗口刷新或请求。

## 2026-06-20 分时坐标轴与深色边框收口

分时图 X 轴固定使用国内场内 ETF 标准交易时段：上午 `09:30-11:30`，下午 `13:00-15:00`。坐标左边界固定为 `09:30`，右边界固定为 `15:00`，不会再用第一条分时数据、最后一条分时数据、当前时间或最新 quote 时间动态改变坐标轴范围。午休 `11:30-13:00` 按断档处理，不按真实 90 分钟空档拉长图形；非交易时段点会被安全忽略，不污染横轴。

分时数据仍只来自东方财富真实 `trends2` 或真实 quote 尾点。quote 尾点仅用于显示，不写入历史分时缓存；如果 quote 时间超出标准交易时段，也不会进入分时曲线。走势图主图、副图 Canvas 背景和容器边框已统一为深色主题，避免系统默认透明背景或焦点框形成白色边框。

## 2026-06-20 走势图窗口外层白边收口

`SecurityChartWindow` 使用自定义 `WindowChrome` 覆盖系统非客户区，`GlassFrameThickness=0`、`UseAeroCaptionButtons=False`，避免 Windows 默认 resize 边框和标题栏边缘回退成白色。窗口最外层 `Border` 与根 `Grid` 均使用深色背景，外框、主图、副图和按钮焦点状态全部使用深色主题资源。

该收口只修复走势图窗口外层白边、标题栏边缘白线、主图/副图浅色外框和按钮默认白色焦点框，不改变分时、日K、周K、月K、成交量、MACD 数据来源和绘制口径。

## 2026-06-21 分时午休连续与成交量红绿柱

分时主图继续只使用东方财富真实 `trends2` 分时点和真实 quote 显示尾点。绘制时按有效交易分钟映射 X 坐标，午休 `11:30-13:00` 不按自然 90 分钟拉开，也不再按上午/下午 session 拆成两条折线；上午最后点与下午第一点在压缩后的时间轴上连续连接。分时显示上限单独覆盖完整交易日分钟点，日K/周K/月K 显示上限保持原口径。

分时成交量副图继续读取真实分时 `volume` 字段。量柱颜色按当前分时价与上一分时价比较：上涨红柱、下跌绿柱、持平继承上一柱颜色，首根为中性颜色。缺失成交量的分时点不生成量柱，全部缺失时显示“成交量数据不可用”，不补假量、不随机生成量柱。

## 2026-06-22 分时 MACD 时间轴对齐

分时 MACD 继续基于真实 `IntradayPoint.Price` 序列计算，`MacdPoint` 保留原始分时时间。绘制分时 MACD 时不再使用点序号把柱线均匀铺满全宽，而是与分时主图、分时成交量共用 `IntradayTradingTimeAxis` 的有效交易分钟 X 坐标：`09:30-11:30`、`13:00-15:00`，午休压缩不断线。若最新真实分时点只到 `14:28`，MACD 最后点也停在 `14:28` 对应位置，右侧到 `15:00` 留空；只有真实点到 `15:00` 时才绘制到右边界。

## 2026-06-22 分时昨收零轴

分时主图增加真实昨收 / 0% 基准白色虚线。昨收价优先来自实时 ETF quote 的 `LastClose`，缺失时才从真实 DailyLike 日 K 中取上一交易日 close。Y 轴按昨收上下对称映射：`displayMax = previousClose + maxDelta`、`displayMin = previousClose - maxDelta`，高于昨收的点落在线上方，低于昨收的点落在线下方，等于昨收的点落在白线上。若没有真实昨收价，窗口显示“昨收线不可用”，不使用当前价、第一条分时价、均价、开盘价或图表中位数伪造零轴。

## 边界

本功能未改策略逻辑、委托草案、账户回放、持仓回放、TradeLog、数据库业务表结构、预警系统和主界面行情刷新按钮。走势图窗口不接券商、不模拟行情、不模拟交易、不接个人微信 Hook、不模拟登录微信、不抓包微信客户端。
