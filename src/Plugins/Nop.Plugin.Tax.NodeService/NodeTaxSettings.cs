using Nop.Core.Configuration;

namespace Nop.Plugin.Tax.NodeService;

/// <summary>
/// Represents settings of the Node tax service plugin
/// </summary>
public class NodeTaxSettings : ISettings
{
    /// <summary>
    /// Gets or sets the Node tax service base URL
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// Gets or sets the API key used to authenticate with the Node tax service
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the request timeout in seconds
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether to log request errors
    /// </summary>
    public bool LogRequestErrors { get; set; } = true;
}
