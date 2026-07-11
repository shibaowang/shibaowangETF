# TASK-DIAG-006-PATCH：排除已删除配置的历史孤立行情缓存

## 目标版本

`v8.1.1`

## 问题

`market_quote_cache` 会保留历史行情。策略删除后，对应缓存不应被当作当前系统健康状态的一部分。

本次只修正运行诊断的只读展示和健康统计：

- 不删除或更新历史缓存；
- 不修改行情请求、parser、router 或 scheduler；
- 不修改今日/当日盈亏审计；
- 不触发联网请求。

## 活动行情集合

运行诊断按现有业务入口构建活动行情集合：

1. `strategy_config.enabled = 1` 的 ETF 代码；
2. 启用策略的 `index_sec_id`；
3. 属于启用策略且 `otc_channel.enabled = 1` 的场外基金代码；
4. `MarketSymbolNormalizer.DefaultTopBarItems()` 中的固定顶部指数和汇率；
5. `position_state` 中来源为“场内ETF”的实际代码，与腾讯 ETF 刷新入口保持一致。

活动集合使用 `market_type + normalized symbol` 作为键，不按单个代码写特判。

## 历史孤立缓存

不属于活动集合的 `market_quote_cache` 行：

- 保留在 SQLite 中；
- 默认不在“行情与缓存”页展示；
- 不计入 `StaleQuoteCount`；
- 不影响 `OverallStatus`；
- 不触发行情请求；
- 不影响今日盈亏审计。

活动行情本身过期时仍正常显示为“过期”，并使整体状态进入“警告”。
