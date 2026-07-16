# TASK-RELEASE-STATIC-VALIDATION-029：正式发布脚本安全静态验证模式

## 任务范围

V8.10.3 仅增强 `scripts/Publish-CrossEtfRelease.ps1`、发布脚本测试、版本元数据和发布说明，不修改应用业务代码、数据库代码、窗口、行情、TradeLog、账户回放、策略或委托。

原脚本在一次发布中存在两条应用启动路径：直接启动正式 `跨境ETF.exe`，以及安装桌面快捷方式后再次通过快捷方式启动。应用启动会使用当前 Windows 用户的生产 LocalAppData，可能初始化、备份或刷新数据库派生状态，也可能触发真实行情请求。该风险属于发布流程，不是业务功能故障。

## ValidationMode

脚本参数末尾新增：

```powershell
[ValidateSet("Launch", "Static")]
[string]$ValidationMode = "Launch"
```

- `Launch` 为默认值，执行全部静态校验，并保留原有直接 EXE 启动与可选快捷方式启动验证。
- `Static` 必须显式指定，执行完整 publish 和全部静态校验，但不启动任何应用 EXE，不通过快捷方式启动，也不调用应用关闭流程。
- 非法值由 PowerShell 参数绑定直接拒绝。
- 不通过环境变量、CI 标志、机器名或用户账号自动切换模式。
- Launch 失败不得自动降级为 Static。
- 两种模式共用唯一一套 publish 参数和静态验证逻辑。

## 正式发布来源

两种模式都要求：

1. `SourcePath` 是 Git worktree。
2. 当前为 clean detached HEAD，暂存区和未跟踪文件均为空。
3. HEAD 等于完整 40 位 `ExpectedCommit`。
4. HEAD 的 exact tag 为 `v<Version>`。
5. 本地标签是 annotated tag，peeled commit 等于 `ExpectedCommit`。
6. origin 存在同名 annotated tag，远端 tag object 与本地一致，peeled commit 等于 `ExpectedCommit`。
7. `Version` 必须为三段式版本号。

自动测试使用临时 Git 仓库和本地 bare origin，不访问 GitHub 或其它网络源。

## Staging 与正式目录状态机

- publish 只写 `<OutputRoot>\.__staging_v<Version>_<GUID>`。
- staging 与正式目标位于同一卷。
- AppHost 在 staging 内重命名为 `跨境ETF.exe`。
- 所有静态检查必须在 staging 完成后才能提升。
- 目标不存在时使用同卷 `Directory.Move` 原子形成 `<OutputRoot>\v<Version>`，不逐文件复制。
- 目标已存在且 manifest hash 相同，视为幂等成功并清理 staging。
- 目标已存在但 manifest hash 不同，拒绝覆盖，保持旧目录不变并保留 staging 供调查。
- 发布失败不得影响其它历史版本目录，清理失败必须报告残留路径。

## 静态发布包校验

两种模式共同校验：

- 中文用户 EXE 严格命名为 `跨境ETF.exe`，英文 AppHost 不得残留。
- 主 DLL、deps、runtimeconfig、Microsoft.Data.Sqlite、SQLitePCLRaw 和 native SQLite 依赖存在且非空。
- EXE `FileVersion` 等于 `<Version>.0`。
- EXE 和主 DLL `ProductVersion/InformationalVersion` 等于 `<Version>+<ExpectedCommit完整SHA>`。
- 主 DLL `AssemblyName` 保持 `CrossETF.Terminal.UiShell.Reference`。
- 正式包递归禁止数据库、WAL/SHM、测试程序集、TestResults、testhost、覆盖率、日志、临时文件、快捷方式、用户配置副本、备份、故障注入配置和 PDB。
- 输出 `跨境ETF.exe` SHA-256。
- 生成确定性目录 manifest：标准化相对路径、文件长度、逐文件 SHA-256，按 Ordinal 排序后计算 manifest SHA-256。
- manifest 不包含绝对路径、时间戳或 staging GUID；相同内容在不同根目录得到相同 hash。

V8.10.3 起，正式包禁止 PDB；Launch 与 Static 使用相同的无 PDB publish 参数。

