using System;
using System.Threading;
using System.Threading.Tasks;
using GithubAccelerator.UI.Services;
using Xunit;

namespace GithubAccelerator.Tests;

public class UpdateCheckServiceTests
{
    private sealed class FakeClient : IUpdateCheckClient
    {
        private readonly Func<Task<UpdateCheckHttpResponse>> _response;

        public FakeClient(Func<Task<UpdateCheckHttpResponse>> response) => _response = response;

        public int Calls { get; private set; }

        public Task<UpdateCheckHttpResponse> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            Calls++;
            return _response();
        }
    }

    [Fact]
    public async Task Timeout_IsReportedAsSilentTimeout()
    {
        var service = new UpdateCheckService(new FakeClient(() => throw new OperationCanceledException()));

        var result = await service.CheckAsync(new Version(1, 2, 3));

        Assert.Equal(UpdateCheckStatus.Timeout, result.Status);
        Assert.Contains("不影响使用", result.Message);
    }

    [Fact]
    public async Task Failure_IsReportedWithoutThrowing()
    {
        var service = new UpdateCheckService(new FakeClient(
            () => Task.FromResult(new UpdateCheckHttpResponse(0, string.Empty, "网络不可用"))));

        var result = await service.CheckAsync(new Version(1, 2, 3));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("不影响使用", result.Message);
    }

    [Fact]
    public async Task PrivateRepo404_ExplainsNoPublicChannel()
    {
        var service = new UpdateCheckService(new FakeClient(
            () => Task.FromResult(new UpdateCheckHttpResponse(404, "{}", null))));

        var result = await service.CheckAsync(new Version(1, 2, 3));

        Assert.Equal(UpdateCheckStatus.NoReleaseInfo, result.Status);
        Assert.Contains("未公开", result.Message);
    }

    [Fact]
    public async Task NewerRelease_IsDetectedAndRequestCarriesNoIdentifier()
    {
        var client = new FakeClient(() => Task.FromResult(new UpdateCheckHttpResponse(200,
            "{\"tag_name\":\"v1.3.0\"}", null)));
        var service = new UpdateCheckService(client);

        var result = await service.CheckAsync(new Version(1, 2, 3));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.3.0", result.LatestVersion?.ToString());
        Assert.Empty(service.ApiUri.Query);
        Assert.Empty(service.ApiUri.UserInfo);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public void ParseLatestVersion_HandlesPrefixAndSuffix()
    {
        Assert.Equal("1.2.3", UpdateCheckService.ParseLatestVersion("{\"tag_name\":\"v1.2.3\"}")?.ToString());
        Assert.Equal("1.2.3", UpdateCheckService.ParseLatestVersion("{\"tag_name\":\"v1.2.3-beta\"}")?.ToString());
        Assert.Null(UpdateCheckService.ParseLatestVersion("not json"));
        Assert.Null(UpdateCheckService.ParseLatestVersion("{}"));
    }
}
