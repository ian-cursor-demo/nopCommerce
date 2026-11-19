using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Tax.NodeService.Models;

/// <summary>
/// Represents plugin configuration model
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Tax.NodeService.Fields.BaseUrl")]
    public string BaseUrl { get; set; }

    [NopResourceDisplayName("Plugins.Tax.NodeService.Fields.ApiKey")]
    public string ApiKey { get; set; }

    [NopResourceDisplayName("Plugins.Tax.NodeService.Fields.RequestTimeoutSeconds")]
    public int RequestTimeoutSeconds { get; set; }

    [NopResourceDisplayName("Plugins.Tax.NodeService.Fields.LogRequestErrors")]
    public bool LogRequestErrors { get; set; }
}
