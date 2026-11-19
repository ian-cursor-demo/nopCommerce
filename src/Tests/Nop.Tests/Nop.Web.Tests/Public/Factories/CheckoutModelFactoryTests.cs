using AwesomeAssertions;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Tests.Nop.Services.Tests;
using Nop.Web.Factories;
using Nop.Web.Models.Checkout;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Public.Factories;

[TestFixture]
public class CheckoutModelFactoryTests : ServiceTest
{
    private ICheckoutModelFactory _checkoutModelFactory;
    private IShoppingCartService _shoppingCartService;
    private IProductService _productService;
    private IList<ShoppingCartItem> _cart;
    private OrderSettings _orderSettings;
    private ShippingSettings _shippingSettings;
    private RewardPointsSettings _rewardPointsSettings;
    private IAddressService _addressService;
    private Address _address;
    private ISettingService _settingService;
    private PaymentSettings _paymentSettings;
    private IRewardPointService _rewardPointService;
    private Customer _customer;
    private IPaymentMethod _paymentMethod;
    private CommonSettings _commonSettings;
    private IOrderService _orderService;
    private ILanguageService _languageService;
    private IWorkContext _workContext;
    private ICurrencyService _currencyService;
    private Language _defaultLanguage;
    private bool _allowCustomerToChooseDeliveryDate;
    private int _deliveryDateRangeDays;
    private string _euroCustomFormatting;
    private string _euroDisplayLocale;
    private bool _euroPublished;
    private int _customerCurrencyId;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        _settingService = GetService<ISettingService>();
        _shippingSettings = GetService<ShippingSettings>();
        _rewardPointsSettings = GetService<RewardPointsSettings>();
        _commonSettings = GetService<CommonSettings>();

        _shippingSettings.ActiveShippingRateComputationMethodSystemNames.Add("FixedRateTestShippingRateComputationMethod");
        await _settingService.SaveSettingAsync(_shippingSettings);

        _paymentSettings = GetService<PaymentSettings>();
        _paymentSettings.ActivePaymentMethodSystemNames.Add("Payments.TestMethod");
        await _settingService.SaveSettingAsync(_paymentSettings);

        _customer = await GetService<IWorkContext>().GetCurrentCustomerAsync();

        _rewardPointService = GetService<IRewardPointService>();
        await _rewardPointService.AddRewardPointsHistoryEntryAsync(_customer, 10000, 1);

        _shoppingCartService = GetService<IShoppingCartService>();
        _productService = GetService<IProductService>();
        _addressService = GetService<IAddressService>();

        _address = new Address();

        await _addressService.InsertAddressAsync(_address);
        await GetService<ICustomerService>().InsertCustomerAddressAsync(_customer, _address);

        _orderSettings = GetService<OrderSettings>();

        await _shoppingCartService.AddToCartAsync(_customer, await _productService.GetProductByIdAsync(1), ShoppingCartType.ShoppingCart, 1);
        await _shoppingCartService.AddToCartAsync(_customer, await _productService.GetProductByIdAsync(2), ShoppingCartType.ShoppingCart, 1);
        await _shoppingCartService.AddToCartAsync(_customer, await _productService.GetProductByIdAsync(3), ShoppingCartType.ShoppingCart, 1);

        _cart = await _shoppingCartService.GetShoppingCartAsync(_customer, ShoppingCartType.ShoppingCart);

        _paymentMethod = (await GetService<IPaymentPluginManager>().LoadActivePluginsAsync(["Payments.TestMethod"])).FirstOrDefault();
        _orderService = GetService<IOrderService>();
        _languageService = GetService<ILanguageService>();
        _workContext = GetService<IWorkContext>();
        _currencyService = GetService<ICurrencyService>();
        _defaultLanguage = await _workContext.GetWorkingLanguageAsync();
        _allowCustomerToChooseDeliveryDate = _shippingSettings.AllowCustomerToChooseDeliveryDate;
        _deliveryDateRangeDays = _shippingSettings.DeliveryDateRangeDays;

