[CmdletBinding()]
param(
    [string]$Version = "8.10.5",
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [string]$OutputRoot = "D:\shibaowangETF\artifacts\installer",
    [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ManagedAssemblyName = "CrossETF.Terminal.UiShell.Reference"
$ReleaseExecutableName = "跨境ETF.exe"
$InstallerScriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) "installer\CrossETF.iss"
$IconPath = Join-Path (Split-Path -Parent $PSScriptRoot) "Resources\AppIcon.ico"

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [int[]]$AllowedExitCodes = @(0))

    Push-Location -LiteralPath $WorkingDirectory
    try {
        $output = @(& $FilePath @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "命令执行失败：$FilePath $($Arguments -join ' '); ExitCode=$exitCode; Output=$($output -join [Environment]::NewLine)"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Resolve-IsccPath {
    param([string]$RequestedPath)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath)
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $candidates.Add($command.Source)
    }

    $candidates.Add("C:\Program Files (x86)\Inno Setup 6\ISCC.exe")
    $candidates.Add("C:\Program Files\Inno Setup 6\ISCC.exe")
    $candidates.Add((Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"))

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return Get-FullPath $candidate
        }
    }

    throw "未找到 Inno Setup 6 ISCC.exe。请通过官方 winget 包 JRSoftware.InnoSetup 安装后重试。"
}

function Assert-CleanDetachedTaggedSource {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$TagName)

    $inside = Invoke-NativeCommand -FilePath "git" -Arguments @("rev-parse", "--is-inside-work-tree") -WorkingDirectory $Source
    if (($inside.Output -join "").Trim() -ne "true") {
        throw "SourcePath 不是 Git worktree：$Source"
    }

    $symbolic = Invoke-NativeCommand -FilePath "git" -Arguments @("symbolic-ref", "-q", "HEAD") -WorkingDirectory $Source -AllowedExitCodes @(0, 1)
    if ($symbolic.ExitCode -eq 0) {
        throw "正式安装包源必须是 detached HEAD，当前分支为：$(($symbolic.Output -join '').Trim())"
    }

    $status = Invoke-NativeCommand -FilePath "git" -Arguments @("status", "--porcelain=v1", "--untracked-files=all") -WorkingDirectory $Source
    if (-not [string]::IsNullOrWhiteSpace(($status.Output -join "`n"))) {
        throw "正式安装包源 worktree 不干净：$Source"
    }

    $head = ((Invoke-NativeCommand -FilePath "git" -Arguments @("rev-parse", "HEAD") -WorkingDirectory $Source).Output -join "").Trim().ToLowerInvariant()
    if ($head -ne $Expected.ToLowerInvariant()) {
        throw "HEAD 与 ExpectedCommit 不一致。Expected=$Expected; Actual=$head"
    }

    $exactTag = ((Invoke-NativeCommand -FilePath "git" -Arguments @("describe", "--tags", "--exact-match", "HEAD") -WorkingDirectory $Source).Output -join "").Trim()
    if ($exactTag -ne $TagName) {
        throw "HEAD exact tag 不正确。Expected=$TagName; Actual=$exactTag"
    }

    $tagType = ((Invoke-NativeCommand -FilePath "git" -Arguments @("cat-file", "-t", $TagName) -WorkingDirectory $Source).Output -join "").Trim()
    if ($tagType -ne "tag") {
        throw "$TagName 不是 annotated tag。ActualType=$tagType"
    }

    $localTagObject = ((Invoke-NativeCommand -FilePath "git" -Arguments @("rev-parse", $TagName) -WorkingDirectory $Source).Output -join "").Trim().ToLowerInvariant()
    $localPeeled = ((Invoke-NativeCommand -FilePath "git" -Arguments @("rev-parse", "$TagName^{}") -WorkingDirectory $Source).Output -join "").Trim().ToLowerInvariant()
    if ($localPeeled -ne $Expected.ToLowerInvariant()) {
        throw "$TagName peeled commit 与 ExpectedCommit 不一致。Expected=$Expected; Actual=$localPeeled"
    }

    $remoteLines = (Invoke-NativeCommand -FilePath "git" -Arguments @("ls-remote", "--tags", "origin", "refs/tags/$TagName*") -WorkingDirectory $Source).Output
    $remoteTagObject = ""
    $remotePeeled = ""
    foreach ($line in $remoteLines) {
        $parts = $line -split "\s+"
        if ($parts.Count -lt 2) {
            continue
        }

        if ($parts[1] -eq "refs/tags/$TagName") {
            $remoteTagObject = $parts[0].ToLowerInvariant()
        }
        elseif ($parts[1] -eq "refs/tags/$TagName^{}") {
            $remotePeeled = $parts[0].ToLowerInvariant()
        }
    }

    if ($remoteTagObject -ne $localTagObject -or $remotePeeled -ne $localPeeled) {
        throw "origin tag 与本地 annotated tag 不一致。LocalObject=$localTagObject; RemoteObject=$remoteTagObject; LocalCommit=$localPeeled; RemoteCommit=$remotePeeled"
    }

    return [pscustomobject]@{
        Head = $head
        TagObject = $localTagObject
        PeeledCommit = $localPeeled
    }
}

