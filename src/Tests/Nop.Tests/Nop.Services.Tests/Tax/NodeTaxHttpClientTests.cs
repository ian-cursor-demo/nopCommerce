using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Net.Http.Headers;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Plugin.Tax.NodeService.Models;
using Nop.Plugin.Tax.NodeService.Services;
using Nop.Services.Logging;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Tax;

[TestFixture]
public class NodeTaxHttpClientTests
{
    [Test]
    public async Task GetTaxRateAsync_maps_successful_response()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"errors":[],"taxRate":8.25}""", Encoding.UTF8, MimeTypes.ApplicationJson)
            });

        var client = CreateClient(handler);
        var response = await client.GetTaxRateAsync(new TaxRateApiRequest
        {
            StoreId = 1,
            TaxCategoryId = 2,
            Price = 100
        });

        response.Success.Should().BeTrue();
        response.TaxRate.Should().Be(8.25m);
    }

    [Test]
    public async Task GetTaxRateAsync_returns_error_result_on_http_failure()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"success":false,"errors":["boom"],"taxRate":0}""", Encoding.UTF8, MimeTypes.ApplicationJson)
        });

        var client = CreateClient(handler);
        var response = await client.GetTaxRateAsync(new TaxRateApiRequest
        {
            StoreId = 1,
            TaxCategoryId = 2,
            Price = 100
        });

        response.Success.Should().BeFalse();
        response.Errors.Should().Contain("boom");
    }

    private static NodeTaxHttpClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var settings = new NodeTaxSettings
        {
            BaseUrl = "http://localhost:3000",
            ApiKey = "test-key",
            RequestTimeoutSeconds = 5,
            LogRequestErrors = false
        };

        return new NodeTaxHttpClient(httpClient, NullLogger.Instance, settings);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static NullLogger Instance { get; } = new();

        public bool IsEnabled(LogLevel level) => false;

        public Task DeleteLogAsync(Log log) => Task.CompletedTask;

        public Task DeleteLogsAsync(IList<Log> logs) => Task.CompletedTask;

        public Task ClearLogAsync(DateTime? olderThan = null) => Task.CompletedTask;

        public Task<IPagedList<Log>> GetAllLogsAsync(DateTime? fromUtc = null, DateTime? toUtc = null, string message = "", LogLevel? logLevel = null, int pageIndex = 0, int pageSize = int.MaxValue) =>
            Task.FromResult<IPagedList<Log>>(new PagedList<Log>(Array.Empty<Log>(), pageIndex, pageSize));

        public Task<Log> GetLogByIdAsync(int logId) => Task.FromResult<Log>(null);

        public Task<IList<Log>> GetLogByIdsAsync(int[] logIds) => Task.FromResult<IList<Log>>(Array.Empty<Log>());

        public Task InsertLogAsync(LogLevel logLevel, string shortMessage, string fullMessage = "", Customer customer = null) =>
            Task.CompletedTask;

        public Task InformationAsync(string message, Exception exception = null, Customer customer = null) =>
            Task.CompletedTask;

        public Task WarningAsync(string message, Exception exception = null, Customer customer = null) =>
            Task.CompletedTask;

        public Task ErrorAsync(string message, Exception exception = null, Customer customer = null) =>
            Task.CompletedTask;
    }
}
