using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Tax;
using Nop.Plugin.Tax.NodeService.Models;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Tax;

namespace Nop.Plugin.Tax.NodeService.Services;

/// <summary>
/// Node tax provider
/// </summary>
public class NodeTaxProvider : BasePlugin, ITaxProvider
{
    #region Fields

    protected readonly IAddressService _addressService;
    protected readonly ICustomerService _customerService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IOrderTotalCalculationService _orderTotalCalculationService;
    protected readonly IPaymentService _paymentService;
    protected readonly IProductService _productService;
    protected readonly ISettingService _settingService;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IWebHelper _webHelper;
    protected readonly NodeTaxHttpClient _nodeTaxHttpClient;
    protected readonly TaxSettings _taxSettings;

    #endregion

    #region Ctor

    public NodeTaxProvider(IAddressService addressService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IPaymentService paymentService,
        IProductService productService,
        ISettingService settingService,
        IShoppingCartService shoppingCartService,
        IWebHelper webHelper,
        NodeTaxHttpClient nodeTaxHttpClient,
        TaxSettings taxSettings)
    {
        _addressService = addressService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _paymentService = paymentService;
        _productService = productService;
        _settingService = settingService;
        _shoppingCartService = shoppingCartService;
        _webHelper = webHelper;
        _nodeTaxHttpClient = nodeTaxHttpClient;
        _taxSettings = taxSettings;
    }

    #endregion

    #region Utilities

    protected virtual TaxAddressModel MapAddress(Address address)
    {
        if (address == null)
            return null;

        return new TaxAddressModel
        {
            CountryId = address.CountryId ?? 0,
            StateProvinceId = address.StateProvinceId ?? 0,
            Zip = address.ZipPostalCode
        };
    }

    protected virtual async Task<Address> GetTaxAddressAsync(Customer customer)
    {
        if (customer == null)
            return null;

        return _taxSettings.TaxBasedOn switch
        {
            TaxBasedOn.BillingAddress => await _customerService.GetCustomerBillingAddressAsync(customer),
            TaxBasedOn.ShippingAddress => await _customerService.GetCustomerShippingAddressAsync(customer),
            _ => await _addressService.GetAddressByIdAsync(_taxSettings.DefaultTaxAddressId)
        };
    }

    protected virtual async Task<TaxSettingsApiModel> BuildTaxSettingsAsync()
    {
        var countryStateZipEnabled = await _settingService.GetSettingByKeyAsync<bool>(
            "FixedOrByCountryStateZipTaxSettings.CountryStateZipEnabled");

        return new TaxSettingsApiModel
        {
            PricesIncludeTax = _taxSettings.PricesIncludeTax,
            ShippingIsTaxable = _taxSettings.ShippingIsTaxable,
            ShippingPriceIncludesTax = _taxSettings.ShippingPriceIncludesTax,
            ShippingTaxClassId = _taxSettings.ShippingTaxClassId,
            PaymentMethodAdditionalFeeIsTaxable = _taxSettings.PaymentMethodAdditionalFeeIsTaxable,
            PaymentMethodAdditionalFeeIncludesTax = _taxSettings.PaymentMethodAdditionalFeeIncludesTax,
            PaymentMethodAdditionalFeeTaxClassId = _taxSettings.PaymentMethodAdditionalFeeTaxClassId,
            CountryStateZipEnabled = countryStateZipEnabled
        };
    }

    protected virtual TaxRateResult MapTaxRateResponse(TaxRateApiResponse response)
    {
        var result = new TaxRateResult();

        if (response?.Errors?.Any() ?? false)
        {
            foreach (var error in response.Errors)
                result.AddError(error);
        }

        if (response != null && !response.Success)
            return result;

        result.TaxRate = response?.TaxRate ?? decimal.Zero;
        return result;
    }

