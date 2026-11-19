using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Tax.NodeService.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Tax.NodeService.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class NodeTaxController : BasePluginController
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly IPermissionService _permissionService;
    protected readonly ISettingService _settingService;
    protected readonly NodeTaxSettings _nodeTaxSettings;

    #endregion

    #region Ctor

    public NodeTaxController(ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        NodeTaxSettings nodeTaxSettings)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _nodeTaxSettings = nodeTaxSettings;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_TAX_SETTINGS)]
    public IActionResult Configure()
    {
        var model = new ConfigurationModel
        {
            BaseUrl = _nodeTaxSettings.BaseUrl,
            ApiKey = _nodeTaxSettings.ApiKey,
            RequestTimeoutSeconds = _nodeTaxSettings.RequestTimeoutSeconds,
            LogRequestErrors = _nodeTaxSettings.LogRequestErrors
        };

        return View("~/Plugins/Tax.NodeService/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_TAX_SETTINGS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        _nodeTaxSettings.BaseUrl = model.BaseUrl?.Trim();
        _nodeTaxSettings.ApiKey = model.ApiKey?.Trim();
        _nodeTaxSettings.RequestTimeoutSeconds = model.RequestTimeoutSeconds;
        _nodeTaxSettings.LogRequestErrors = model.LogRequestErrors;

        await _settingService.SaveSettingAsync(_nodeTaxSettings);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    #endregion
}
