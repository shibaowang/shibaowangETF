<#
.SYNOPSIS
Publishes a tagged CrossETF release with static or launch validation.

.PARAMETER ValidationMode
Launch runs all static checks and the existing application startup validations.
Static performs the complete publish and static checks without starting any
published application executable or launching the desktop shortcut.
#>
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
    [string]$ExpectedCommit,

    [ValidateSet("Launch", "Static")]
    [string]$ValidationMode = "Launch"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:OriginalExeName = "CrossETF.Terminal.UiShell.Reference.exe"
$script:ReleaseExeName = "跨境ETF.exe"
$script:ManagedAssemblyName = "CrossETF.Terminal.UiShell.Reference"
$script:ShortcutName = "跨境ETF.lnk"
$script:ProjectName = "CrossETF.Terminal.UiShell.Reference.csproj"
$script:ActiveTestProcess = $null

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

function Invoke-GitCommand {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = @(& git -C $RepositoryPath @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "Git 命令失败：git -C `"$RepositoryPath`" $($Arguments -join ' ')；ExitCode=$exitCode；Output=$($output -join [Environment]::NewLine)"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Assert-ReleaseSource {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit)

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version 格式无效，应为 x.y.z：$Version"
    }

    if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "ExpectedCommit 必须是完整 40 位 commit hash：$ExpectedCommit"
    }

    if (-not (Test-Path -LiteralPath $RepositoryPath -PathType Container)) {
        throw "SourcePath 不存在：$RepositoryPath"
    }

    $sourceDirectory = Get-FullPath $RepositoryPath
    $insideWorkTree = Invoke-GitCommand $sourceDirectory @("rev-parse", "--is-inside-work-tree")
    if (($insideWorkTree.Output | Select-Object -Last 1).Trim() -ne "true") {
        throw "SourcePath 不是 Git worktree：$sourceDirectory"
    }

    $headResult = Invoke-GitCommand $sourceDirectory @("rev-parse", "HEAD")
    $actualCommit = ([string]($headResult.Output | Select-Object -Last 1)).Trim()
    if (-not [string]::Equals($actualCommit, $ExpectedCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SourcePath commit 不匹配。Expected=$ExpectedCommit; Actual=$actualCommit"
    }

    $branchResult = Invoke-GitCommand $sourceDirectory @("rev-parse", "--abbrev-ref", "HEAD")
    $branchName = ([string]($branchResult.Output | Select-Object -Last 1)).Trim()
    if ($branchName -ne "HEAD") {
        throw "正式发布必须来自 detached HEAD，当前分支：$branchName"
    }

    $statusResult = Invoke-GitCommand $sourceDirectory @("status", "--porcelain=v1", "--untracked-files=all")
    $dirtyLines = @($statusResult.Output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($dirtyLines.Count -ne 0) {
        throw "正式发布 worktree 不干净：$($dirtyLines -join '; ')"
    }

    $tagName = "v$Version"
    $exactTagResult = Invoke-GitCommand $sourceDirectory @("describe", "--tags", "--exact-match", "HEAD") -AllowFailure
    if ($exactTagResult.ExitCode -ne 0) {
        throw "HEAD 缺少发布版本的 exact tag。Expected=$tagName; Output=$($exactTagResult.Output -join [Environment]::NewLine)"
    }

    $exactTag = ([string]($exactTagResult.Output | Select-Object -Last 1)).Trim()
    if (-not [string]::Equals($exactTag, $tagName, [System.StringComparison]::Ordinal)) {
        throw "HEAD 的 exact tag 不匹配。Expected=$tagName; Actual=$exactTag"
    }

    $tagTypeResult = Invoke-GitCommand $sourceDirectory @("cat-file", "-t", "refs/tags/$tagName")
    $tagType = ([string]($tagTypeResult.Output | Select-Object -Last 1)).Trim()
    if ($tagType -ne "tag") {
        throw "正式发布标签必须是 annotated tag：$tagName；ActualType=$tagType"
    }

    $tagCommitResult = Invoke-GitCommand $sourceDirectory @("rev-list", "-n", "1", "refs/tags/$tagName")
    $tagCommit = ([string]($tagCommitResult.Output | Select-Object -Last 1)).Trim()
    if (-not [string]::Equals($tagCommit, $ExpectedCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "本地标签目标不匹配。Tag=$tagName; Expected=$ExpectedCommit; Actual=$tagCommit"
    }

    $localTagObjectResult = Invoke-GitCommand $sourceDirectory @("rev-parse", "refs/tags/$tagName")
    $localTagObject = ([string]($localTagObjectResult.Output | Select-Object -Last 1)).Trim()
    [void](Invoke-GitCommand $sourceDirectory @("remote", "get-url", "origin"))
    $remoteResult = Invoke-GitCommand $sourceDirectory @(
        "ls-remote",
        "--tags",
        "origin",
        "refs/tags/$tagName",
        "refs/tags/$tagName^{}")

    $remoteRefs = @{}
    foreach ($line in $remoteResult.Output) {
        $parts = @(([string]$line) -split '\s+')
        if ($parts.Count -ge 2) {
            $remoteRefs[$parts[1]] = $parts[0]
        }
    }

    $remoteTagRef = "refs/tags/$tagName"
    $remotePeeledRef = "$remoteTagRef^{}"
    if (-not $remoteRefs.ContainsKey($remoteTagRef) -or -not $remoteRefs.ContainsKey($remotePeeledRef)) {
        throw "origin 缺少完整 annotated tag 引用：$tagName"
    }

    if (-not [string]::Equals($remoteRefs[$remoteTagRef], $localTagObject, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "origin 标签对象与本地标签对象不一致：$tagName"
    }

    if (-not [string]::Equals($remoteRefs[$remotePeeledRef], $ExpectedCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "origin 标签目标不匹配。Tag=$tagName; Expected=$ExpectedCommit; Actual=$($remoteRefs[$remotePeeledRef])"
    }

    return [pscustomobject]@{
        SourceDirectory = $sourceDirectory
        Commit = $actualCommit
        TagName = $tagName
        TagObject = $localTagObject
    }
}

function Get-ReleasePublishArguments {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit)

    return @(
        "publish",
        $ProjectPath,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "false",
        "-o", $StagingDirectory,
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0",
        "-p:InformationalVersion=$Version+$ExpectedCommit",
        "-p:IncludeSourceRevisionInInformationalVersion=false",
        "-p:DebugType=None",
        "-p:DebugSymbols=false")
}

function Invoke-ReleasePublish {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit)

    $arguments = Get-ReleasePublishArguments $ProjectPath $StagingDirectory $Version $ExpectedCommit
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败，ExitCode=$LASTEXITCODE；Staging=$StagingDirectory"
    }
}

function Rename-ReleaseAppHost {
    param([Parameter(Mandatory = $true)][string]$DirectoryPath)

    $originalExePath = Join-Path $DirectoryPath $script:OriginalExeName
    $releaseExePath = Join-Path $DirectoryPath $script:ReleaseExeName
    if (-not (Test-Path -LiteralPath $originalExePath -PathType Leaf)) {
        throw "发布后未找到原始 EXE：$originalExePath"
    }

    if (Test-Path -LiteralPath $releaseExePath) {
        throw "重命名前已存在中文 EXE：$releaseExePath"
    }

    Rename-Item -LiteralPath $originalExePath -NewName $script:ReleaseExeName
    if (-not (Test-Path -LiteralPath $releaseExePath -PathType Leaf)) {
        throw "中文发布 EXE 不存在：$releaseExePath"
    }

    if (Test-Path -LiteralPath $originalExePath) {
        throw "正式目录仍保留旧英文名称 EXE：$originalExePath"
    }

    return $releaseExePath
}

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$FilePath)

    $root = Get-FullPath $RootPath
    $file = Get-FullPath $FilePath
    $prefix = $root + [System.IO.Path]::DirectorySeparatorChar
    if (-not $file.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "文件不在发布目录内：Root=$root; File=$file"
    }

    return $file.Substring($prefix.Length).Replace('\', '/')
}

function Get-ReleaseManifest {
    param([Parameter(Mandatory = $true)][string]$DirectoryPath)

    $directory = Get-FullPath $DirectoryPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "发布目录不存在：$directory"
    }

    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File -Force) {
        $relativePath = Get-NormalizedRelativePath $directory $file.FullName
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        $entries.Add("$relativePath`t$($file.Length)`t$hash")
    }

    $entries.Sort([System.StringComparer]::Ordinal)
    return $entries.ToArray()
}

function Get-ReleaseManifestHash {
    param([Parameter(Mandatory = $true)][string]$DirectoryPath)

    $manifest = @(Get-ReleaseManifest $DirectoryPath)
    $manifestText = [string]::Join("`n", $manifest)
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($manifestText)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
    }
}

