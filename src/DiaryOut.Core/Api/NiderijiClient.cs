using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DiaryOut.Core.Models;

namespace DiaryOut.Core.Api;

/// <summary>登录态失效（401 或 error=6/7）。</summary>
public sealed class AuthExpiredException : Exception
{
    public AuthExpiredException(string message) : base(message) { }
}

/// <summary>站点业务错误（error != 0）。</summary>
public sealed class NiderijiApiException : Exception
{
    public int ErrorCode { get; }
    public NiderijiApiException(int errorCode, string message) : base(message) => ErrorCode = errorCode;
}

/// <summary>
/// nideriji.cn API 客户端。
/// 已验证事实（2026-08-31）：
/// - 鉴权头：auth: token &lt;JWT&gt;
/// - 登录：POST /api/login/，JSON {email,password}
/// - 全量同步：POST /api/v2/sync/，multipart 字段 user_config_ts/diaries_ts/readmark_ts/images_ts（0=全量）
/// - 图片下载：GET {fileDomain}/api/image/{user_id}/{image_id}/
/// </summary>
public sealed class NiderijiClient : IDisposable
{
    public const string ApiBase = "https://nideriji.cn";
    public const string FileDomain = "https://f.nideriji.cn";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    /// <summary>请求最小间隔（限速）。</summary>
    public TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>失败重试次数（指数退避）。</summary>
    public int MaxRetries { get; set; } = 3;

    public string? Token { get; private set; }
    public long UserId { get; private set; }
    public string? UserName { get; private set; }

    public NiderijiClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OhApp/3.7 Platform/Web");
    }

    /// <summary>使用已保存的 token 恢复会话（不保存密码）。</summary>
    public void RestoreSession(string token, long userId, string? userName)
    {
        Token = token;
        UserId = userId;
        UserName = userName;
    }

    public void Logout()
    {
        Token = null;
        UserId = 0;
        UserName = null;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        // 站点登录为 multipart/form-data（字段 email、password），与网页端行为一致
        var json = await SendWithRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/api/login/");
            req.Content = new MultipartFormDataContent
            {
                { new StringContent(email), "email" },
                { new StringContent(password), "password" },
            };
            return req;
        }, requireAuth: false, ct).ConfigureAwait(false);

        var resp = JsonSerializer.Deserialize<LoginResponse>(json, JsonOptions)
                   ?? throw new NiderijiApiException(-1, "登录响应解析失败");
        if (resp.Error != 0)
            throw new NiderijiApiException(resp.Error, LoginErrorMessage(resp.Error));
        if (string.IsNullOrEmpty(resp.Token))
            throw new NiderijiApiException(-1, "登录响应缺少 token");

        RestoreSession(resp.Token, resp.UserId, resp.UserConfig?.Name ?? resp.UserConfig?.UserEmail);
        return resp;
    }

    /// <summary>全量同步日记、图片元数据与用户配置。</summary>
    public async Task<SyncResponse> SyncAllAsync(CancellationToken ct = default)
    {
        var json = await SendWithRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/api/v2/sync/");
            var form = new MultipartFormDataContent
            {
                { new StringContent("0"), "user_config_ts" },
                { new StringContent("0"), "diaries_ts" },
                { new StringContent("0"), "readmark_ts" },
                { new StringContent("0"), "images_ts" },
            };
            req.Content = form;
            return req;
        }, requireAuth: true, ct).ConfigureAwait(false);

        var resp = JsonSerializer.Deserialize<SyncResponse>(json, JsonOptions)
                   ?? throw new NiderijiApiException(-1, "同步响应解析失败");
        if (resp.Error != 0)
            throw new NiderijiApiException(resp.Error, $"同步失败（错误码 {resp.Error}）");
        return resp;
    }

    /// <summary>
    /// 下载图片。返回字节与扩展名；HTTP 错误返回 null（由调用方记录失败）。
    /// </summary>
    public async Task<(byte[] Data, string Extension)?> DownloadImageAsync(
        long userId, long imageId, CancellationToken ct = default)
    {
        var url = $"{FileDomain}/api/image/{userId}/{imageId}/";
        try
        {
            return await SendWithRetryForBytesAsync(
                () => new HttpRequestMessage(HttpMethod.Get, url), ct).ConfigureAwait(false);
        }
        catch (AuthExpiredException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory, bool requireAuth, CancellationToken ct)
    {
        var (data, _) = await SendCoreAsync(requestFactory, requireAuth, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(data);
    }

    private async Task<(byte[] Data, string Extension)?> SendWithRetryForBytesAsync(
        Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        var (data, ext) = await SendCoreAsync(requestFactory, requireAuth: true, ct).ConfigureAwait(false);
        return (data, ext);
    }

    /// <summary>限速 + 指数退避重试的统一请求通道。401 / error 6 / 7 视为登录态失效。</summary>
    private async Task<(byte[] Data, string Extension)> SendCoreAsync(
        Func<HttpRequestMessage> requestFactory, bool requireAuth, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            await ThrottleAsync(ct).ConfigureAwait(false);

            using var req = requestFactory();
            req.Headers.Referrer = new Uri($"{ApiBase}/w/");
            if (!string.IsNullOrEmpty(Token))
                req.Headers.TryAddWithoutValidation("auth", $"token {Token}");
            else if (requireAuth)
                throw new AuthExpiredException("缺少登录 token，请先登录");

            try
            {
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    throw new AuthExpiredException("登录态已失效（401），请重新登录");

                if ((int)resp.StatusCode >= 500 || resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    lastError = new HttpRequestException($"HTTP {(int)resp.StatusCode}");
                    continue; // 服务端错误/限流：重试
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var ext = ExtensionFromContentType(resp.Content.Headers.ContentType);

                if (!resp.IsSuccessStatusCode)
                {
                    // 站点约定：业务错误也返回 4xx（如登录 403 {"error":4}），
                    // 正文带 error 字段时交给调用方按错误码处理
                    if (LooksLikeApiError(bytes))
                        return (bytes, ext);
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
                }
                return (bytes, ext);
            }
            catch (AuthExpiredException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex; // 网络错误/超时：重试
            }
        }
        throw lastError ?? new HttpRequestException("请求失败");
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (elapsed < MinRequestInterval)
                await Task.Delay(MinRequestInterval - elapsed, ct).ConfigureAwait(false);
            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static string ExtensionFromContentType(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => ".jpg",
        };

    /// <summary>响应体是否是 {"error": n} 形式的站点业务错误。</summary>
    private static bool LooksLikeApiError(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("error", out _);
        }
        catch
        {
            return false;
        }
    }

    private static string LoginErrorMessage(int error) => error switch
    {
        1 => "参数错误",
        2 => "权限不足",
        3 => "请求过于频繁，请稍后再试",
        4 => "用户不存在或密码错误",
        5 => "用户已存在",
        6 => "Token 无效",
        7 => "Token 已过期",
        8 => "验证失败",
        9 => "账号已被禁用",
        _ => $"登录失败（错误码 {error}）",
    };

    public void Dispose()
    {
        _http.Dispose();
        _throttle.Dispose();
    }
}
