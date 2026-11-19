namespace Nop.Plugin.Tax.NodeService;

/// <summary>
/// Represents constants of the Node tax service plugin
/// </summary>
public static class NodeTaxDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public const string SystemName = "Tax.NodeService";

    /// <summary>
    /// Gets the API key header name
    /// </summary>
    public const string ApiKeyHeader = "X-API-Key";

    /// <summary>
    /// Gets the tax rate endpoint path
    /// </summary>
    public const string TaxRatePath = "/v1/tax/rate";

    /// <summary>
    /// Gets the tax total endpoint path
    /// </summary>
    public const string TaxTotalPath = "/v1/tax/total";
}
