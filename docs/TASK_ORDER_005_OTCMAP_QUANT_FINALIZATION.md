# TASK-ORDER-005: OTCMap A/C 多通道 + 量化委托 + 交易定稿

## 实现范围

模块 5 在模块 4 的 `strategy_decision_state` 之后工作，只把策略建议转换为可检查的委托草案和可冻结的交易定稿，不产生真实成交。

输入数据全部来自本地真实链路：

- `strategy_decision_state`
- `account_replay_state`
- `position_replay_state`
- `otc_position_replay_state`
- `otc_channel`
- `trade_log`
- `market_quote_cache`

## 场内 ETF 量化规则

- 买入使用真实 ETF 价格或策略建议价格。
- 买入金额受 `target_amount`、现金余额、`real_sniper_pool` 共同约束。
- 买入数量按 100 股整手向下取整。
- 不足 100 股时标记为不可执行。
- 卖出使用真实 ETF 价格，卖出数量不超过当前场内持仓数量。
- 卖出金额受底仓保护约束：`max(0, base_current_cost - base_target_amount)`。
- 卖出数量按 `sell_space / average_cost` 和目标卖出金额共同封顶，再按 100 股整手向下取整。

## OTC A/C 通道规则

- 场外买入读取启用的 `otc_channel`。
- 按 `priority` 排序拆单。
- 每个通道扣减当日已在 `trade_log` 中记录的买入金额。
- 通道 `daily_limit <= 0` 视为不设本地日限额。
- 通道剩余额度低于 `min_buy` 时跳过。
- 场外买入按金额定稿，不要求净值；金额保留到分。
- 额度不足但仍有部分可申购时标记为 `部分可委托`。
- 场外卖出优先使用 C 类通道，再使用 A 类通道。
- 场外卖出必须有真实净值或行情净值；缺少净值时不可执行，不使用假净值。
- 场外卖出不超过当前场外持仓数量，也不突破底仓保护金额。

## 派生表和定稿表

新增派生草案表：

- `order_draft_state`
- `order_draft_leg_state`

新增定稿表：

- `order_finalization_state`
- `order_finalization_leg_state`

`order_draft_state` / `order_draft_leg_state` 是派生表，会随最新策略、账户、持仓、OTCMap、TradeLog、行情输入变化而清空重算。

`order_finalization_state` / `order_finalization_leg_state` 是定稿记录，只追加冻结当前可执行草案。定稿不是成交，不写入 `trade_log`。

## UI

主界面底部左侧区域显示委托草案预览、草案汇总和最新定稿状态。

“定稿当前草案”按钮只把当前可执行草案冻结到定稿表：

- 不刷新行情；
- 不自动写 TradeLog；
- 不模拟成交；
- 不接券商。

如果最新定稿的 `snapshot_key` 与当前草案不一致，UI 显示“可能失效”，提示用户重新核对。

## 自动刷新

程序启动和 2-4 秒本地刷新时读取最新草案。

后台草案计算在以下输入变化时自动执行：

- 策略决策派生表变化；
- 账户回放变化；
- 场内/场外持仓回放变化；
- OTCMap 配置变化；
- TradeLog 变化；
- 行情缓存变化。

计算失败会写入 `runtime_log`，并在 UI 状态显示错误，不阻塞主界面。

## 边界

- 不接入真实下单。
- 不接券商。
- 不自动写 TradeLog。
- 不做模拟行情。
- 不做模拟交易。
- 不新增手动刷新按钮。
- 不改策略决策口径。
- 不改行情接口。
- 不改账户回放。
- 不改曲线逻辑。
