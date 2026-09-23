using AwesomeAssertions;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Data;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Stores;
using Nop.Tests.Nop.Services.Tests.Payments;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Orders;

[TestFixture]
public class PlaceOrderTests : ServiceTest
{
    private const string FixedRateShippingMethod = "FixedRateTestShippingRateComputationMethod";
    private const string TestPaymentMethodSystemName = "Payments.TestMethod";
    private const string OrderSubtotalDiscountName = "Discount 1";

    // Product tax on FR_451_RB x 2 + FIRST_PRP x 3 is 20.7 before shipping.
    // Shipping is not taxable (install default), so the saved tax stays 20.7.
    // Shipping excl tax and OrderTotal are pinned from the current PlaceOrderAsync result.
    private const decimal SubtotalExclTax = 207M;
    private const decimal OrderTax = 20.7M;
    private const decimal ShippingExclTax = 10M;
    private const decimal OrderTotal = 237.7M;

    // The $3 order-subtotal discount also removes $0.30 of product tax (20.7 -> 20.4).
    // Shipping stays at the undiscounted amount.
    private const decimal SubtotalDiscountExclTax = 3M;
    private const decimal OrderTaxWithSubtotalDiscount = 20.4M;
    private const decimal OrderTotalWithSubtotalDiscount = 234.4M;

    private const decimal FreeShippingOrderTotal = 227.7M;

    private IOrderProcessingService _orderProcessingService;
    private IOrderService _orderService;
    private IShoppingCartService _shoppingCartService;
    private IProductService _productService;
    private ICustomerService _customerService;
    private IDiscountService _discountService;
    private ISettingService _settingService;
    private IGenericAttributeService _genericAttributeService;
    private IStoreService _storeService;
    private ShippingSettings _shippingSettings;
    private PaymentSettings _paymentSettings;
    private TaxSettings _taxSettings;
    private Customer _customer;
    private int _storeId;
    private List<string> _shippingMethods;
    private List<string> _paymentMethods;
    private bool _shippingIsTaxable;
    private string _activeTaxProvider;
    private decimal _additionalHandlingFee;
    private readonly List<int> _placedOrderIds = new();