        var euro = await _currencyService.GetCurrencyByCodeAsync("EUR");
        _euroCustomFormatting = euro.CustomFormatting;
        _euroDisplayLocale = euro.DisplayLocale;
        _euroPublished = euro.Published;
        _customerCurrencyId = _customer.CurrencyId ?? 0;

        _checkoutModelFactory = GetService<ICheckoutModelFactory>();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        foreach (var shoppingCartItem in _cart)
            await _shoppingCartService.DeleteShoppingCartItemAsync(shoppingCartItem);

        await _addressService.DeleteAddressAsync(_address);

        _shippingSettings.ActiveShippingRateComputationMethodSystemNames.Clear();
        await _settingService.SaveSettingAsync(_shippingSettings);

        _paymentSettings.ActivePaymentMethodSystemNames.Clear();
        await _settingService.SaveSettingAsync(_paymentSettings);

        _rewardPointsSettings.Enabled = true;
        await _settingService.SaveSettingAsync(_rewardPointsSettings);
        foreach (var history in await _rewardPointService.GetRewardPointsHistoryAsync(_customer.Id))
            await _rewardPointService.DeleteRewardPointsHistoryEntryAsync(history);

        _shippingSettings.AllowCustomerToChooseDeliveryDate = _allowCustomerToChooseDeliveryDate;
        _shippingSettings.DeliveryDateRangeDays = _deliveryDateRangeDays;
        await _settingService.SaveSettingAsync(_shippingSettings);

        if (_defaultLanguage != null)
            await _workContext.SetWorkingLanguageAsync(_defaultLanguage);

        var euro = await _currencyService.GetCurrencyByCodeAsync("EUR");
        euro.CustomFormatting = _euroCustomFormatting;
        euro.DisplayLocale = _euroDisplayLocale;
        euro.Published = _euroPublished;
        await _currencyService.UpdateCurrencyAsync(euro);

