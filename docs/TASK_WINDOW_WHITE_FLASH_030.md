# TASK-WINDOW-WHITE-FLASH-030：五个业务窗口首次打开白闪统一修复

## 版本与状态

- 目标测试版本：V8.10.5。
- 稳定正式基线：V8.10.3，提交 `d57cccba9d8e6f6c6cc1b7bbf0f2c2dc5e2a8f9c`。
- 本任务完成代码和自动测试后仅进入用户实机验收，不提交、不推送、不创建标签、不发布正式目录。

## 用户现象与预检结论

以下五个业务窗口首次打开时可能短暂暴露白色客户区：

1. T1-T6 看图中心；
2. 资金仓位中心；
3. 指标回撤中心；
4. 溢价决策窗口（`ManualDataEntryWindow`）；
5. 行情监控中心。

预检确认五个窗口的 WPF `Window` 和根容器已经使用不透明深色 `#050B14`，主要缺口是原生 HWND 创建后、WPF 首次完成客户区绘制前可能使用系统默认擦除背景。部分窗口首次布局工作量不同，但本任务不混入数据加载或布局重构。

## 统一首帧处理

共享实现位于 `Views/WindowWhiteFlashGuard.cs`，职责严格限定为窗口原生首帧：

- 在构造函数的 `InitializeComponent()` 后立即接入，并监听 `SourceInitialized`；
- 通过窗口句柄取得 `HwndSource`，在首个 WPF 可见帧前设置 `CompositionTarget.BackgroundColor`；
- 为每个窗口创建一支 `#050B14` GDI 实色画刷；
- 处理 `WM_ERASEBKGND`，使用 `GetClientRect` 和 `FillRect` 覆盖当前完整客户区，避免固定尺寸并兼容 DPI、缩放、最大化和还原；
- 同一窗口重复接入返回同一 guard，不重复挂 hook；
- `HwndSource` 销毁或窗口关闭时移除 hook，并通过安全句柄调用 `DeleteObject` 释放画刷；
- 原生资源创建或源获取失败时显式报错，不以静默 `catch` 掩盖资源错误。

五个窗口删除各自重复、只设置 `CompositionTarget.BackgroundColor` 的旧方法，统一使用上述实现。行情监控原有首帧代码也合并到同一 helper，不叠加第二套 hook。

## 深色背景与禁止方案

- 五个目标 `Window.Background` 均保持 `#050B14`；
- 五个根 `Grid` 均保持 `#050B14`；
- 不使用 `Opacity=0`、`Visibility=Hidden`、透明窗口、延迟 `Show`、`Thread.Sleep`、`Task.Delay`、定时遮罩或白色 Loading；
- 不修改 `WindowChrome`、系统标题栏、最大化/还原、窗口尺寸、位置、Owner、`Show`/`ShowDialog` 或单实例规则；
- 不预先永久创建窗口，不禁用硬件加速，不修改系统主题。

## 业务与数据边界

本任务不修改窗口构造函数中的业务加载顺序、`DataContext`、绑定、刷新周期或数据查询入口，也不访问或修改：

- TradeLog、账户回放、持仓、策略和委托；
- SQLite 数据库、schema、缓存或用户数据；
- 行情接口、parser、router、scheduler 或网络层；
- 正式发布目录和桌面快捷方式；
- `docs/LOCKED_MODULES.md`。

## 自动测试范围

- 五个窗口均具有深色 `Window` 和根容器；
- 五个窗口均在 `InitializeComponent()` 后接入同一 helper，不在 `Loaded` 后延迟处理；
- 原有 DWM 深色标题栏处理、业务绑定、加载、刷新和窗口生命周期测试继续保留；
- helper 重复接入只挂一次 hook；
- 关闭后 hook 和 GDI 画刷均释放；
- 多次创建/关闭窗口后资源状态稳定；
- `GetClientRect` 保证客户区覆盖不依赖固定像素尺寸；
- helper 不包含隐藏、透明、延迟显示、数据库、网络或业务服务依赖；
- 原 1720 项测试必须全部保留，新增测试必须全部通过，`failed=0`、`skipped=0`。

## 用户实机验收步骤

在后续隔离测试包中逐一打开五个窗口，重点观察首次打开和连续关闭再打开：

1. 首帧和空数据状态保持深色，不出现白色客户区；
2. 最大化、还原和 DPI 缩放时客户区保持完整深色；
3. 标题栏、窗口按钮、拖动和缩放行为不变；
4. 数据加载、绑定、刷新、Owner、Show 方式和单实例规则不变；
5. 关闭后再次打开无资源异常或持续增长。

用户实机验收通过前，不进行 commit、push、tag 或 publish。