    [SetUp]
    public async Task SetUp()
    {
        _orderProcessingService = GetService<IOrderProcessingService>();
        _orderService = GetService<IOrderService>();
        _shoppingCartService = GetService<IShoppingCartService>();
        _productService = GetService<IProductService>();
        _customerService = GetService<ICustomerService>();
        _discountService = GetService<IDiscountService>();
        _settingService = GetService<ISettingService>();
        _genericAttributeService = GetService<IGenericAttributeService>();
        _storeService = GetService<IStoreService>();

        _shippingSettings = GetService<ShippingSettings>();
        _paymentSettings = GetService<PaymentSettings>();
        _taxSettings = GetService<TaxSettings>();

        _shippingMethods = _shippingSettings.ActiveShippingRateComputationMethodSystemNames.ToList();
        _paymentMethods = _paymentSettings.ActivePaymentMethodSystemNames.ToList();
        _shippingIsTaxable = _taxSettings.ShippingIsTaxable;
        _activeTaxProvider = _taxSettings.ActiveTaxProviderSystemName;
        _additionalHandlingFee = TestPaymentMethod.AdditionalHandlingFee;

        _shippingSettings.ActiveShippingRateComputationMethodSystemNames.Clear();
        _shippingSettings.ActiveShippingRateComputationMethodSystemNames.Add(FixedRateShippingMethod);
        await _settingService.SaveSettingAsync(_shippingSettings);

        if (!_paymentSettings.ActivePaymentMethodSystemNames.Contains(TestPaymentMethodSystemName))
            _paymentSettings.ActivePaymentMethodSystemNames.Add(TestPaymentMethodSystemName);
        await _settingService.SaveSettingAsync(_paymentSettings);

        _taxSettings.ActiveTaxProviderSystemName = "FixedTaxRateTest";
        _taxSettings.ShippingIsTaxable = false;
        await _settingService.SaveSettingAsync(_taxSettings);

        TestPaymentMethod.AdditionalHandlingFee = decimal.Zero;

        _customer = await _customerService.GetCustomerByEmailAsync(NopTestsDefaults.AdminEmail);
        _storeId = (await _storeService.GetAllStoresAsync()).First().Id;

        await SetSampleShippingFlagsAsync();
        await _shoppingCartService.ClearShoppingCartAsync(_customer, _storeId);
        await ClearCheckoutAttributesAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var orderId in _placedOrderIds.ToList())
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order != null)
                await _orderProcessingService.DeleteOrderAsync(order);
        }

        _placedOrderIds.Clear();

        await DeleteOrderSubtotalDiscountsAsync();

        if (_customer != null)
        {
            await _shoppingCartService.ClearShoppingCartAsync(_customer, _storeId);
            await ClearCheckoutAttributesAsync();
        }

        await SetSampleShippingFlagsAsync();

        if (_shippingSettings != null && _shippingMethods != null)
        {
            _shippingSettings.ActiveShippingRateComputationMethodSystemNames.Clear();
            _shippingSettings.ActiveShippingRateComputationMethodSystemNames.AddRange(_shippingMethods);
            await _settingService.SaveSettingAsync(_shippingSettings);
        }

        if (_paymentSettings != null && _paymentMethods != null)
        {
            _paymentSettings.ActivePaymentMethodSystemNames.Clear();
            _paymentSettings.ActivePaymentMethodSystemNames.AddRange(_paymentMethods);
            await _settingService.SaveSettingAsync(_paymentSettings);
        }

        if (_taxSettings != null)
        {
            _taxSettings.ShippingIsTaxable = _shippingIsTaxable;
            _taxSettings.ActiveTaxProviderSystemName = _activeTaxProvider;
            await _settingService.SaveSettingAsync(_taxSettings);
        }

        TestPaymentMethod.AdditionalHandlingFee = _additionalHandlingFee;
    }

    [Test]
    public async Task PlaceOrder_SampleCart_PinsShippingAndOrderTotal()
    {
        var order = await PlaceSampleOrderAsync();

        order.OrderSubtotalExclTax.Should().Be(SubtotalExclTax);
        order.OrderTax.Should().Be(OrderTax);
        order.OrderShippingExclTax.Should().Be(ShippingExclTax);
        order.OrderTotal.Should().Be(OrderTotal);
    }

    [Test]
    public async Task PlaceOrder_OrderSubtotalDiscount_MovesTotalByThatDiscountOnly()
    {
        await DeleteOrderSubtotalDiscountsAsync();
        await _discountService.InsertDiscountAsync(new Discount
        {
            IsActive = true,
            Name = OrderSubtotalDiscountName,
            DiscountType = DiscountType.AssignedToOrderSubTotal,
            DiscountAmount = SubtotalDiscountExclTax,
            DiscountLimitation = DiscountLimitationType.Unlimited
        });

        var order = await PlaceSampleOrderAsync();

        order.OrderSubtotalExclTax.Should().Be(SubtotalExclTax);
        order.OrderSubTotalDiscountExclTax.Should().Be(SubtotalDiscountExclTax);
        order.OrderShippingExclTax.Should().Be(ShippingExclTax);
        order.OrderTax.Should().Be(OrderTaxWithSubtotalDiscount);
        order.OrderTotal.Should().Be(OrderTotalWithSubtotalDiscount);

        var totalDelta = OrderTotal - order.OrderTotal;
        var taxDelta = OrderTax - order.OrderTax;
        totalDelta.Should().Be(SubtotalDiscountExclTax + taxDelta);
    }

    [Test]
    public async Task PlaceOrder_AllItemsFreeShipping_ShippingIsZeroAndTotalMatches()
    {
        var book = await _productService.GetProductBySkuAsync("FR_451_RB");
        var pie = await _productService.GetProductBySkuAsync("FIRST_PRP");
        book.IsFreeShipping = true;
        pie.IsFreeShipping = true;
        await _productService.UpdateProductAsync(book);
        await _productService.UpdateProductAsync(pie);

        var order = await PlaceSampleOrderAsync();

        order.OrderSubtotalExclTax.Should().Be(SubtotalExclTax);
        order.OrderTax.Should().Be(OrderTax);
        order.OrderShippingExclTax.Should().Be(0M);
        order.OrderTotal.Should().Be(FreeShippingOrderTotal);
        (OrderTotal - order.OrderTotal).Should().Be(ShippingExclTax);
    }

    private async Task<Order> PlaceSampleOrderAsync()
    {
        var book = await _productService.GetProductBySkuAsync("FR_451_RB");
        var pie = await _productService.GetProductBySkuAsync("FIRST_PRP");

        var bookWarnings = await _shoppingCartService.AddToCartAsync(_customer, book, ShoppingCartType.ShoppingCart, _storeId, quantity: 2);
        bookWarnings.Should().BeEmpty();
        var pieWarnings = await _shoppingCartService.AddToCartAsync(_customer, pie, ShoppingCartType.ShoppingCart, _storeId, quantity: 3);
        pieWarnings.Should().BeEmpty();

        // Gift wrapping is required for shippable products. "No" is the preselected value and adds no price.
        var checkoutAttributes = GetService<IAttributeService<CheckoutAttribute, CheckoutAttributeValue>>();
        var giftWrapping = (await checkoutAttributes.GetAllAttributesAsync()).First(attribute => attribute.Name == "Gift wrapping");
        var noValue = (await checkoutAttributes.GetAttributeValuesAsync(giftWrapping.Id)).First(value => value.Name == "No");
        var checkoutAttributesXml = GetService<IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>>()
            .AddAttribute(string.Empty, giftWrapping, noValue.Id.ToString());

        await _genericAttributeService.SaveAttributeAsync(_customer, NopCustomerDefaults.CheckoutAttributes, checkoutAttributesXml, _storeId);
        await _genericAttributeService.SaveAttributeAsync(_customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, TestPaymentMethodSystemName, _storeId);
        await _genericAttributeService.SaveAttributeAsync(_customer, NopCustomerDefaults.UseRewardPointsDuringCheckoutAttribute, false, _storeId);
        await _genericAttributeService.SaveAttributeAsync<ShippingOption>(_customer, NopCustomerDefaults.SelectedShippingOptionAttribute, null, _storeId);

        var result = await _orderProcessingService.PlaceOrderAsync(new ProcessPaymentRequest
        {
            StoreId = _storeId,
            CustomerId = _customer.Id,
            PaymentMethodSystemName = TestPaymentMethodSystemName
        });

        result.Errors.Should().BeEmpty();
        result.Success.Should().BeTrue();
        result.PlacedOrder.Should().NotBeNull();
        _placedOrderIds.Add(result.PlacedOrder.Id);

        return await _orderService.GetOrderByIdAsync(result.PlacedOrder.Id);
    }

    private async Task SetSampleShippingFlagsAsync()
    {
        var book = await _productService.GetProductBySkuAsync("FR_451_RB");
        book.AdditionalShippingCharge = 0M;
        book.IsFreeShipping = true;
        await _productService.UpdateProductAsync(book);

        var pie = await _productService.GetProductBySkuAsync("FIRST_PRP");
        pie.AdditionalShippingCharge = 0M;
        pie.IsFreeShipping = false;
        await _productService.UpdateProductAsync(pie);
    }

    private async Task ClearCheckoutAttributesAsync()
    {
        await _genericAttributeService.SaveAttributeAsync<string>(_customer, NopCustomerDefaults.CheckoutAttributes, null, _storeId);
        await _genericAttributeService.SaveAttributeAsync<string>(_customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, null, _storeId);
        await _genericAttributeService.SaveAttributeAsync<ShippingOption>(_customer, NopCustomerDefaults.SelectedShippingOptionAttribute, null, _storeId);
    }

    private async Task DeleteOrderSubtotalDiscountsAsync()
    {
        var discounts = GetService<IRepository<Discount>>().Table.Where(discount => discount.Name == OrderSubtotalDiscountName).ToList();
        foreach (var discount in discounts)
        {
            var history = await _discountService.GetAllDiscountUsageHistoryAsync(discountId: discount.Id);
            foreach (var usage in history)
                await _discountService.DeleteDiscountUsageHistoryAsync(usage);

            await _discountService.DeleteDiscountAsync(discount);
        }
    }
}