        if (_customerCurrencyId > 0)
        {
            _customer.CurrencyId = _customerCurrencyId;
            await GetService<ICustomerService>().UpdateCustomerAsync(_customer);
        }
    }

    [Test]
    public async Task CanPrepareBillingAddressModel()
    {
        var model = new CheckoutBillingAddressModel();
        await _checkoutModelFactory.PrepareBillingAddressModelAsync(model, _cart);

        model.ShipToSameAddressAllowed.Should().Be(_shippingSettings.ShipToSameAddress &&
                                                   await _shoppingCartService.ShoppingCartRequiresShippingAsync(_cart));
        model.ShipToSameAddress.Should().Be(!_orderSettings.DisableBillingAddressCheckoutStep);

        model.ExistingAddresses.Any().Should().BeTrue();
        model.ExistingAddresses.Count.Should().Be(1);

        model.InvalidExistingAddresses.Any().Should().BeTrue();
        model.InvalidExistingAddresses.Count.Should().Be(1);

        model.BillingNewAddress.Should().NotBeNull();
    }

    [Test]
    public async Task CanPrepareShippingAddressModel()
    {
        var model = new CheckoutShippingAddressModel();
        await _checkoutModelFactory.PrepareShippingAddressModelAsync(model, _cart);

        model.DisplayPickupInStore.Should().Be(!_orderSettings.DisplayPickupInStoreOnShippingMethodPage);

        model.ExistingAddresses.Any().Should().BeTrue();
        model.ExistingAddresses.Count.Should().Be(1);

        model.InvalidExistingAddresses.Any().Should().BeTrue();
        model.InvalidExistingAddresses.Count.Should().Be(1);

        model.ShippingNewAddress.Should().NotBeNull();
    }

    [Test]
    public async Task CanPrepareShippingMethodModel()
    {
        var model = await _checkoutModelFactory.PrepareShippingMethodModelAsync(_cart, await _addressService.GetAddressByIdAsync(1));
        model.DisplayPickupInStore.Should().Be(_orderSettings.DisplayPickupInStoreOnShippingMethodPage);
        model.Warnings.Any().Should().BeFalse();
        model.ShippingMethods.Any().Should().BeTrue();
        model.ShippingMethods.Count.Should().Be(2);
    }

    [Test]
    public async Task CanPreparePaymentMethodModel()
    {
        var model = await _checkoutModelFactory.PreparePaymentMethodModelAsync(_cart, 0);

        model.DisplayRewardPoints.Should().BeTrue();
        model.PaymentMethods.Count.Should().Be(1);
        model.RewardPointsToUseAmount.Should().Be("$1,944.90");
        model.RewardPointsToUse.Should().Be(1945);
        model.RewardPointsBalance.Should().Be(10000);
        model.RewardPointsEnoughToPayForOrder.Should().BeTrue();
        model.UseRewardPoints.Should().BeFalse();
    }

    [Test]
    public async Task PreparePaymentMethodModelShouldDependOnSettings()
    {
        var model = await _checkoutModelFactory.PreparePaymentMethodModelAsync(_cart, 0);
        model.DisplayRewardPoints.Should().BeTrue();
        model.RewardPointsToUse.Should().Be(1945);
        model.RewardPointsBalance.Should().Be(10000);

        _rewardPointsSettings.Enabled = false;
        await _settingService.SaveSettingAsync(_rewardPointsSettings);

        model = await GetService<ICheckoutModelFactory>().PreparePaymentMethodModelAsync(_cart, 0);

        _rewardPointsSettings.Enabled = true;
        await _settingService.SaveSettingAsync(_rewardPointsSettings);

        model.DisplayRewardPoints.Should().BeFalse();
        model.RewardPointsBalance.Should().Be(0);
    }

    [Test]
    public async Task CanPreparePaymentInfoModel()
    {
        var model = await _checkoutModelFactory.PreparePaymentInfoModelAsync(_paymentMethod);

        model.PaymentViewComponent.Should().Be(_paymentMethod.GetPublicViewComponent());
        model.DisplayOrderTotals.Should().Be(_orderSettings.OnePageCheckoutDisplayOrderTotalsOnPaymentInfoTab);
    }

    [Test]
    public async Task CanPrepareConfirmOrderModel()
    {
        var model = await _checkoutModelFactory.PrepareConfirmOrderModelAsync(_cart);

        model.TermsOfServiceOnOrderConfirmPage.Should().Be(_orderSettings.TermsOfServiceOnOrderConfirmPage);
        model.TermsOfServicePopup.Should().Be(_commonSettings.PopupForTermsOfServiceLinks);
        model.MinOrderTotalWarning.Should().BeNullOrEmpty();
        model.Warnings.Any().Should().BeFalse();
    }

    [Test]
    public async Task CanPrepareCheckoutCompletedModel()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var model = await _checkoutModelFactory.PrepareCheckoutCompletedModelAsync(order);

        model.OrderId.Should().Be(order.Id);
        model.OnePageCheckoutEnabled.Should().Be(_orderSettings.OnePageCheckoutEnabled);
        model.CustomOrderNumber.Should().Be(order.CustomOrderNumber);
    }

    [Test]
    public void PrepareCheckoutCompletedModelShouldRaiseExceptionIfOrderIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _checkoutModelFactory.PrepareCheckoutCompletedModelAsync(null).Wait());
    }

    [Test]
    public async Task CanPrepareCheckoutProgressModel()
    {
        foreach (var step in Enum.GetValues(typeof(CheckoutProgressStep)).Cast<CheckoutProgressStep>())
        {
            var model = await _checkoutModelFactory.PrepareCheckoutProgressModelAsync(step);
            model.CheckoutProgressStep.Should().Be(step);
        }
    }

    [Test]
    public async Task CanPrepareOnePageCheckoutModel()
    {
        var model = await _checkoutModelFactory.PrepareOnePageCheckoutModelAsync(_cart);

        model.ShippingRequired.Should().Be(await _shoppingCartService.ShoppingCartRequiresShippingAsync(_cart));
        model.DisableBillingAddressCheckoutStep.Should().Be(_orderSettings.DisableBillingAddressCheckoutStep);
        model.BillingAddress.Should().NotBeNull();
    }

    [Test]
    public void PrepareOnePageCheckoutModelShouldRaiseExceptionIfCartIsNull()
    {
        Assert.Throws<AggregateException>(() =>
            _checkoutModelFactory.PrepareOnePageCheckoutModelAsync(null).Wait());
    }

    [Test]
    public async Task PrepareShippingMethodModelShouldFormatDesiredDeliveryDateUsingWorkingLanguageCulture()
    {
        var frenchLanguage = (await _languageService.GetAllLanguagesAsync())
            .FirstOrDefault(language => language.LanguageCulture == "fr-FR");

        if (frenchLanguage == null)
        {
            frenchLanguage = new Language
            {
                Name = "French",
                LanguageCulture = "fr-FR",
                UniqueSeoCode = "fr",
                Published = true,
                DisplayOrder = 99
            };
            await _languageService.InsertLanguageAsync(frenchLanguage);
        }

        try
        {
            await _workContext.SetWorkingLanguageAsync(frenchLanguage);

            _shippingSettings.AllowCustomerToChooseDeliveryDate = true;
            _shippingSettings.DeliveryDateRangeDays = 3;
            await _settingService.SaveSettingAsync(_shippingSettings);

            var model = await _checkoutModelFactory
                .PrepareShippingMethodModelAsync(_cart, await _addressService.GetAddressByIdAsync(1));

            var desiredDeliveryDate = model.ShippingMethods.First().DesiredDeliveryDate;
            desiredDeliveryDate.Enabled.Should().BeTrue();
            desiredDeliveryDate.AvailableDates.Should().NotBeEmpty();

            var firstDate = desiredDeliveryDate.AvailableDates.First();
            firstDate.Value.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
            firstDate.Text.Should().MatchRegex("(janvier|février|mars|avril|mai|juin|juillet|août|septembre|octobre|novembre|décembre)");
        }
        finally
        {
            _shippingSettings.AllowCustomerToChooseDeliveryDate = _allowCustomerToChooseDeliveryDate;
            _shippingSettings.DeliveryDateRangeDays = _deliveryDateRangeDays;
            await _settingService.SaveSettingAsync(_shippingSettings);

            if (_defaultLanguage != null)
                await _workContext.SetWorkingLanguageAsync(_defaultLanguage);
        }
    }

    [Test]
    public async Task PreparePaymentMethodModelShouldFormatAmountUsingFrenchCurrencyLocale()
    {
        var frenchLanguage = (await _languageService.GetAllLanguagesAsync())
            .FirstOrDefault(language => language.LanguageCulture == "fr-FR");

        if (frenchLanguage == null)
        {
            frenchLanguage = new Language
            {
                Name = "French",
                LanguageCulture = "fr-FR",
                UniqueSeoCode = "fr",
                Published = true,
                DisplayOrder = 99
            };
            await _languageService.InsertLanguageAsync(frenchLanguage);
        }

        var euro = await _currencyService.GetCurrencyByCodeAsync("EUR");
        euro.CustomFormatting = string.Empty;
        euro.DisplayLocale = "fr-FR";
        euro.Published = true;
        await _currencyService.UpdateCurrencyAsync(euro);

        try
        {
            await _workContext.SetWorkingLanguageAsync(frenchLanguage);
            await _workContext.SetWorkingCurrencyAsync(euro);

            var model = await _checkoutModelFactory.PreparePaymentMethodModelAsync(_cart, 0);

            model.RewardPointsToUseAmount.Should().Contain("€");
            model.RewardPointsToUseAmount.Should().MatchRegex(@"\d[\d\s]*,\d{2}\s*€");
        }
        finally
        {
            euro.CustomFormatting = _euroCustomFormatting;
            euro.DisplayLocale = _euroDisplayLocale;
            euro.Published = _euroPublished;
            await _currencyService.UpdateCurrencyAsync(euro);

            if (_defaultLanguage != null)
                await _workContext.SetWorkingLanguageAsync(_defaultLanguage);

            var primaryCurrency = await _currencyService.GetCurrencyByIdAsync(_customerCurrencyId > 0
                ? _customerCurrencyId
                : GetService<CurrencySettings>().PrimaryStoreCurrencyId);
            if (primaryCurrency != null)
                await _workContext.SetWorkingCurrencyAsync(primaryCurrency);
        }
    }

}