    protected virtual TaxTotalResult MapTaxTotalResponse(TaxTotalApiResponse response)
    {
        var result = new TaxTotalResult();

        if (response?.Errors?.Any() ?? false)
        {
            foreach (var error in response.Errors)
                result.AddError(error);
        }

        if (response != null && !response.Success)
            return result;

        result.TaxTotal = response?.TaxTotal ?? decimal.Zero;

        if (response?.TaxRates != null)
        {
            foreach (var (rate, amount) in response.TaxRates)
            {
                if (decimal.TryParse(rate, out var parsedRate))
                    result.TaxRates[parsedRate] = amount;
            }
        }

        return result;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets tax rate
    /// </summary>
    public async Task<TaxRateResult> GetTaxRateAsync(TaxRateRequest taxRateRequest)
    {
        var countryStateZipEnabled = await _settingService.GetSettingByKeyAsync<bool>(
            "FixedOrByCountryStateZipTaxSettings.CountryStateZipEnabled");

        var request = new TaxRateApiRequest
        {
            StoreId = taxRateRequest.CurrentStoreId,
            TaxCategoryId = taxRateRequest.TaxCategoryId,
            Price = taxRateRequest.Price,
            Address = MapAddress(taxRateRequest.Address),
            Customer = taxRateRequest.Customer == null
                ? null
                : new TaxCustomerModel
                {
                    Id = taxRateRequest.Customer.Id,
                    IsTaxExempt = taxRateRequest.Customer.IsTaxExempt
                },
            Product = taxRateRequest.Product == null
                ? null
                : new TaxProductModel
                {
                    IsTaxExempt = taxRateRequest.Product.IsTaxExempt
                },
            CountryStateZipEnabled = countryStateZipEnabled
        };

        var response = await _nodeTaxHttpClient.GetTaxRateAsync(request);
        return MapTaxRateResponse(response);
    }

    /// <summary>
    /// Gets tax total
    /// </summary>
    public async Task<TaxTotalResult> GetTaxTotalAsync(TaxTotalRequest taxTotalRequest)
    {
        var customer = taxTotalRequest.Customer;
        var address = await GetTaxAddressAsync(customer);
        var cartItems = new List<CartItemApiModel>();

        foreach (var cartItem in taxTotalRequest.ShoppingCart)
        {
            var product = await _productService.GetProductByIdAsync(cartItem.ProductId);
            if (product == null)
                continue;

            var unitPrice = (await _shoppingCartService.GetUnitPriceAsync(cartItem, true)).unitPrice;
            cartItems.Add(new CartItemApiModel
            {
                ProductId = product.Id,
                TaxCategoryId = product.TaxCategoryId,
                UnitPrice = unitPrice,
                Quantity = cartItem.Quantity,
                IsTaxExempt = product.IsTaxExempt
            });
        }

        var shippingPrice = decimal.Zero;
        if (_taxSettings.ShippingIsTaxable)
        {
            var (shippingExclTax, _, _) = await _orderTotalCalculationService
                .GetShoppingCartShippingTotalAsync(taxTotalRequest.ShoppingCart, false);
            shippingPrice = shippingExclTax ?? decimal.Zero;
        }

        var paymentFee = decimal.Zero;
        if (taxTotalRequest.UsePaymentMethodAdditionalFee && _taxSettings.PaymentMethodAdditionalFeeIsTaxable)
        {
            var paymentMethodSystemName = customer != null
                ? await _genericAttributeService.GetAttributeAsync<string>(customer,
                    NopCustomerDefaults.SelectedPaymentMethodAttribute, taxTotalRequest.StoreId)
                : string.Empty;

            paymentFee = await _paymentService.GetAdditionalHandlingFeeAsync(taxTotalRequest.ShoppingCart,
                paymentMethodSystemName);
        }

        var request = new TaxTotalApiRequest
        {
            StoreId = taxTotalRequest.StoreId,
            UsePaymentMethodAdditionalFee = taxTotalRequest.UsePaymentMethodAdditionalFee,
            Customer = customer == null
                ? null
                : new TaxCustomerModel
                {
                    Id = customer.Id,
                    IsTaxExempt = customer.IsTaxExempt
                },
            CartItems = cartItems,
            ShippingPrice = shippingPrice,
            PaymentFee = paymentFee,
            Address = MapAddress(address),
            Settings = await BuildTaxSettingsAsync()
        };

        var response = await _nodeTaxHttpClient.GetTaxTotalAsync(request);
        return MapTaxTotalResponse(response);
    }

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/NodeTax/Configure";
    }

    /// <summary>
    /// Install plugin
    /// </summary>
    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new NodeTaxSettings());

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Tax.NodeService.Fields.BaseUrl"] = "Tax service base URL",
            ["Plugins.Tax.NodeService.Fields.BaseUrl.Hint"] = "The base URL of the Node tax microservice, for example http://tax_service:3000.",
            ["Plugins.Tax.NodeService.Fields.ApiKey"] = "API key",
            ["Plugins.Tax.NodeService.Fields.ApiKey.Hint"] = "The API key sent in the X-API-Key header.",
            ["Plugins.Tax.NodeService.Fields.RequestTimeoutSeconds"] = "Request timeout (seconds)",
            ["Plugins.Tax.NodeService.Fields.RequestTimeoutSeconds.Hint"] = "The HTTP timeout used when calling the Node tax service.",
            ["Plugins.Tax.NodeService.Fields.LogRequestErrors"] = "Log request errors",
            ["Plugins.Tax.NodeService.Fields.LogRequestErrors.Hint"] = "Check to log failed requests to the Node tax service."
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall plugin
    /// </summary>
    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<NodeTaxSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Tax.NodeService");

        await base.UninstallAsync();
    }

    #endregion
}