function Assert-PublishPayload {
    param([Parameter(Mandatory = $true)][string]$PublishDirectory)

    $requiredFiles = @(
        $ReleaseExecutableName,
        "$ManagedAssemblyName.dll",
        "$ManagedAssemblyName.deps.json",
        "$ManagedAssemblyName.runtimeconfig.json",
        "Microsoft.Data.Sqlite.dll",
        "SQLitePCLRaw.batteries_v2.dll",
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "e_sqlite3.dll",
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll",
        "PresentationFramework.dll",
        "WindowsBase.dll")

    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $requiredFile) -PathType Leaf)) {
            throw "自包含 publish 缺少核心文件：$requiredFile"
        }
    }

    $pollution = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File -Force | Where-Object {
        $_.Name -match '(?i)(\.db$|\.sqlite$|\.sqlite3$|-wal$|-shm$|\.pdb$|\.log$|\.trx$|\.tmp$|\.lnk$|\.Tests\.dll$)' -or
        $_.FullName -match '(?i)[\\/]TestResults[\\/]' -or
        $_.FullName -match '(?i)[\\/]Tests[\\/]'
    })
    if ($pollution.Count -ne 0) {
        throw "自包含 publish 包含禁止文件：$($pollution.FullName -join '; ')"
    }

    $englishAppHost = Join-Path $PublishDirectory "$ManagedAssemblyName.exe"
    if (Test-Path -LiteralPath $englishAppHost) {
        throw "自包含 publish 仍包含英文 apphost：$englishAppHost"
    }

    $files = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File -Force)
    return [pscustomobject]@{
        FileCount = $files.Count
        TotalBytes = ($files | Measure-Object -Property Length -Sum).Sum
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version 格式无效：$Version"
}
if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "ExpectedCommit 必须是完整 40 位 commit：$ExpectedCommit"
}

$source = Get-FullPath $SourcePath
$output = Get-FullPath $OutputRoot
$tagName = "v$Version"
$finalDirectory = Join-Path $output $tagName
$finalInstallerName = "跨境ETF安装程序_v${Version}_win-x64.exe"
$finalInstallerPath = Join-Path $finalDirectory $finalInstallerName
$stagingRoot = Join-Path $output ".__staging_installer_v${Version}_$([Guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $stagingRoot "publish"
$compiledDirectory = Join-Path $stagingRoot "installer"
$preserveStaging = $false

if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "SourcePath 不存在：$source"
}
if (-not (Test-Path -LiteralPath $InstallerScriptPath -PathType Leaf)) {
    throw "Inno Setup 配置不存在：$InstallerScriptPath"
}
if (-not (Test-Path -LiteralPath $IconPath -PathType Leaf)) {
    throw "应用图标不存在：$IconPath"
}

$tagInfo = Assert-CleanDetachedTaggedSource $source $ExpectedCommit $tagName
$iscc = Resolve-IsccPath $IsccPath
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $compiledDirectory -Force | Out-Null

