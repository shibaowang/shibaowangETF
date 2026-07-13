# TASK-CHART-WINDOW-MAXIMIZE-009：走势图窗口最大化与还原按钮

## 范围

- 目标测试版本：`V8.2.1`。
- 仅修改 `SecurityChartWindow` 自定义标题栏。
- ETF 与指数走势图共用该窗口，因此分时、日K、周K和月K均获得相同窗口操作能力。

## 行为

1. 自定义标题栏按“最小化、最大化/还原、关闭”顺序显示三个按钮。
2. 普通状态显示最大化图标和“最大化”提示；最大化状态显示还原图标和“还原”提示。
3. 按钮通过 WPF `SystemCommands.MaximizeWindow` 和 `SystemCommands.RestoreWindow` 切换状态。
4. 双击标题栏在普通与最大化状态之间切换；普通状态单击标题栏仍可拖动窗口。
5. 最大化由 WPF/Windows 使用当前显示器工作区，不写死分辨率或主显示器尺寸。

## 保持不变

- `WindowStyle=None`、`ResizeMode=CanResize` 和现有 `WindowChrome` 配置不变。
- 不修改图表数据、行情、缓存或数据库逻辑。
- 不修改 viewport、均线、B/S、十字光标或周期状态。
- 不修改其它窗口、`ChartWindowManager`、窗口 `LifetimeToken` 或深历史取消逻辑。
- 最大化/还原不重新创建窗口、不重新订阅，也不取消或重启深历史任务。
- 不读写 TradeLog，不修改账户回放、策略或委托。
