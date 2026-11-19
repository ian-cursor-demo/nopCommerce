using System.Net;
using System.Text;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Logging;
using Nop.Plugin.Tax.NodeService.Models;
using Nop.Services.Logging;

namespace Nop.Plugin.Tax.NodeService.Services;

/// <summary>
/// Represents HTTP client for the Node tax service
/// </summary>
public class NodeTaxHttpClient
{
    #region Fields

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly NodeTaxSettings _nodeTaxSettings;

    #endregion

    #region Ctor

    public NodeTaxHttpClient(HttpClient httpClient, ILogger logger, NodeTaxSettings nodeTaxSettings)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(nodeTaxSettings.RequestTimeoutSeconds > 0
            ? nodeTaxSettings.RequestTimeoutSeconds
            : 10);
        httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, MimeTypes.ApplicationJson);

        _httpClient = httpClient;
        _logger = logger;
        _nodeTaxSettings = nodeTaxSettings;
    }

    #endregion

    #region Utilities

    private Uri BuildUri(string path)
    {
        var baseUrl = (_nodeTaxSettings.BaseUrl ?? string.Empty).TrimEnd('/');
        return new Uri($"{baseUrl}{path}");
    }

    private async Task<TResponse> SendAsync<TResponse>(string path, object payload)
        where TResponse : class, new()
    {
        var requestUri = BuildUri(path);
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, MimeTypes.ApplicationJson)
        };

        if (!string.IsNullOrEmpty(_nodeTaxSettings.ApiKey))
            request.Headers.TryAddWithoutValidation(NodeTaxDefaults.ApiKeyHeader, _nodeTaxSettings.ApiKey);

        var httpResponse = await _httpClient.SendAsync(request);
        var response = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            if (_nodeTaxSettings.LogRequestErrors)
            {
                await _logger.InsertLogAsync(LogLevel.Error, NodeTaxDefaults.SystemName,
                    $"Request to {requestUri} failed with status {(int)httpResponse.StatusCode}: {response}");
            }

            if (!string.IsNullOrEmpty(response))
            {
                var errorResponse = JsonConvert.DeserializeObject<TResponse>(response);
                if (errorResponse != null)
                    return errorResponse;
            }

            throw new NopException($"Node tax service request failed with status {(int)httpResponse.StatusCode}.");
        }

        return JsonConvert.DeserializeObject<TResponse>(response) ?? new TResponse();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets tax rate from the Node tax service
    /// </summary>
    public async Task<TaxRateApiResponse> GetTaxRateAsync(TaxRateApiRequest request)
    {
        try
        {
            return await SendAsync<TaxRateApiResponse>(NodeTaxDefaults.TaxRatePath, request);
        }
        catch (Exception exception)
        {
            if (_nodeTaxSettings.LogRequestErrors)
                await _logger.ErrorAsync($"{NodeTaxDefaults.SystemName} tax rate request failed.", exception);

            return new TaxRateApiResponse
            {
                Success = false,
                Errors = new List<string> { exception.Message }
            };
        }
    }

    /// <summary>
    /// Gets tax total from the Node tax service
    /// </summary>
    public async Task<TaxTotalApiResponse> GetTaxTotalAsync(TaxTotalApiRequest request)
    {
        try
        {
            return await SendAsync<TaxTotalApiResponse>(NodeTaxDefaults.TaxTotalPath, request);
        }
        catch (Exception exception)
        {
            if (_nodeTaxSettings.LogRequestErrors)
                await _logger.ErrorAsync($"{NodeTaxDefaults.SystemName} tax total request failed.", exception);

            return new TaxTotalApiResponse
            {
                Success = false,
                Errors = new List<string> { exception.Message }
            };
        }
    }

    #endregion
}
