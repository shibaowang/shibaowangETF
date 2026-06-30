# TASK-MARKET-002: 真实行情联网 + 随机刷新 + 防封限频

## 2026-06-16 收口补丁：历史 K 线缓存质量保护

隔夜运行排查确认，`push2his` 可能短暂返回 `ResponseEnded` / `The response ended prematurely`，并存在日线失败后 fallback 到 `klt=103` 月线导致缓存退化的风险。本补丁要求写入 `market_history_cache` 前比较新旧 payload 质量：已有 DailyLike 日线缓存时，MonthlyLike fallback、空 `data.klines`、点数显著缩水或更旧的 payload 不得覆盖旧缓存；无旧缓存时允许真实月线 fallback 作为降级缓存，并用 `HISTORY_DEGRADED_MONTHLY_FALLBACK` 记录。`251.NDXTMC` 与 `100.NDX100` 属于核心指数，已有日线缓存时月线 fallback 永远不能覆盖。历史源短暂失败但实时行情正常且核心日线缓存有效时，主连接状态保持实时行情正常，不再误导为整体“部分连接”。系统仍不使用假 K 线、假高点或随机曲线。

## 收口结论

模块 2 按真实数据源状态收口：

- 实时行情联网：完成。
- 防封限频：完成。
- 缓存和熔断：完成。
- 历史高点链路：代码完成，但当前东方财富 `push2his` 真实接口未返回，状态保持未就绪。
- T1-T6 前置状态：未就绪。

早期周末验证时，东方财富历史 K 线 `push2his` 未返回；用户确认同一时间 VBA V8.2 源码中的同源历史高点自动获取也无法成功。后续进一步定位确认：开启 Clash Verge / 系统代理时，`push2his.eastmoney.com` 会出现 `ERR_EMPTY_RESPONSE` / `ResponseEnded` / `The response ended prematurely`；关闭代理后，同一日线 K 线接口可以返回真实 `data.klines`。系统不使用假高点、不显示假曲线、不把历史高点标记为成功。

## 数据来源

- ETF 实时行情：腾讯 `http://qt.gtimg.cn/q=...`，GB18030 解码。
- 指数实时行情：东方财富 `https://push2.eastmoney.com/api/qt/ulist.np/get?secids=...&fields=f12,f2,f3,f43,f170`。
- 场外基金净值：新浪 `http://hq.sinajs.cn/list=f_基金代码`，GB18030 解码。
- 历史高点：东方财富 `https://push2his.eastmoney.com/api/qt/stock/kline/get?...`。

## 历史高点实现口径

历史高点链路已按 VBA V8.2 口径迁移：

- `NormalizeEMSecID` 支持并保留 `251.NDXTMC`、`100.NDX100`、`100.NDX`、`116.HSI`、`1.000300`、`0.399001` 等东方财富 secid。
- ETF 使用 `fqt=1` 前复权；指数使用 `fqt=0` 不复权。
- 指数历史 K 线优先请求 `klt=101` 日线，失败后回退 `klt=103` 月线；ETF 历史高点仍优先请求 `klt=103` 月线，失败后回退 `klt=101` 日线。
- 解析 `data.klines`，每行按英文逗号拆分，用 `fields[3] / f54` 计算历史最高点。
- 支持 JSON 和 JSONP 响应。
- `push2his` 历史 K 线请求使用独立 no-proxy `HttpClient`，`SocketsHttpHandler.UseProxy=false`，不依赖系统代理，不要求用户关闭 Clash；GPT / Codex 仍可继续走代理。
- 请求使用 HTTP/1.1、`User-Agent: Mozilla/5.0`、`Referer: https://quote.eastmoney.com/`、`Accept: application/json,text/plain,*/*`、`Accept-Language: zh-CN,zh;q=0.9,en;q=0.8`。
- 失败时尝试 `rtntype=6`、`ut=fa5fd1943c7b386f172d6893dbfba10b`、`klt=101` 日线 fallback 和 EastMoney 历史 K 线 host fallback。
- 失败写入 `runtime_log` 和 `market_source_status`，连续失败进入 10 分钟熔断。
- 当天如果 `251.NDXTMC` / `100.NDX100` 已有缓存但只有月线级别稀疏点数，程序会继续尝试获取真实日线；只有真实日线成功后才覆盖缓存。

## UI 状态

- 左图固定为 `251.NDXTMC`，标题为“纳斯达克科技指数回撤监控”。
- 右图固定为 `100.NDX100`，标题为“纳斯达克100指数回撤监控”。
- 当前历史 K 线失败时显示“历史 K 线暂不可用，T1-T6 前置未就绪”。
- T1-T6 前置状态当前显示“未就绪”，原因是 `251.NDXTMC` / `100.NDX100` 历史 K 线未成功返回。
- 不显示假曲线，不显示假高点，不用用户手工高点冒充接口成功。

真实日线成功时，左图使用 `251.NDXTMC` 真实日线 K 线，右图使用 `100.NDX100` 真实日线 K 线，X 轴日期来自真实 K 线日期，T1-T6 前置状态才允许变为“已就绪”。

## 刷新和限频

- UI 本地刷新保持 2 到 4 秒随机间隔，只负责刷新时间、状态、缓存显示和本地数据库读取。
- ETF 真实网络请求按腾讯来源批量请求，最小间隔 6 秒，随机 6 到 10 秒。
- 指数、全球指数、汇率按东方财富来源批量请求，最小间隔 10 秒，随机 10 到 20 秒。
- 场外基金净值按新浪来源批量请求，最小间隔 60 秒，随机 60 到 120 秒。
- 历史高点按标的每日最多成功刷新一次，失败后进入 10 分钟冷却。
- 请求超时由 `HttpClient` 控制，当前为 8 秒。

## 后续交易日验证

需要在交易日重新验证：

- `251.NDXTMC` 历史 K 线是否返回真实 `data.klines`。
- `100.NDX100` 历史 K 线是否返回真实 `data.klines`。
- 两个指数均成功后，才允许 T1-T6 前置状态变为“已就绪”，再进入依赖历史高点的 T1-T6 策略计算。

## 人工验收

1. 执行 `dotnet build .\CrossETF.Terminal.UiShell.Reference.sln -c Debug`。
2. 执行 `dotnet test .\CrossETF.Terminal.UiShell.Reference.sln -c Debug --no-build`。
3. 执行 `dotnet run --project .\CrossETF.Terminal.UiShell.Reference.csproj`。
4. 运行至少 3 分钟。
5. 确认腾讯 ETF 实时行情正常。
6. 确认东方财富实时指数正常。
7. 确认新浪基金净值正常。
8. 确认历史高点失败不导致程序卡死，并进入熔断。
9. 确认左图固定 `251.NDXTMC`，右图固定 `100.NDX100`。
10. 确认 T1-T6 前置状态显示“未就绪”。
11. 确认没有手动刷新按钮、没有模拟行情、没有模拟交易、没有假高点、没有假曲线。