function Assert-PackagePollution {
    param([Parameter(Mandatory = $true)][string]$DirectoryPath)

    $directory = Get-FullPath $DirectoryPath
    $pollution = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File -Force) {
        $relativePath = Get-NormalizedRelativePath $directory $file.FullName
        $normalized = $relativePath.ToLowerInvariant()
        $name = $file.Name.ToLowerInvariant()
        $segments = @($normalized -split '/')
        $isPollution =
            $name -match '\.(db|sqlite|sqlite3)(-wal|-shm)?$' -or
            $name -match '\.(pdb|trx|coverage|coveragexml|log|tmp|temp|lnk|bak|backup|user)$' -or
            $name -match '^testhost' -or
            $name -match '\.tests\.dll$' -or
            $name -match '^coverage' -or
            $segments -contains 'tests' -or
            $segments -contains 'testresults' -or
            $normalized -match 'fault[-_. ]?injection|failure[-_. ]?injection|testsettings|\.runsettings$' -or
            $normalized -match '(^|/)(user\.config|settings\.local\.json|appsettings\..*\.local\.json)$'

        if ($isPollution) {
            $pollution.Add($relativePath)
        }
    }

    if ($pollution.Count -ne 0) {
        $items = [string]::Join([Environment]::NewLine, $pollution.ToArray())
        throw "发布包包含禁止文件：$([Environment]::NewLine)$items"
    }
}

