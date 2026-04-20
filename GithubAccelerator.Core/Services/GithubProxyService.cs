using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace GithubAccelerator.Services;

public class GithubProxyService
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, string> _domainIpMap = new();
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public int ProxyPort { get; set; } = 8888;
    public bool IsRunning { get; private set; }
    public int RequestsHandled { get; private set; }
    public long TotalBytesTransferred { get; private set; }

    public event Action<string>? OnLog;
    public event Action<int>? OnRequestsHandled;

    private static readonly string[] GithubDomains = new[]
    {
        "github.com",
        "api.github.com",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "codeload.github.com",
        "avatars.githubusercontent.com",
        "github.global.ssl.fastly.net"
    };

    public void SetDomainIpMap(Dictionary<string, string> domainIpMap)
    {
        _domainIpMap.Clear();
        foreach (var kvp in domainIpMap)
        {
            _domainIpMap[kvp.Key] = kvp.Value;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            OnLog?.Invoke("代理已在运行中");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{ProxyPort}/");
        _listener.Start();

        IsRunning = true;
        RequestsHandled = 0;
        TotalBytesTransferred = 0;

        OnLog?.Invoke($"代理服务器已启动，监听端口: {ProxyPort}");

        _ = Task.Run(() => AcceptConnectionsAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();

        IsRunning = false;
        OnLog?.Invoke("代理服务器已停止");
    }

    private async Task AcceptConnectionsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await _listener!.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"接受连接失败: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken token)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var targetUrl = request.Url?.ToString();
            if (string.IsNullOrEmpty(targetUrl))
            {
                response.StatusCode = 400;
                return;
            }

            var host = request.Url?.Host;
            if (string.IsNullOrEmpty(host) || !IsGithubDomain(host))
            {
                response.StatusCode = 403;
                return;
            }

            var optimizedUrl = OptimizeUrl(request.Url!, host);

            var proxyRequest = new HttpRequestMessage
            {
                Method = new HttpMethod(request.HttpMethod),
                RequestUri = new Uri(optimizedUrl)
            };

            foreach (string headerName in request.Headers.AllKeys)
            {
                if (headerName == "Host" || headerName == "Connection")
                    continue;

                proxyRequest.Headers.TryAddWithoutValidation(headerName, request.Headers[headerName]);
            }

            if (request.HasEntityBody)
            {
                proxyRequest.Content = new StreamContent(request.InputStream);
            }

            var proxyResponse = await _httpClient.SendAsync(proxyRequest, token);

            response.StatusCode = (int)proxyResponse.StatusCode;
            response.StatusDescription = proxyResponse.ReasonPhrase ?? "";

            foreach (var header in proxyResponse.Headers)
            {
                try
                {
                    response.AddHeader(header.Key, string.Join(", ", header.Value));
                }
                catch
                {
                }
            }

            foreach (var header in proxyResponse.Content.Headers)
            {
                try
                {
                    response.AddHeader(header.Key, string.Join(", ", header.Value));
                }
                catch
                {
                }
            }

            var responseBytes = await proxyResponse.Content.ReadAsByteArrayAsync(token);
            TotalBytesTransferred += responseBytes.Length;

            response.ContentLength64 = responseBytes.Length;
            await response.OutputStream.WriteAsync(responseBytes, token);

            RequestsHandled++;
            OnRequestsHandled?.Invoke(RequestsHandled);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"处理请求失败: {ex.Message}");
            response.StatusCode = 502;
        }
        finally
        {
            response.Close();
        }
    }

    private bool IsGithubDomain(string host)
    {
        return GithubDomains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                                      host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    private string OptimizeUrl(Uri originalUrl, string host)
    {
        if (_domainIpMap.TryGetValue(host, out var ip))
        {
            var optimizedUrl = originalUrl.ToString().Replace(host, ip);
            return optimizedUrl;
        }

        return originalUrl.ToString();
    }

    public async Task<bool> ConfigureGitProxyAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"config --global http.proxy http://localhost:{ProxyPort}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"配置 Git 代理失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveGitProxyAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "config --global --unset http.proxy",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"移除 Git 代理失败: {ex.Message}");
            return false;
        }
    }

    public bool IsGitProxyConfigured()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "config --global --get http.proxy",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd();
                return output.Contains($"localhost:{ProxyPort}");
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _httpClient.Dispose();
    }
}