## Launch 兼容行为

默认 Launch 保留原有行为：

1. 静态校验全部通过后启动正式 EXE。
2. 15 秒内发现目标进程。
3. 运行约 8 秒并检查主窗口句柄。
4. 调用 `CloseMainWindow`，最多等待 15 秒正常退出。
5. 显式指定 `CreateDesktopShortcut` 时安装快捷方式，并再次通过快捷方式启动同一 EXE 验证。

脚本不使用 `Kill` 或 `Stop-Process` 强制终止应用。

## 快捷方式状态机

- `CreateDesktopShortcut` 未指定时不创建快捷方式；Launch 和 Static 均可显式指定。
- 临时 `.lnk` 与正式 `.lnk` 位于同一目录。
- 临时快捷方式先校验 `TargetPath`、`WorkingDirectory`、`IconLocation`、`Description` 和目标 EXE 存在性。
- 记录旧快捷方式 SHA-256 和属性后，使用同目录 `File.Replace` 原子替换；原快捷方式不存在时使用同目录 `File.Move`。
- 替换后重新读取正式快捷方式并复核属性。
- 验证失败时使用原子替换恢复旧快捷方式，并复核旧 hash 和属性。
- 恢复失败时保留备份路径作为证据，并避免留下指向错误目标的正式快捷方式。
- 所有 WScript.Shell COM 对象均在 `finally` 中释放。
- Static 可静态创建和安装快捷方式，但绝不通过快捷方式启动应用。

## 自动测试隔离

发布脚本测试只使用：

- 临时目录；
- 临时 Git 仓库和本地 bare origin；
- 临时伪发布包；
- 临时 OutputRoot；
- 临时目录中的快捷方式。

自动测试不启动 `跨境ETF.exe` 或伪业务 EXE，不访问真实桌面，不写正式 `artifacts\release`，不访问生产数据库，不请求网络，不修改 Git 配置。快捷方式 COM 测试在 Windows 开发机执行，最终要求 0 skipped。

覆盖范围包括参数绑定、Static 启动不可达、Launch 结构回归、来源校验、核心文件和版本、污染拒绝、manifest 确定性、目录幂等/冲突以及快捷方式原子替换/恢复。

## 命令示例

```powershell
# Launch：默认完整发布并执行应用启动验证
.\scripts\Publish-CrossEtfRelease.ps1 `
  -Version 8.10.3 `
  -SourcePath D:\release-worktree `
  -ExpectedCommit <40位SHA> `
  -ValidationMode Launch `
  -CreateDesktopShortcut

# Static：完整发布与静态校验，不启动任何应用 EXE
.\scripts\Publish-CrossEtfRelease.ps1 `
  -Version 8.10.3 `
  -SourcePath D:\release-worktree `
  -ExpectedCommit <40位SHA> `
  -ValidationMode Static
```

Static 只有在显式指定 `CreateDesktopShortcut` 时才会安装快捷方式；即使安装，也不会启动快捷方式。

## V8.10.3 人工验收

本轮开发完成后只审阅代码、测试和隔离验证结果，不创建 v8.10.3 tag，不执行正式 publish，不修改真实桌面快捷方式，不启动任何应用 EXE。

人工验收应确认：

1. `ValidationMode` 位于参数末尾，默认 Launch，Static 必须显式指定。
2. Static 在任何 `Start-Process`、`Test-ApplicationLaunch`、`CloseMainWindow` 或快捷方式启动前完成分支，不存在自动降级。
3. Launch 的 15 秒启动、8 秒运行、主窗口检查、`CloseMainWindow` 和 15 秒退出等待保持不变。
4. 两种模式共用来源、publish、版本、程序集、污染、hash 和 manifest 校验。
5. staging 提升、同版本幂等和内容冲突拒绝不会覆盖历史正式目录。
6. 快捷方式安装前后均校验，失败时可恢复原快捷方式并保留恢复证据。
7. 全部测试通过且 0 skipped，测试未产生生产数据库、正式 artifacts、桌面快捷方式或应用进程副作用。

用户验收后才能另行执行提交、推送、标签和正式发布流程。