function Assert-PublishedPackage {
    param(
        [Parameter(Mandatory = $true)][string]$DirectoryPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit)

    $directory = Get-FullPath $DirectoryPath
    $requiredFiles = @(
        $script:ReleaseExeName,
        "$($script:ManagedAssemblyName).dll",
        "$($script:ManagedAssemblyName).deps.json",
        "$($script:ManagedAssemblyName).runtimeconfig.json",
        "Microsoft.Data.Sqlite.dll",
        "SQLitePCLRaw.batteries_v2.dll",
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "e_sqlite3.dll")

    foreach ($relativePath in $requiredFiles) {
        $path = Join-Path $directory $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "发布包缺少核心文件：$relativePath；Directory=$directory"
        }

        if ((Get-Item -LiteralPath $path).Length -le 0) {
            throw "发布包核心文件为空：$relativePath；Directory=$directory"
        }
    }

    $englishAppHost = Join-Path $directory $script:OriginalExeName
    if (Test-Path -LiteralPath $englishAppHost) {
        throw "正式目录仍保留旧英文名称 EXE：$englishAppHost"
    }

    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File -Force) {
        if ($file.Length -le 0) {
            throw "发布包包含空文件：$(Get-NormalizedRelativePath $directory $file.FullName)"
        }
    }

    Assert-PackagePollution $directory

    $releaseExePath = Join-Path $directory $script:ReleaseExeName
    $managedDllPath = Join-Path $directory "$($script:ManagedAssemblyName).dll"
    $expectedFileVersion = "$Version.0"
    $expectedProductVersion = "$Version+$ExpectedCommit"
    $exeVersionInfo = (Get-Item -LiteralPath $releaseExePath).VersionInfo
    if (-not [string]::Equals($exeVersionInfo.FileVersion, $expectedFileVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "FileVersion 不匹配。Expected=$expectedFileVersion; Actual=$($exeVersionInfo.FileVersion)"
    }

    if (-not [string]::Equals($exeVersionInfo.ProductVersion, $expectedProductVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "ProductVersion 不匹配。Expected=$expectedProductVersion; Actual=$($exeVersionInfo.ProductVersion)"
    }

    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($managedDllPath).Name
    if (-not [string]::Equals($assemblyName, $script:ManagedAssemblyName, [System.StringComparison]::Ordinal)) {
        throw "AssemblyName 不匹配。Expected=$($script:ManagedAssemblyName); Actual=$assemblyName"
    }

    $managedVersionInfo = (Get-Item -LiteralPath $managedDllPath).VersionInfo
    if (-not [string]::Equals($managedVersionInfo.ProductVersion, $expectedProductVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Managed Assembly InformationalVersion 不匹配。Expected=$expectedProductVersion; Actual=$($managedVersionInfo.ProductVersion)"
    }

    return [pscustomobject]@{
        ExecutablePath = $releaseExePath
        FileVersion = $exeVersionInfo.FileVersion
        ProductVersion = $exeVersionInfo.ProductVersion
        AssemblyName = $assemblyName
        ExecutableSha256 = (Get-FileHash -LiteralPath $releaseExePath -Algorithm SHA256).Hash.ToUpperInvariant()
        ManifestHash = Get-ReleaseManifestHash $directory
    }
}

function Promote-ReleaseDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$TargetDirectory)

    $staging = Get-FullPath $StagingDirectory
    $target = Get-FullPath $TargetDirectory
    if (-not (Test-Path -LiteralPath $staging -PathType Container)) {
        throw "staging 目录不存在：$staging"
    }

    $stagingRoot = [System.IO.Path]::GetPathRoot($staging)
    $targetRoot = [System.IO.Path]::GetPathRoot($target)
    if (-not [string]::Equals($stagingRoot, $targetRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "staging 与正式目录不在同一卷：Staging=$staging; Target=$target"
    }

    $stagingHash = Get-ReleaseManifestHash $staging
    if (Test-Path -LiteralPath $target) {
        if (-not (Test-Path -LiteralPath $target -PathType Container)) {
            throw "正式目标路径已存在但不是目录：$target"
        }

        $targetHash = Get-ReleaseManifestHash $target
        if (-not [string]::Equals($stagingHash, $targetHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            $exception = [System.InvalidOperationException]::new(
                "同版本正式目录已存在但内容不同，拒绝覆盖。Target=$target; ExistingHash=$targetHash; StagingHash=$stagingHash")
            $exception.Data["PreserveStaging"] = $true
            throw $exception
        }

        Remove-Item -LiteralPath $staging -Recurse -Force
        return [pscustomobject]@{
            Status = "ExistingIdentical"
            TargetDirectory = $target
            ManifestHash = $targetHash
        }
    }

    [System.IO.Directory]::Move($staging, $target)
    return [pscustomobject]@{
        Status = "Promoted"
        TargetDirectory = $target
        ManifestHash = $stagingHash
    }
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
        $script:ActiveTestProcess = Get-ProcessByExecutablePath $ExpectedExecutable
    }
    while ($null -eq $script:ActiveTestProcess -and [DateTime]::UtcNow -lt $deadline)

    if ($null -eq $script:ActiveTestProcess) {
        throw "$Label 未启动目标程序：$ExpectedExecutable"
    }

    $processId = $script:ActiveTestProcess.Id
    Start-Sleep -Seconds 8
    $script:ActiveTestProcess.Refresh()
    if ($script:ActiveTestProcess.HasExited) {
        throw "$Label 启动后 8 秒内异常退出。"
    }

    $handleDeadline = [DateTime]::UtcNow.AddSeconds(8)
    while ($script:ActiveTestProcess.MainWindowHandle -eq [IntPtr]::Zero -and
           [DateTime]::UtcNow -lt $handleDeadline) {
        Start-Sleep -Milliseconds 250
        $script:ActiveTestProcess.Refresh()
    }

    if (-not $script:ActiveTestProcess.CloseMainWindow()) {
        throw "$Label 无法通过 CloseMainWindow 正常关闭，PID=$processId。"
    }

    if (-not $script:ActiveTestProcess.WaitForExit(15000)) {
        throw "$Label 在收到正常关闭请求后 15 秒仍未退出，PID=$processId。"
    }

    $script:ActiveTestProcess = $null
    return $processId
}

function Invoke-ApplicationLaunchForMode {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Launch", "Static")]
        [string]$ValidationMode,
        [Parameter(Mandatory = $true)][scriptblock]$LaunchAction)

    if ($ValidationMode -eq "Static") {
        return $null
    }

    return & $LaunchAction
}

