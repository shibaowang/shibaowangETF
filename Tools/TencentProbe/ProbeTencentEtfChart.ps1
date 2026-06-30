param(
    [Alias('Codes')]
    [string[]]$Code = @(),
    [switch]$Live,
    [switch]$AllowBatch,
    [switch]$Yes,
    [int]$MaxSymbols = 1,
    [int]$DelaySeconds = 5,
    [int]$TimeoutSeconds = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$defaultCodes = @('159509','159941','159513','159660','159501','159659','513100','513300')
$codesToProbe = @($Code | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($codesToProbe.Count -eq 0) {
    $codesToProbe = $defaultCodes
}

function Convert-ToTencentCode {
    param([string]$Value)
    $digits = ($Value -replace '\D', '')
    if ($digits.StartsWith('51') -or $digits.StartsWith('513')) {
        return 'sh' + $digits
    }

    return 'sz' + $digits
}

function New-ProbeTargets {
    param([string]$TencentCode)

    return @(
        [pscustomobject]@{ use = 'quote'; url = "http://qt.gtimg.cn/q=$TencentCode" },
        [pscustomobject]@{ use = 'intraday'; url = "https://web.ifzq.gtimg.cn/appstock/app/minute/query?code=$TencentCode" },
        [pscustomobject]@{ use = 'daily_qfq'; url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param=$TencentCode,day,,,320,qfq" },
        [pscustomobject]@{ use = 'daily_raw'; url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param=$TencentCode,day,,,320" }
    )
}

$plan = foreach ($codeItem in $codesToProbe) {
    $tencentCode = Convert-ToTencentCode $codeItem
    foreach ($target in New-ProbeTargets $tencentCode) {
        [pscustomobject]@{
            code = $codeItem
            tencent_code = $tencentCode
            use = $target.use
            url = $target.url
        }
    }
}

if (-not $Live) {
    [pscustomobject]@{
        generated_at = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        dry_run = $true
        use_proxy = $false
        live_required = $true
        live_hint = 'Add -Live to send requests. Add -AllowBatch -Yes for multiple symbols.'
        symbol_count = $codesToProbe.Count
        estimated_requests = @($plan).Count
        planned_requests = $plan
    } | ConvertTo-Json -Depth 6
    return
}

if ($codesToProbe.Count -gt 1 -and -not $AllowBatch) {
    throw "Live probe allows one symbol by default. Pass -Code 159941 for a single probe, or add -AllowBatch -Yes."
}

if ($codesToProbe.Count -gt $MaxSymbols -and -not $AllowBatch) {
    throw "Live probe symbol count exceeds MaxSymbols=$MaxSymbols. Add -AllowBatch -Yes only after confirming the request volume."
}

if ($AllowBatch -and -not $Yes) {
    $answer = Read-Host "Live batch will send $(@($plan).Count) requests without proxy, delay ${DelaySeconds}s. Type YES to continue"
    if ($answer -ne 'YES') {
        throw 'Live batch probe cancelled.'
    }
}

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Web.Extensions

[System.Net.ServicePointManager]::SecurityProtocol =
    [System.Net.SecurityProtocolType]::Tls12 -bor
    [System.Net.SecurityProtocolType]::Tls11 -bor
    [System.Net.SecurityProtocolType]::Tls

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.UseProxy = $false

$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('Mozilla/5.0')

$json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$json.MaxJsonLength = 20MB

function Invoke-NoProxyGet {
    param(
        [string]$Url,
        [string]$Use
    )

    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = $client.GetAsync($Url).GetAwaiter().GetResult()
        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        $sw.Stop()
        return [pscustomobject]@{
            use = $Use
            url = $Url
            ok = $true
            http = [int]$response.StatusCode
            content_type = ($response.Content.Headers.ContentType | Out-String).Trim()
            elapsed_ms = $sw.ElapsedMilliseconds
            text = $text
            error = $null
        }
    }
    catch {
        $sw.Stop()
        $ex = $_.Exception
        while ($ex.InnerException) {
            $ex = $ex.InnerException
        }

        return [pscustomobject]@{
            use = $Use
            url = $Url
            ok = $false
            http = $null
            content_type = ''
            elapsed_ms = $sw.ElapsedMilliseconds
            text = ''
            error = $ex.GetType().FullName + ': ' + $ex.Message
        }
    }
}

function Get-LeafStrings {
    param([object]$Node)

    $items = New-Object System.Collections.Generic.List[string]

    function Walk {
        param([object]$Value)

        if ($null -eq $Value) {
            return
        }

        if ($Value -is [System.Collections.IDictionary]) {
            foreach ($child in $Value.Values) {
                Walk $child
            }
            return
        }

        if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
            foreach ($child in $Value) {
                Walk $child
            }
            return
        }

        if ($Value -is [string]) {
            $items.Add($Value) | Out-Null
        }
    }

    Walk $Node
    return $items
}

function Analyze-Quote {
    param(
        [string]$Text,
        [string]$TencentCode
    )

    $parse = $Text -match "v_$TencentCode="
    $fieldCount = 0
    $price = $null
    $prevClose = $null
    $quoteTime = $null
    $hasVolume = $false
    $hasAmount = $false
    if ($parse -and $Text -match '"([^"]*)"') {
        $parts = $Matches[1].Split('~')
        $fieldCount = $parts.Count
        if ($parts.Count -gt 4) {
            $price = $parts[3]
            $prevClose = $parts[4]
        }
        if ($parts.Count -gt 30) {
            $quoteTime = $parts[30]
        }
        $hasVolume = $parts.Count -gt 36 -and -not [string]::IsNullOrWhiteSpace($parts[36])
        $hasAmount = $parts.Count -gt 37 -and -not [string]::IsNullOrWhiteSpace($parts[37])
    }

    return [pscustomobject]@{
        parse = $parse
        field_count = $fieldCount
        point_count = $(if ($parse) { 1 } else { 0 })
        first = $quoteTime
        last = $quoteTime
        has_volume = $hasVolume
        has_amount = $hasAmount
        daily_like = $false
        price = $price
        prev_close = $prevClose
    }
}

function Analyze-Minute {
    param([string]$Text)

    $rows = @()
    try {
        $obj = $json.DeserializeObject($Text)
        $leafs = Get-LeafStrings $obj
        $rows = @($leafs | Where-Object {
            $_ -match '^\d{2}:?\d{2}[\s,]' -or
            $_ -match '^\d{4}[-/]?\d{0,2}[-/]?\d{0,2}\s*\d{2}:?\d{2}'
        })
    }
    catch {
        $rows = @()
    }

    $hasVolume = $false
    $hasAmount = $false
    foreach ($row in $rows) {
        $parts = @($row -split '[,\s]+' | Where-Object { $_ -ne '' })
        if ($parts.Count -ge 3) {
            $hasVolume = $true
        }
        if ($parts.Count -ge 4) {
            $hasAmount = $true
        }
    }

    return [pscustomobject]@{
        parse = $rows.Count -gt 0
        field_count = 0
        point_count = $rows.Count
        first = $(if ($rows.Count -gt 0) { $rows[0] } else { '' })
        last = $(if ($rows.Count -gt 0) { $rows[-1] } else { '' })
        has_volume = $hasVolume
        has_amount = $hasAmount
        daily_like = $false
    }
}

function Analyze-Daily {
    param([string]$Text)

    $rows = @()
    try {
        $obj = $json.DeserializeObject($Text)
        $leafs = Get-LeafStrings $obj
        $rows = @($leafs | Where-Object {
            $_ -match '^\d{4}-\d{2}-\d{2}' -or $_ -match '^\d{8}'
        })
    }
    catch {
        $rows = @()
    }

    $hasVolume = $false
    $hasAmount = $false
    foreach ($row in $rows) {
        $parts = @($row -split '[,\s]+' | Where-Object { $_ -ne '' })
        if ($parts.Count -ge 6) {
            $hasVolume = $true
        }
        if ($parts.Count -ge 7) {
            $hasAmount = $true
        }
    }

    return [pscustomobject]@{
        parse = $rows.Count -gt 0
        field_count = 0
        point_count = $rows.Count
        first = $(if ($rows.Count -gt 0) { $rows[0] } else { '' })
        last = $(if ($rows.Count -gt 0) { $rows[-1] } else { '' })
        has_volume = $hasVolume
        has_amount = $hasAmount
        daily_like = $rows.Count -ge 60
    }
}

$requests = New-Object System.Collections.Generic.List[object]
$summary = New-Object System.Collections.Generic.List[object]
$requestIndex = 0

foreach ($codeItem in $codesToProbe) {
    $tencentCode = Convert-ToTencentCode $codeItem
    $targets = New-ProbeTargets $tencentCode
    $byUse = @{}

    foreach ($target in $targets) {
        if ($requestIndex -gt 0 -and $DelaySeconds -gt 0) {
            Start-Sleep -Seconds $DelaySeconds
        }

        $requestIndex++
        $fetch = Invoke-NoProxyGet -Url $target.url -Use $target.use
        if ($target.use -eq 'quote') {
            $analysis = Analyze-Quote -Text $fetch.text -TencentCode $tencentCode
        }
        elseif ($target.use -eq 'intraday') {
            $analysis = Analyze-Minute -Text $fetch.text
        }
        else {
            $analysis = Analyze-Daily -Text $fetch.text
        }

        $sample = $fetch.text
        if ($sample.Length -gt 300) {
            $sample = $sample.Substring(0, 300)
        }

        $row = [pscustomobject]@{
            code = $codeItem
            tencent_code = $tencentCode
            use = $target.use
            url = $target.url
            http = $fetch.http
            content_type = $fetch.content_type
            elapsed_ms = $fetch.elapsed_ms
            request_ok = $fetch.ok
            parse_ok = $analysis.parse
            field_count = $analysis.field_count
            point_count = $analysis.point_count
            first = $analysis.first
            last = $analysis.last
            has_volume = $analysis.has_volume
            has_amount = $analysis.has_amount
            daily_like = $analysis.daily_like
            error = $fetch.error
            sample = $sample.Replace("`r", ' ').Replace("`n", ' ')
        }

        $requests.Add($row) | Out-Null
        $byUse[$target.use] = $row
    }

    $daily = if ($byUse['daily_qfq'].daily_like) { $byUse['daily_qfq'] } else { $byUse['daily_raw'] }
    $summary.Add([pscustomobject]@{
        code = $codeItem
        tencent_code = $tencentCode
        quote_available = $byUse['quote'].parse_ok
        intraday_available = $byUse['intraday'].parse_ok -and $byUse['intraday'].point_count -ge 30
        intraday_points = $byUse['intraday'].point_count
        intraday_volume = $byUse['intraday'].has_volume
        daily_available = $daily.daily_like
        daily_points = $daily.point_count
        daily_quality = $(if ($daily.daily_like) { 'DailyLike' } elseif ($daily.parse_ok) { 'NonDailyLike' } else { 'Unavailable' })
        daily_volume = $daily.has_volume
        conclusion = $(if (($byUse['intraday'].parse_ok -and $byUse['intraday'].point_count -ge 30) -and $daily.daily_like) { 'intraday_and_daily_candidate' } elseif ($byUse['intraday'].parse_ok -and $byUse['intraday'].point_count -ge 30) { 'intraday_candidate_only' } elseif ($daily.daily_like) { 'daily_candidate_only' } else { 'quote_or_unavailable_only' })
    }) | Out-Null
}

[pscustomobject]@{
    generated_at = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    dry_run = $false
    use_proxy = $false
    delay_seconds = $DelaySeconds
    requests = $requests
    summary = $summary
} | ConvertTo-Json -Depth 8
