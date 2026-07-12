using System.Net.Http;
using System.Net;
using System.Security.Authentication;
using System.Globalization;
using System.Text;
using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Infrastructure.Market;

public sealed class MarketDataClient : IChartMarketDataClient
{
    private const int TencentDailyPageSize = 320;
    private const int TencentDailyMaximumTarget = 3000;
    private static readonly TimeSpan TencentHistoryPageMinimumInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TencentHistoryPageMaximumInterval = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;
    private readonly HttpClient _eastMoneyHistoryHttpClient;
    private readonly Encoding _gb18030Encoding;
    private readonly GlobalMarketRequestScheduler? _scheduler;
    private readonly Func<DateTimeOffset> _nowProvider;

    public MarketDataClient(GlobalMarketRequestScheduler? scheduler = null, Func<DateTimeOffset>? nowProvider = null)
    {
        _scheduler = scheduler;
        _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _gb18030Encoding = Encoding.GetEncoding("GB18030");
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        var historyHandler = new SocketsHttpHandler
        {
            UseProxy = false,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
        };
        _eastMoneyHistoryHttpClient = new HttpClient(historyHandler)
        {
            Timeout = TimeSpan.FromSeconds(8),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        _eastMoneyHistoryHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        _eastMoneyHistoryHttpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept-Language",
            "zh-CN,zh;q=0.9,en;q=0.8");
    }