function Get-CurrentUserDesktopDirectory {
    $shell = New-Object -ComObject WScript.Shell
    try {
        $desktopDirectory = [string]$shell.SpecialFolders.Item("Desktop")
        if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
            -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
            throw "无法解析 Windows 当前用户桌面特殊目录。"
        }

        return Get-FullPath $desktopDirectory
    }
    finally {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

function New-ReleaseShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$IconLocation,
        [Parameter(Mandatory = $true)][string]$Description)

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        $shortcut.TargetPath = $TargetPath
        $shortcut.WorkingDirectory = $WorkingDirectory
        $shortcut.IconLocation = $IconLocation
        $shortcut.Description = $Description
        $shortcut.Save()
    }
    finally {
        if ($null -ne $shortcut) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }

        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

function Get-ReleaseShortcutProperties {
    param([Parameter(Mandatory = $true)][string]$ShortcutPath)

    if (-not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
        throw "快捷方式不存在：$ShortcutPath"
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        return [pscustomobject]@{
            TargetPath = [string]$shortcut.TargetPath
            WorkingDirectory = [string]$shortcut.WorkingDirectory
            IconLocation = [string]$shortcut.IconLocation
            Description = [string]$shortcut.Description
        }
    }
    finally {
        if ($null -ne $shortcut) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }

        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

function Assert-ReleaseShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$ExpectedTarget,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ExpectedIconLocation,
        [Parameter(Mandatory = $true)][string]$ExpectedDescription)

    $properties = Get-ReleaseShortcutProperties $ShortcutPath
    if (-not (Test-SamePath $properties.TargetPath $ExpectedTarget)) {
        throw "快捷方式 TargetPath 不正确：$($properties.TargetPath)"
    }

    if (-not (Test-SamePath $properties.WorkingDirectory $ExpectedWorkingDirectory)) {
        throw "快捷方式 WorkingDirectory 不正确：$($properties.WorkingDirectory)"
    }

    if (-not [string]::Equals($properties.IconLocation, $ExpectedIconLocation, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "快捷方式 IconLocation 不正确：$($properties.IconLocation)"
    }

    if (-not [string]::Equals($properties.Description, $ExpectedDescription, [System.StringComparison]::Ordinal)) {
        throw "快捷方式 Description 不正确：$($properties.Description)"
    }

    if (-not (Test-Path -LiteralPath $properties.TargetPath -PathType Leaf)) {
        throw "快捷方式目标不存在：$($properties.TargetPath)"
    }

    return $properties
}

