[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [Alias("WorktreePath")]
    [string]$SourcePath,

    [string]$OutputRoot = "D:\shibaowangETF\artifacts\release",

    [switch]$CreateDesktopShortcut,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$originalExeName = "CrossETF.Terminal.UiShell.Reference.exe"
$releaseExeName = "跨境ETF.exe"
$shortcutName = "跨境ETF.lnk"
$projectName = "CrossETF.Terminal.UiShell.Reference.csproj"
$activeTestProcess = $null
$targetDirectory = $null
$targetBackupDirectory = $null
$desktopShell = $null
$temporaryShortcutPath = $null
$shortcutBackupPath = $null
$installedShortcutPath = $null
$hadExistingShortcut = $false
$shortcutWasReplaced = $false
$releaseSucceeded = $false

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-SamePath {
    param(
        [Parameter(Mandatory = $true)][string]$First,
        [Parameter(Mandatory = $true)][string]$Second)

    return [string]::Equals(
        (Get-FullPath $First),
        (Get-FullPath $Second),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-ProcessByExecutablePath {
    param([Parameter(Mandatory = $true)][string]$ExecutablePath)

    $expected = Get-FullPath $ExecutablePath
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
        try {
            if ($process.Path -and (Test-SamePath $process.Path $expected)) {
                return $process
            }
        }
        catch {
            # Some system processes do not expose Path to a standard user.
        }
    }

    return $null
}

function Test-ApplicationLaunch {
    param(
        [Parameter(Mandatory = $true)][string]$LaunchPath,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutable,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$Label)

    if (Get-ProcessByExecutablePath $ExpectedExecutable) {
        throw "$Label 启动验证前已存在同路径进程，拒绝关闭用户进程。"
    }

    Start-Process -FilePath $LaunchPath -WorkingDirectory $WorkingDirectory
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $script:activeTestProcess = Get-ProcessByExecutablePath $ExpectedExecutable
    }
    while ($null -eq $script:activeTestProcess -and [DateTime]::UtcNow -lt $deadline)

    if ($null -eq $script:activeTestProcess) {
        throw "$Label 未启动目标程序：$ExpectedExecutable"
    }

    $processId = $script:activeTestProcess.Id
    Start-Sleep -Seconds 8
    $script:activeTestProcess.Refresh()
    if ($script:activeTestProcess.HasExited) {
        throw "$Label 启动后 8 秒内异常退出。"
    }

    $handleDeadline = [DateTime]::UtcNow.AddSeconds(8)
    while ($script:activeTestProcess.MainWindowHandle -eq [IntPtr]::Zero -and
           [DateTime]::UtcNow -lt $handleDeadline) {
        Start-Sleep -Milliseconds 250
        $script:activeTestProcess.Refresh()
    }

    if (-not $script:activeTestProcess.CloseMainWindow()) {
        throw "$Label 无法通过 CloseMainWindow 正常关闭，PID=$processId。"
    }

    if (-not $script:activeTestProcess.WaitForExit(15000)) {
        throw "$Label 在收到正常关闭请求后 15 秒仍未退出，PID=$processId。"
    }

    $script:activeTestProcess = $null
    return $processId
}

function Assert-Shortcut {
    param(
        [Parameter(Mandatory = $true)]$Shell,
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$ExpectedTarget,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ExpectedIconLocation,
        [Parameter(Mandatory = $true)][string]$ExpectedDescription)

    if (-not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
        throw "快捷方式不存在：$ShortcutPath"
    }

    $shortcut = $Shell.CreateShortcut($ShortcutPath)
    try {
        if (-not (Test-SamePath $shortcut.TargetPath $ExpectedTarget)) {
            throw "快捷方式 TargetPath 不正确：$($shortcut.TargetPath)"
        }

        if (-not (Test-SamePath $shortcut.WorkingDirectory $ExpectedWorkingDirectory)) {
            throw "快捷方式 WorkingDirectory 不正确：$($shortcut.WorkingDirectory)"
        }

        if (-not [string]::Equals(
                $shortcut.IconLocation,
                $ExpectedIconLocation,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "快捷方式 IconLocation 不正确：$($shortcut.IconLocation)"
        }

        if (-not [string]::Equals($shortcut.Description, $ExpectedDescription, [System.StringComparison]::Ordinal)) {
            throw "快捷方式 Description 不正确：$($shortcut.Description)"
        }

        if (-not (Test-Path -LiteralPath $shortcut.TargetPath -PathType Leaf)) {
            throw "快捷方式目标不存在：$($shortcut.TargetPath)"
        }
    }
    finally {
        if ($null -ne $shortcut) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }
    }
}

try {
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
        throw "SourcePath 不存在：$SourcePath"
    }

    $sourceDirectory = Get-FullPath $SourcePath
    $commitOutput = @(& git -C $sourceDirectory rev-parse HEAD 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "无法读取 SourcePath commit：$($commitOutput -join [Environment]::NewLine)"
    }

    $actualCommit = ([string]($commitOutput | Select-Object -Last 1)).Trim()
    if (-not [string]::Equals($actualCommit, $ExpectedCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SourcePath commit 不匹配。Expected=$ExpectedCommit; Actual=$actualCommit"
    }

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version 格式无效，应为 x.y.z：$Version"
    }

    if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "ExpectedCommit 必须是完整 40 位 commit hash。"
    }

    $projectPath = Join-Path $sourceDirectory $projectName
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "发布项目不存在：$projectPath"
    }

    $outputRootDirectory = Get-FullPath $OutputRoot
    [void](New-Item -ItemType Directory -Path $outputRootDirectory -Force)
    $targetDirectory = Get-FullPath (Join-Path $outputRootDirectory ("v" + $Version))
    $requiredPrefix = $outputRootDirectory + [System.IO.Path]::DirectorySeparatorChar
    if (-not $targetDirectory.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "目标发布目录越界：$targetDirectory"
    }

    if (Test-Path -LiteralPath $targetDirectory) {
        $targetBackupDirectory = $targetDirectory + ".__backup_" + [Guid]::NewGuid().ToString("N")
        Move-Item -LiteralPath $targetDirectory -Destination $targetBackupDirectory
    }

    & dotnet publish $projectPath -c Release -r win-x64 --self-contained false -o $targetDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败，退出码：$LASTEXITCODE"
    }

    $originalExePath = Join-Path $targetDirectory $originalExeName
    $releaseExePath = Join-Path $targetDirectory $releaseExeName
    if (-not (Test-Path -LiteralPath $originalExePath -PathType Leaf)) {
        throw "发布后未找到原始 EXE：$originalExePath"
    }

    Rename-Item -LiteralPath $originalExePath -NewName $releaseExeName
    if (-not (Test-Path -LiteralPath $releaseExePath -PathType Leaf)) {
        throw "中文发布 EXE 不存在：$releaseExePath"
    }

    if (Test-Path -LiteralPath $originalExePath) {
        throw "正式目录仍保留旧英文名称 EXE：$originalExePath"
    }

    $versionInfo = (Get-Item -LiteralPath $releaseExePath).VersionInfo
    $expectedFileVersion = $Version + ".0"
    if (-not [string]::Equals($versionInfo.FileVersion, $expectedFileVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "FileVersion 不匹配。Expected=$expectedFileVersion; Actual=$($versionInfo.FileVersion)"
    }

    if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion) -or
        $versionInfo.ProductVersion.IndexOf($ExpectedCommit, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "ProductVersion 未包含最终 commit。Actual=$($versionInfo.ProductVersion)"
    }

    $directLaunchPid = Test-ApplicationLaunch `
        -LaunchPath $releaseExePath `
        -ExpectedExecutable $releaseExePath `
        -WorkingDirectory $targetDirectory `
        -Label "正式 EXE"

    $shortcutLaunchPid = $null
    if ($CreateDesktopShortcut) {
        $desktopShell = New-Object -ComObject WScript.Shell
        $desktopDirectory = [string]$desktopShell.SpecialFolders.Item("Desktop")
        if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
            -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
            throw "无法解析 Windows 当前用户桌面特殊目录。"
        }

        $installedShortcutPath = Join-Path $desktopDirectory $shortcutName
        $temporaryShortcutPath = Join-Path $desktopDirectory ("跨境ETF." + [Guid]::NewGuid().ToString("N") + ".tmp.lnk")
        $shortcutBackupPath = Join-Path $desktopDirectory ("跨境ETF." + [Guid]::NewGuid().ToString("N") + ".backup.lnk")
        $iconLocation = $releaseExePath + ",0"
        $description = "跨境ETF智能投资决策系统 V" + $Version

        $temporaryShortcut = $desktopShell.CreateShortcut($temporaryShortcutPath)
        try {
            $temporaryShortcut.TargetPath = $releaseExePath
            $temporaryShortcut.WorkingDirectory = $targetDirectory
            $temporaryShortcut.IconLocation = $iconLocation
            $temporaryShortcut.Description = $description
            $temporaryShortcut.Save()
        }
        finally {
            if ($null -ne $temporaryShortcut) {
                [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($temporaryShortcut)
            }
        }

        Assert-Shortcut $desktopShell $temporaryShortcutPath $releaseExePath $targetDirectory $iconLocation $description
        $hadExistingShortcut = Test-Path -LiteralPath $installedShortcutPath -PathType Leaf
        if ($hadExistingShortcut) {
            [System.IO.File]::Replace($temporaryShortcutPath, $installedShortcutPath, $shortcutBackupPath, $true)
        }
        else {
            Move-Item -LiteralPath $temporaryShortcutPath -Destination $installedShortcutPath
        }

        $shortcutWasReplaced = $true
        Assert-Shortcut $desktopShell $installedShortcutPath $releaseExePath $targetDirectory $iconLocation $description
        $shortcutLaunchPid = Test-ApplicationLaunch `
            -LaunchPath $installedShortcutPath `
            -ExpectedExecutable $releaseExePath `
            -WorkingDirectory $targetDirectory `
            -Label "桌面快捷方式"

        if (Test-Path -LiteralPath $shortcutBackupPath) {
            Remove-Item -LiteralPath $shortcutBackupPath -Force
        }
    }

    if ($targetBackupDirectory -and (Test-Path -LiteralPath $targetBackupDirectory)) {
        Remove-Item -LiteralPath $targetBackupDirectory -Recurse -Force
    }

    $releaseSucceeded = $true
    Write-Host "发布成功"
    Write-Host "Version=$Version"
    Write-Host "Commit=$actualCommit"
    Write-Host "OutputDirectory=$targetDirectory"
    Write-Host "Executable=$releaseExePath"
    Write-Host "FileVersion=$($versionInfo.FileVersion)"
    Write-Host "ProductVersion=$($versionInfo.ProductVersion)"
    Write-Host "DirectLaunchPid=$directLaunchPid"
    if ($CreateDesktopShortcut) {
        Write-Host "DesktopShortcut=$installedShortcutPath"
        Write-Host "ShortcutLaunchPid=$shortcutLaunchPid"
    }
}
catch {
    if ($null -ne $activeTestProcess) {
        try {
            $activeTestProcess.Refresh()
            if (-not $activeTestProcess.HasExited) {
                [void]$activeTestProcess.CloseMainWindow()
                [void]$activeTestProcess.WaitForExit(5000)
            }
        }
        catch {
            Write-Warning "测试进程无法正常关闭，未使用 Kill。PID=$($activeTestProcess.Id)"
        }
    }

    if ($shortcutWasReplaced) {
        try {
            if ($hadExistingShortcut -and (Test-Path -LiteralPath $shortcutBackupPath -PathType Leaf)) {
                Copy-Item -LiteralPath $shortcutBackupPath -Destination $installedShortcutPath -Force
            }
            elseif (-not $hadExistingShortcut -and (Test-Path -LiteralPath $installedShortcutPath -PathType Leaf)) {
                Remove-Item -LiteralPath $installedShortcutPath -Force
            }
        }
        catch {
            Write-Warning "恢复原桌面快捷方式失败：$($_.Exception.Message)"
        }
    }

    if ($temporaryShortcutPath -and (Test-Path -LiteralPath $temporaryShortcutPath)) {
        Remove-Item -LiteralPath $temporaryShortcutPath -Force -ErrorAction SilentlyContinue
    }

    if ($shortcutBackupPath -and (Test-Path -LiteralPath $shortcutBackupPath)) {
        Remove-Item -LiteralPath $shortcutBackupPath -Force -ErrorAction SilentlyContinue
    }

    if ($targetDirectory -and (Test-Path -LiteralPath $targetDirectory)) {
        Remove-Item -LiteralPath $targetDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($targetBackupDirectory -and (Test-Path -LiteralPath $targetBackupDirectory)) {
        Move-Item -LiteralPath $targetBackupDirectory -Destination $targetDirectory -ErrorAction SilentlyContinue
    }

    Write-Error "发布失败：$($_.Exception.Message)"
    exit 1
}
finally {
    if ($null -ne $desktopShell) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($desktopShell)
    }

    if (-not $releaseSucceeded) {
        Write-Verbose "发布未完成。"
    }
}