try {
    $projectPath = Join-Path $source "$ManagedAssemblyName.csproj"
    $publishArguments = @(
        "publish", $projectPath,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-o", $publishDirectory,
        "-p:PublishSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0",
        "-p:InformationalVersion=$Version+$ExpectedCommit",
        "-p:IncludeSourceRevisionInInformationalVersion=false")
    $publishResult = Invoke-NativeCommand -FilePath "dotnet" -Arguments $publishArguments -WorkingDirectory $source
    $publishResult.Output | ForEach-Object { Write-Host $_ }

    $englishAppHost = Join-Path $publishDirectory "$ManagedAssemblyName.exe"
    $releaseExecutable = Join-Path $publishDirectory $ReleaseExecutableName
    if (-not (Test-Path -LiteralPath $englishAppHost -PathType Leaf)) {
        throw "publish 未生成预期 apphost：$englishAppHost"
    }
    if (Test-Path -LiteralPath $releaseExecutable) {
        throw "publish 目录在重命名前已存在正式中文 EXE：$releaseExecutable"
    }
    Move-Item -LiteralPath $englishAppHost -Destination $releaseExecutable

    $payload = Assert-PublishPayload $publishDirectory
    $versionInfo = (Get-Item -LiteralPath $releaseExecutable).VersionInfo
    $expectedProductVersion = "$Version+$ExpectedCommit"
    if ($versionInfo.FileVersion -ne "$Version.0" -or $versionInfo.ProductVersion -ne $expectedProductVersion) {
        throw "自包含主程序版本不正确。FileVersion=$($versionInfo.FileVersion); ProductVersion=$($versionInfo.ProductVersion)"
    }

    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName((Join-Path $publishDirectory "$ManagedAssemblyName.dll")).Name
    if ($assemblyName -ne $ManagedAssemblyName) {
        throw "AssemblyName 不正确。Expected=$ManagedAssemblyName; Actual=$assemblyName"
    }

    $isccArguments = @(
        "/DMyAppVersion=$Version",
        "/DSourceDir=$publishDirectory",
        "/DOutputDir=$compiledDirectory",
        "/DIconFile=$IconPath",
        $InstallerScriptPath)
    $isccResult = Invoke-NativeCommand -FilePath $iscc -Arguments $isccArguments -WorkingDirectory (Split-Path -Parent $InstallerScriptPath)
    $isccResult.Output | ForEach-Object { Write-Host $_ }

    $stagedInstallerPath = Join-Path $compiledDirectory $finalInstallerName
    if (-not (Test-Path -LiteralPath $stagedInstallerPath -PathType Leaf)) {
        throw "ISCC 未生成预期安装程序：$stagedInstallerPath"
    }

    $installerHash = (Get-FileHash -LiteralPath $stagedInstallerPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $checksumPath = Join-Path $compiledDirectory "SHA256SUMS.txt"
    $checksumLine = "$installerHash  $finalInstallerName"
    [System.IO.File]::WriteAllText(
        $checksumPath,
        $checksumLine + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
    if ([System.IO.File]::ReadAllText($checksumPath, [System.Text.Encoding]::UTF8).TrimEnd() -ne $checksumLine) {
        throw "SHA256SUMS.txt 写入校验失败：$checksumPath"
    }

    $promotionStatus = "Promoted"
    if (Test-Path -LiteralPath $finalDirectory) {
        if (-not (Test-Path -LiteralPath $finalInstallerPath -PathType Leaf)) {
            $preserveStaging = $true
            throw "同版本输出目录存在但缺少安装程序，拒绝覆盖。Existing=$finalDirectory; Staging=$stagingRoot"
        }

        $existingHash = (Get-FileHash -LiteralPath $finalInstallerPath -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($existingHash -ne $installerHash) {
            $preserveStaging = $true
            throw "同版本安装程序内容不同，拒绝静默覆盖。ExistingHash=$existingHash; NewHash=$installerHash; Staging=$stagingRoot"
        }

        $promotionStatus = "ExistingIdentical"
    }
    else {
        [System.IO.Directory]::Move($compiledDirectory, $finalDirectory)
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $finalInstallerPath
    [pscustomobject]@{
        Version = $Version
        Commit = $tagInfo.Head
        Tag = $tagName
        TagObject = $tagInfo.TagObject
        SourcePath = $source
        IsccPath = $iscc
        PublishDirectory = $publishDirectory
        PublishFileCount = $payload.FileCount
        PublishTotalBytes = $payload.TotalBytes
        OutputDirectory = $finalDirectory
        PromotionStatus = $promotionStatus
        InstallerPath = $finalInstallerPath
        InstallerBytes = (Get-Item -LiteralPath $finalInstallerPath).Length
        InstallerSha256 = $installerHash
        InstallerFileVersion = (Get-Item -LiteralPath $finalInstallerPath).VersionInfo.FileVersion
        SignatureStatus = $signature.Status
    }
}
finally {
    if ((Test-Path -LiteralPath $stagingRoot) -and -not $preserveStaging) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
