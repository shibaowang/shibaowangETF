# TASK-INSTALLER-PACKAGING-031：V8.10.5 Windows 安装包

## 目标与来源

- 应用版本：V8.10.5。
- 应用代码必须来自 `v8.10.5` annotated tag 对应的 clean detached worktree。
- 标签 commit：`b372c199dc53a89fe2bae004887fcbe92dd3913c`。
- 最终安装包：`artifacts/installer/v8.10.5/跨境ETF安装程序_v8.10.5_win-x64.exe`。

## 技术口径

- 安装器：Inno Setup 6，简体中文，Windows x64。
- 简体中文消息资源固定取自 Inno Setup 官方 `jrsoftware/issrc` 仓库提交 `c495623a97376d524f298b1b160e8fd612375c62` 的 `Files/Languages/ChineseSimplified.isl`，文件 SHA-256 为 `6753BE2C5E2740D859900FD902824DB2EC568DA5C5B52486524C9762D778B0B0`，随安装器配置保存，构建时不从网络下载。
- 安装范围：当前用户，`PrivilegesRequired=lowest`。
- 默认目录：`%LocalAppData%\Programs\CrossETF`。
- 发布方式：.NET 8 win-x64 self-contained、非单文件、不裁剪、无 PDB，不要求目标机器预装 .NET Desktop Runtime。
- 固定 AppId：`{C1935940-49E2-4F33-BAF2-70E991F37959}`，后续覆盖升级必须继续使用该值。
- 正式主程序名称：`跨境ETF.exe`。
- 安装器创建开始菜单快捷方式，并提供默认勾选的可选桌面快捷方式。
- 安装完成页可以运行应用，但静默安装使用 `skipifsilent`，不会自动启动。
- 安装或升级时使用 Inno Setup Restart Manager 请求应用正常关闭，不使用 Kill 或 `taskkill`。

## 用户数据边界

安装包只包含经过污染检查的 self-contained publish 文件，不包含数据库、WAL/SHM、日志、测试程序集、PDB、临时文件、用户配置或快捷方式文件。

卸载只移除安装目录、安装器创建的快捷方式和卸载注册项，不定义 `[UninstallDelete]`，不删除或迁移：

`%LocalAppData%\CrossETF.Terminal.UiShell.Reference`

因此数据库、TradeLog、账户回放、持仓、策略、委托、行情缓存和其他用户数据继续使用应用既有路径与口径。

## 构建脚本安全门

`scripts/Build-CrossEtfInstaller.ps1` 必须验证：

1. Version 和 40 位 ExpectedCommit；
2. SourcePath 是 clean detached Git worktree；
3. HEAD 等于 ExpectedCommit；
4. exact tag、annotated tag、本地 tag object/peeled commit 和 origin tag 全部一致；
5. self-contained publish 的核心 WPF、.NET、SQLite 文件齐全；
6. publish 不含数据库、日志、PDB、测试或用户文件；
7. 安装器先在同卷 staging 生成，校验成功后再原子提升；
8. 同版本同 hash 可幂等成功，不同 hash 必须拒绝覆盖并保留 staging 调查；
9. 输出 `SHA256SUMS.txt`。

## 业务边界

本任务不修改应用版本、业务源码、UI、TradeLog、账户/持仓回放、行情、刷新频率、策略、委托、预警、图表、数据库结构、网络接口或用户配置口径，也不加入注册、登录、会员、支付、自动更新、遥测、广告或自动交易。

## 签名说明

本任务不创建自签名证书，不修改系统证书存储，也不关闭 Windows 安全功能。安装包当前没有可信数字签名，Windows SmartScreen 可能提示未知发布者；大范围分发前应另行采购并配置可信代码签名证书。

## 验证要求

- restore、Release build 和全部自动测试通过；
- 从 `v8.10.5` detached worktree 构建 self-contained publish 和安装包；
- 静默安装后验证安装目录、开始菜单和桌面快捷方式；
- 正常启动并保持至少 15 秒，再通过 `CloseMainWindow` 正常关闭，禁止 Kill；
- 验证同版本覆盖安装；
- 正常卸载后安装目录和安装器快捷方式消失，用户数据库保持不变；
- 重装后应用继续识别既有用户数据；
- 若未完成无 .NET 的 Windows Sandbox/独立机器测试，必须如实标记为未完成。