    public async Task<IReadOnlyList<MarketQuoteRecord>> FetchTencentEtfQuotesAsync(IEnumerable<MarketWatchItem> items, CancellationToken cancellationToken)
    {
        string[] codes = items.Select(item => item.RawCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (codes.Length == 0)
        {
            return Array.Empty<MarketQuoteRecord>();
        }

        string url = "http://qt.gtimg.cn/q=" + string.Join(",", codes);
        byte[] bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        string payload = _gb18030Encoding.GetString(bytes);
        return TencentQuoteParser.Parse(payload, DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<MarketQuoteRecord>> FetchEastMoneyQuotesAsync(IEnumerable<MarketWatchItem> items, CancellationToken cancellationToken)
    {
        Dictionary<string, MarketWatchItem> requested = items
            .Where(item => !string.IsNullOrWhiteSpace(item.RawCode))
            .GroupBy(item => item.RawCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0)
        {
            return Array.Empty<MarketQuoteRecord>();
        }

        string fields = "f12,f2,f3,f43,f170,f15,f16,f17,f18";
        string secids = string.Join(",", requested.Keys.Select(Uri.EscapeDataString));
        string url = "https://push2.eastmoney.com/api/qt/ulist.np/get?secids=" +
                     secids + "&fields=" + fields;
        string json = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return EastMoneyQuoteParser.Parse(json, requested, DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<MarketQuoteRecord>> FetchSinaFundQuotesAsync(IEnumerable<string> fundCodes, CancellationToken cancellationToken)
    {
        string[] codes = fundCodes
            .Select(MarketSymbolNormalizer.DigitsOnly)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => "f_" + code)
            .ToArray();
        if (codes.Length == 0)
        {
            return Array.Empty<MarketQuoteRecord>();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://hq.sinajs.cn/list=" + string.Join(",", codes));
        request.Headers.Referrer = new Uri("http://finance.sina.com.cn");
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        string payload = _gb18030Encoding.GetString(bytes);
        return SinaFundQuoteParser.Parse(payload, DateTimeOffset.Now);
    }

    public Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(string secId, bool isEtf, CancellationToken cancellationToken)
        => FetchEastMoneyHistoryAsync(secId, isEtf, preferDaily: !isEtf, cancellationToken);

    public async Task<EastMoneyHistoryFetchResult> FetchEastMoneyHistoryAsync(string secId, bool isEtf, bool preferDaily, CancellationToken cancellationToken)
    {
        int fqt = isEtf ? 1 : 0;
        var failures = new List<string>();
        foreach ((int klt, string host, string variant, string url) in BuildEastMoneyHistoryUrls(secId, fqt, preferDaily))
        {
            DateTimeOffset now = _nowProvider();
            if (_scheduler is not null
                && !_scheduler.TryAcquireRaw(
                    host,
                    "kline/get",
                    secId,
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(60),
                    now,
                    countsAgainstNonQuoteBudget: false,
                    out MarketRequestDecision schedulerDecision))
            {
                string next = schedulerDecision.NextAllowedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
                failures.Add($"{variant}: scheduler {schedulerDecision.Reason}; next={next}; url={url}");
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                request.Headers.Referrer = new Uri("https://quote.eastmoney.com/");
                request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
                request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

                using HttpResponseMessage response = await _eastMoneyHistoryHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    failures.Add($"{variant}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}; url={url}");
                    continue;
                }

                IReadOnlyList<MarketHistoryPoint> points = EastMoneyHistoryParser.ParsePoints(json);
                if (points.Count == 0)
                {
                    failures.Add($"{variant}: HTTP {(int)response.StatusCode}, no data.klines; url={url}");
                    continue;
                }

                double high = points.Max(point => point.High);
                _scheduler?.RecordRawSuccess(host, "kline/get", secId);
                return new EastMoneyHistoryFetchResult
                {
                    SecId = secId,
                    Fqt = fqt,
                    Klt = klt,
                    Url = url,
                    RawPayload = json,
                    High = high,
                    PointCount = points.Count,
                    LatestDrawdown = EastMoneyHistoryParser.CalculateLatestDrawdown(points),
                    IsSourceExhausted = points.Count < 10000
                };
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                string error = FormatException(ex);
                _scheduler?.RecordRawFailure(host, "kline/get", secId, error, _nowProvider());
                failures.Add($"{variant}: {error}; url={url}");
            }
        }

        throw new InvalidOperationException(
            "EastMoney history failed. secid=" + secId +
            "; http=1.1; proxy=disabled; user-agent=Mozilla/5.0; referer=https://quote.eastmoney.com/; attempts=" +
            string.Join(" || ", failures));
    }

    public async Task<EastMoneyIntradayFetchResult> FetchEastMoneyIntradayAsync(string secId, CancellationToken cancellationToken)
    {
        string url = "https://push2.eastmoney.com/api/qt/stock/trends2/get?secid=" +
                     Uri.EscapeDataString(secId) +
                     "&fields1=f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13" +
                     "&fields2=f51,f52,f53,f54,f55,f56,f57,f58" +
                     "&iscr=0&iscca=0&ndays=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Referrer = new Uri("https://quote.eastmoney.com/");
        request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"EastMoney intraday failed. secid={secId}; HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        IReadOnlyList<IntradayPoint> points = EastMoneyIntradayParser.ParsePoints(json);
        if (points.Count == 0)
        {
            throw new InvalidOperationException("EastMoney intraday failed. secid=" + secId + "; no data.trends");
        }

        return new EastMoneyIntradayFetchResult(secId, json, points, DateTimeOffset.Now);
    }

    public async Task<EastMoneyIntradayFetchResult> FetchTencentIntradayAsync(string tencentCode, CancellationToken cancellationToken)
    {
        string code = tencentCode.Trim().ToLowerInvariant();
        string url = "https://web.ifzq.gtimg.cn/appstock/app/minute/query?code=" +
                     Uri.EscapeDataString(code);
        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Referrer = new Uri("https://gu.qq.com/");
        request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Tencent intraday failed. code={code}; HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        IReadOnlyList<IntradayPoint> points = TencentIntradayParser.ParsePoints(json);
        if (points.Count == 0)
        {
            throw new InvalidOperationException("Tencent intraday failed. code=" + code + "; no minute data");
        }

        return new EastMoneyIntradayFetchResult(code, json, points, DateTimeOffset.Now);
    }

    public async Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryAsync(string tencentCode, CancellationToken cancellationToken)
    {
        string code = tencentCode.Trim().ToLowerInvariant();
        TencentDailyPage page = await FetchTencentDailyPageAsync(
            code,
            endDate: null,
            TencentDailyPageSize,
            cancellationToken).ConfigureAwait(false);
        return BuildTencentHistoryResult(
            code,
            page.Url,
            page.Points,
            page.Points.Count < TencentDailyPageSize,
            requestCount: 1);
    }

    public async Task<EastMoneyHistoryFetchResult> FetchTencentDailyHistoryDepthAsync(
        string tencentCode,
        int targetPointCount,
        CancellationToken cancellationToken)
    {
        string code = tencentCode.Trim().ToLowerInvariant();
        int target = Math.Clamp(targetPointCount, TencentDailyPageSize, TencentDailyMaximumTarget);
        int maxPages = (int)Math.Ceiling(target / (double)TencentDailyPageSize);
        var merged = new SortedDictionary<DateTime, MarketHistoryPoint>();
        DateTime? endDate = null;
        DateTime? previousEarliest = null;
        string firstUrl = string.Empty;
        bool sourceExhausted = false;
        int requestCount = 0;

        while (merged.Count < target && requestCount < maxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requestCount > 0)
            {
                await WaitForTencentHistoryPageSlotAsync(code, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            TencentDailyPage page;
            try
            {
                page = await FetchTencentDailyPageAsync(
                    code,
                    endDate,
                    TencentDailyPageSize,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (requestCount > 0)
                {
                    _scheduler?.RecordRawSuccess("web.ifzq.gtimg.cn", "fqkline/get", code);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (requestCount > 0)
                {
                    _scheduler?.RecordRawFailure(
                        "web.ifzq.gtimg.cn",
                        "fqkline/get",
                        code,
                        FormatException(ex),
                        _nowProvider());
                }

                throw;
            }

            requestCount++;
            firstUrl = firstUrl.Length == 0 ? page.Url : firstUrl;
            DateTime pageEarliest = page.Points.Min(point => point.Date.Date);
            if (previousEarliest.HasValue && pageEarliest >= previousEarliest.Value)
            {
                throw new InvalidOperationException("Tencent qfq daily pagination did not advance to earlier dates. code=" + code);
            }

            int overlapCount = 0;
            foreach (MarketHistoryPoint point in page.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTime date = point.Date.Date;
                if (merged.TryGetValue(date, out MarketHistoryPoint? existing))
                {
                    overlapCount++;
                    if (!SameOhlc(existing, point))
                    {
                        throw new InvalidOperationException("Tencent qfq daily pagination returned conflicting OHLC. code=" + code + "; date=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                }

                merged[date] = point;
            }

            if (requestCount > 1 && overlapCount == 0)
            {
                throw new InvalidOperationException("Tencent qfq daily pagination did not provide an overlap date for adjustment consistency. code=" + code);
            }

            previousEarliest = pageEarliest;
            if (page.Points.Count < TencentDailyPageSize)
            {
                sourceExhausted = true;
                break;
            }

            // Keep one real trading-day overlap so independently returned qfq batches are never spliced unchecked.
            endDate = pageEarliest;
        }

        cancellationToken.ThrowIfCancellationRequested();
        MarketHistoryPoint[] points = merged.Values
            .TakeLast(target)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        EastMoneyHistoryFetchResult result = BuildTencentHistoryResult(
            code,
            firstUrl,
            points,
            sourceExhausted,
            requestCount);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<TencentDailyPage> FetchTencentDailyPageAsync(
        string code,
        DateTime? endDate,
        int count,
        CancellationToken cancellationToken)
    {
        string end = endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        string url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param=" +
                     Uri.EscapeDataString(code + ",day,," + end + "," + count.ToString(CultureInfo.InvariantCulture) + ",qfq");
        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Referrer = new Uri("https://gu.qq.com/");
        request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Tencent qfq daily failed. code={code}; HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        IReadOnlyList<MarketHistoryPoint> points = TencentHistoryParser.ParsePoints(json);
        cancellationToken.ThrowIfCancellationRequested();
        if (points.Count == 0)
        {
            throw new InvalidOperationException("Tencent qfq daily failed. code=" + code + "; no qfqday data");
        }

        return new TencentDailyPage(url, points);
    }

    private static EastMoneyHistoryFetchResult BuildTencentHistoryResult(
        string code,
        string url,
        IReadOnlyList<MarketHistoryPoint> points,
        bool sourceExhausted,
        int requestCount)
    {
        string normalizedPayload = TencentHistoryParser.ToEastMoneyCompatiblePayload(points);
        IReadOnlyList<MarketHistoryPoint> normalizedPoints = EastMoneyHistoryParser.ParsePoints(normalizedPayload);
        if (normalizedPoints.Count == 0)
        {
            throw new InvalidOperationException("Tencent qfq daily failed. code=" + code + "; normalized daily payload is empty");
        }

        return new EastMoneyHistoryFetchResult
        {
            SecId = code,
            Fqt = 1,
            Klt = 101,
            Url = url,
            RawPayload = normalizedPayload,
            High = normalizedPoints.Max(point => point.High),
            PointCount = normalizedPoints.Count,
            LatestDrawdown = EastMoneyHistoryParser.CalculateLatestDrawdown(normalizedPoints),
            IsSourceExhausted = sourceExhausted,
            RequestCount = requestCount
        };
    }

    private async Task WaitForTencentHistoryPageSlotAsync(string code, CancellationToken cancellationToken)
    {
        if (_scheduler is null)
        {
            await Task.Delay(TencentHistoryPageMinimumInterval, cancellationToken).ConfigureAwait(false);
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = _nowProvider();
            if (_scheduler.TryAcquireRaw(
                    "web.ifzq.gtimg.cn",
                    "fqkline/get",
                    code,
                    TencentHistoryPageMinimumInterval,
                    TencentHistoryPageMaximumInterval,
                    now,
                    countsAgainstNonQuoteBudget: false,
                    out MarketRequestDecision decision))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            if (!decision.NextAllowedAt.HasValue)
            {
                throw new InvalidOperationException("Tencent qfq daily pagination blocked by scheduler. code=" + code + "; reason=" + decision.Reason);
            }

            TimeSpan delay = decision.NextAllowedAt.Value - now + TimeSpan.FromMilliseconds(100);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool SameOhlc(MarketHistoryPoint left, MarketHistoryPoint right)
        => NearlyEqual(left.Open, right.Open)
           && NearlyEqual(left.High, right.High)
           && NearlyEqual(left.Low, right.Low)
           && NearlyEqual(left.Close, right.Close);

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) <= 0.00000001;

    private static IEnumerable<(int Klt, string Host, string Variant, string Url)> BuildEastMoneyHistoryUrls(string secId, int fqt, bool preferDaily)
    {
        string end = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (string host in new[] { "push2his.eastmoney.com", "push2hisappn.eastmoney.com" })
        {
            int[] kltOrder = preferDaily ? new[] { 101, 103 } : new[] { 103, 101 };
            foreach (int klt in kltOrder)
            {
                yield return (klt, host, host + ",klt=" + klt, BuildEastMoneyHistoryUrl(host, secId, fqt, klt, null, end));
                yield return (klt, host, host + ",klt=" + klt + ",rtntype=6", BuildEastMoneyHistoryUrl(host, secId, fqt, klt, "rtntype=6", end));
                yield return (klt, host, host + ",klt=" + klt + ",ut", BuildEastMoneyHistoryUrl(host, secId, fqt, klt, "ut=fa5fd1943c7b386f172d6893dbfba10b", end));
                yield return (klt, host, host + ",klt=" + klt + ",rtntype=6,ut", BuildEastMoneyHistoryUrl(host, secId, fqt, klt, "rtntype=6&ut=fa5fd1943c7b386f172d6893dbfba10b", end));
            }
        }

        static string BuildEastMoneyHistoryUrl(string host, string secId, int fqt, int klt, string? extra, string end)
        {
            string url = "https://" + host + "/api/qt/stock/kline/get?secid=" + secId;
            if (!string.IsNullOrWhiteSpace(extra))
            {
                url += "&" + extra;
            }

            return url +
                   "&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61" +
                   "&klt=" + klt.ToString(CultureInfo.InvariantCulture) +
                   "&fqt=" + fqt.ToString(CultureInfo.InvariantCulture) +
                   "&beg=19900101&end=" + end + "&lmt=10000";
        }
    }

    private static string FormatException(Exception exception)
    {
        var messages = new List<string>();
        Exception? current = exception;
        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message)
                && !messages.Contains(current.Message, StringComparer.Ordinal))
            {
                messages.Add(current.Message);
            }

            current = current.InnerException;
        }

        return string.Join(" | ", messages);
    }

    private sealed record TencentDailyPage(
        string Url,
        IReadOnlyList<MarketHistoryPoint> Points);

    public void Dispose()
    {
        _httpClient.Dispose();
        _eastMoneyHistoryHttpClient.Dispose();
    }
}