function Invoke-AtomicFileReplace {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$BackupPath,
        [Parameter(Mandatory = $true)][ValidateSet("Install", "Restore")][string]$Phase)

    [System.IO.File]::Replace($SourcePath, $DestinationPath, $BackupPath, $true)
}

function Install-ReleaseShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$TemporaryShortcutPath,
        [Parameter(Mandatory = $true)][string]$InstalledShortcutPath,
        [Parameter(Mandatory = $true)][string]$ExpectedTarget,
        [Parameter(Mandatory = $true)][string]$ExpectedWorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ExpectedIconLocation,
        [Parameter(Mandatory = $true)][string]$ExpectedDescription,
        [scriptblock]$PostInstallValidator)

    $temporaryDirectory = Get-FullPath (Split-Path -Parent $TemporaryShortcutPath)
    $installedDirectory = Get-FullPath (Split-Path -Parent $InstalledShortcutPath)
    if (-not (Test-SamePath $temporaryDirectory $installedDirectory)) {
        throw "临时快捷方式与正式快捷方式必须位于同一目录：Temporary=$TemporaryShortcutPath; Installed=$InstalledShortcutPath"
    }

    [void](Assert-ReleaseShortcut $TemporaryShortcutPath $ExpectedTarget $ExpectedWorkingDirectory $ExpectedIconLocation $ExpectedDescription)
    $hadExistingShortcut = Test-Path -LiteralPath $InstalledShortcutPath -PathType Leaf
    $oldHash = $null
    $oldProperties = $null
    if ($hadExistingShortcut) {
        $oldHash = (Get-FileHash -LiteralPath $InstalledShortcutPath -Algorithm SHA256).Hash
        $oldProperties = Get-ReleaseShortcutProperties $InstalledShortcutPath
    }

    $backupPath = Join-Path $installedDirectory ("跨境ETF.__backup_" + [Guid]::NewGuid().ToString("N") + ".lnk")
    $failedNewPath = Join-Path $installedDirectory ("跨境ETF.__failed_" + [Guid]::NewGuid().ToString("N") + ".lnk")
    $replacementCompleted = $false
    try {
        if ($hadExistingShortcut) {
            Invoke-AtomicFileReplace $TemporaryShortcutPath $InstalledShortcutPath $backupPath "Install"
        }
        else {
            [System.IO.File]::Move($TemporaryShortcutPath, $InstalledShortcutPath)
        }

        $replacementCompleted = $true
        if ($null -ne $PostInstallValidator) {
            & $PostInstallValidator $InstalledShortcutPath
        }
        else {
            [void](Assert-ReleaseShortcut $InstalledShortcutPath $ExpectedTarget $ExpectedWorkingDirectory $ExpectedIconLocation $ExpectedDescription)
        }

        if (Test-Path -LiteralPath $backupPath) {
            try {
                Remove-Item -LiteralPath $backupPath -Force
            }
            catch {
                Write-Warning "快捷方式已安装，但备份清理失败，保留路径：$backupPath；$($_.Exception.Message)"
            }
        }

        return [pscustomobject]@{
            InstalledShortcutPath = $InstalledShortcutPath
            ReplacedExisting = $hadExistingShortcut
        }
    }
    catch {
        $installFailure = $_.Exception
        if ($replacementCompleted) {
            if ($hadExistingShortcut) {
                try {
                    if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                        throw "原快捷方式备份不存在：$backupPath"
                    }

                    Invoke-AtomicFileReplace $backupPath $InstalledShortcutPath $failedNewPath "Restore"
                    $restoredHash = (Get-FileHash -LiteralPath $InstalledShortcutPath -Algorithm SHA256).Hash
                    if (-not [string]::Equals($restoredHash, $oldHash, [System.StringComparison]::OrdinalIgnoreCase)) {
                        throw "恢复后的快捷方式 SHA-256 与原快捷方式不一致。"
                    }

                    [void](Assert-ReleaseShortcut `
                        $InstalledShortcutPath `
                        $oldProperties.TargetPath `
                        $oldProperties.WorkingDirectory `
                        $oldProperties.IconLocation `
                        $oldProperties.Description)

                    if (Test-Path -LiteralPath $failedNewPath) {
                        try {
                            Remove-Item -LiteralPath $failedNewPath -Force
                        }
                        catch {
                            Write-Warning "旧快捷方式已恢复，但失败的新快捷方式未清理：$failedNewPath；$($_.Exception.Message)"
                        }
                    }
                }
                catch {
                    $restoreFailure = $_.Exception
                    try {
                        if (Test-Path -LiteralPath $InstalledShortcutPath -PathType Leaf) {
                            Remove-Item -LiteralPath $InstalledShortcutPath -Force
                        }
                    }
                    catch {
                        Write-Warning "无法移除验证失败的新快捷方式：$InstalledShortcutPath；$($_.Exception.Message)"
                    }

                    throw "快捷方式安装失败且原快捷方式恢复失败。Backup=$backupPath; InstallError=$($installFailure.Message); RestoreError=$($restoreFailure.Message)"
                }
            }
            elseif (Test-Path -LiteralPath $InstalledShortcutPath -PathType Leaf) {
                Remove-Item -LiteralPath $InstalledShortcutPath -Force
            }
        }

        throw "快捷方式安装失败：$($installFailure.Message)"
    }
    finally {
        if (Test-Path -LiteralPath $TemporaryShortcutPath -PathType Leaf) {
            try {
                Remove-Item -LiteralPath $TemporaryShortcutPath -Force
            }
            catch {
                Write-Warning "临时快捷方式清理失败，保留路径：$TemporaryShortcutPath；$($_.Exception.Message)"
            }
        }
    }
}

function Invoke-CrossEtfRelease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [switch]$CreateDesktopShortcut,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit,
        [Parameter(Mandatory = $true)][ValidateSet("Launch", "Static")][string]$ValidationMode)

    $stagingDirectory = $null
    $temporaryShortcutPath = $null
    $preserveStaging = $false
    try {
        $source = Assert-ReleaseSource $SourcePath $Version $ExpectedCommit
        $projectPath = Join-Path $source.SourceDirectory $script:ProjectName
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "发布项目不存在：$projectPath"
        }

        $outputRootDirectory = Get-FullPath $OutputRoot
        [void](New-Item -ItemType Directory -Path $outputRootDirectory -Force)
        $targetDirectory = Get-FullPath (Join-Path $outputRootDirectory "v$Version")
        $requiredPrefix = $outputRootDirectory + [System.IO.Path]::DirectorySeparatorChar
        if (-not $targetDirectory.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "目标发布目录越界：$targetDirectory"
        }

        $stagingDirectory = Get-FullPath (Join-Path $outputRootDirectory (".__staging_v$Version" + "_" + [Guid]::NewGuid().ToString("N")))
        [void](New-Item -ItemType Directory -Path $stagingDirectory)
        Invoke-ReleasePublish $projectPath $stagingDirectory $Version $ExpectedCommit
        [void](Rename-ReleaseAppHost $stagingDirectory)
        $stagingPackage = Assert-PublishedPackage $stagingDirectory $Version $ExpectedCommit
        $promotion = Promote-ReleaseDirectory $stagingDirectory $targetDirectory
        $stagingDirectory = $null
        $finalPackage = Assert-PublishedPackage $targetDirectory $Version $ExpectedCommit

        $directLaunchPid = Invoke-ApplicationLaunchForMode $ValidationMode {
            Test-ApplicationLaunch `
                -LaunchPath $finalPackage.ExecutablePath `
                -ExpectedExecutable $finalPackage.ExecutablePath `
                -WorkingDirectory $targetDirectory `
                -Label "正式 EXE"
        }

        $installedShortcutPath = $null
        $shortcutLaunchPid = $null
        if ($CreateDesktopShortcut) {
            $desktopDirectory = Get-CurrentUserDesktopDirectory
            $installedShortcutPath = Join-Path $desktopDirectory $script:ShortcutName
            $temporaryShortcutPath = Join-Path $desktopDirectory ("跨境ETF.__new_" + [Guid]::NewGuid().ToString("N") + ".tmp.lnk")
            $iconLocation = $finalPackage.ExecutablePath + ",0"
            $description = "跨境ETF智能投资决策系统 V$Version"

            New-ReleaseShortcut $temporaryShortcutPath $finalPackage.ExecutablePath $targetDirectory $iconLocation $description
            [void](Assert-ReleaseShortcut $temporaryShortcutPath $finalPackage.ExecutablePath $targetDirectory $iconLocation $description)
            [void](Install-ReleaseShortcut `
                $temporaryShortcutPath `
                $installedShortcutPath `
                $finalPackage.ExecutablePath `
                $targetDirectory `
                $iconLocation `
                $description)
            $temporaryShortcutPath = $null

            $shortcutLaunchPid = Invoke-ApplicationLaunchForMode $ValidationMode {
                Test-ApplicationLaunch `
                    -LaunchPath $installedShortcutPath `
                    -ExpectedExecutable $finalPackage.ExecutablePath `
                    -WorkingDirectory $targetDirectory `
                    -Label "桌面快捷方式"
            }
        }

        Write-Host "发布成功"
        Write-Host "ValidationMode=$ValidationMode"
        Write-Host "Version=$Version"
        Write-Host "Commit=$($source.Commit)"
        Write-Host "Tag=$($source.TagName)"
        Write-Host "OutputDirectory=$targetDirectory"
        Write-Host "PromotionStatus=$($promotion.Status)"
        Write-Host "Executable=$($finalPackage.ExecutablePath)"
        Write-Host "FileVersion=$($finalPackage.FileVersion)"
        Write-Host "ProductVersion=$($finalPackage.ProductVersion)"
        Write-Host "AssemblyName=$($finalPackage.AssemblyName)"
        Write-Host "ExecutableSha256=$($finalPackage.ExecutableSha256)"
        Write-Host "ManifestHash=$($finalPackage.ManifestHash)"
        Write-Host "PublishArguments=$((Get-ReleasePublishArguments $projectPath '<staging>' $Version $ExpectedCommit) -join ' ')"
        if ($ValidationMode -eq "Static") {
            Write-Host "ApplicationLaunch=Skipped"
            Write-Host "ShortcutLaunch=Skipped"
        }
        else {
            Write-Host "DirectLaunchPid=$directLaunchPid"
            if ($CreateDesktopShortcut) {
                Write-Host "ShortcutLaunchPid=$shortcutLaunchPid"
            }
        }

        if ($CreateDesktopShortcut) {
            Write-Host "DesktopShortcut=$installedShortcutPath"
        }
    }
    catch {
        $failure = $_
        if ($failure.Exception.Data.Contains("PreserveStaging")) {
            $preserveStaging = [bool]$failure.Exception.Data["PreserveStaging"]
        }

        if ($null -ne $script:ActiveTestProcess) {
            try {
                $script:ActiveTestProcess.Refresh()
                if (-not $script:ActiveTestProcess.HasExited) {
                    [void]$script:ActiveTestProcess.CloseMainWindow()
                    [void]$script:ActiveTestProcess.WaitForExit(5000)
                }
            }
            catch {
                Write-Warning "测试进程无法正常关闭，未使用 Kill。PID=$($script:ActiveTestProcess.Id)"
            }
        }

        if ($temporaryShortcutPath -and (Test-Path -LiteralPath $temporaryShortcutPath -PathType Leaf)) {
            try {
                Remove-Item -LiteralPath $temporaryShortcutPath -Force
            }
            catch {
                Write-Warning "临时快捷方式清理失败，保留路径：$temporaryShortcutPath；$($_.Exception.Message)"
            }
        }

        if ($stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory -PathType Container)) {
            if ($preserveStaging) {
                Write-Warning "为调查同版本内容冲突，保留 staging：$stagingDirectory"
            }
            else {
                try {
                    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
                }
                catch {
                    Write-Warning "staging 清理失败，保留路径：$stagingDirectory；$($_.Exception.Message)"
                }
            }
        }

        throw "发布失败：Stage=Invoke-CrossEtfRelease; Version=$Version; ExpectedCommit=$ExpectedCommit; SourcePath=$SourcePath; Error=$($failure.Exception.Message)"
    }
}

if ($MyInvocation.InvocationName -ne ".") {
    try {
        Invoke-CrossEtfRelease @PSBoundParameters
    }
    catch {
        Write-Error $_.Exception.Message
        exit 1
    }
}
