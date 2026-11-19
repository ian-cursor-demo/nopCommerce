namespace Nop.Plugin.Tax.NodeService.Models;

/// <summary>
/// Represents an address payload for the Node tax service
/// </summary>
public class TaxAddressModel
{
    public int CountryId { get; set; }

    public int StateProvinceId { get; set; }

    public string Zip { get; set; }
}

/// <summary>
/// Represents a customer payload for the Node tax service
/// </summary>
public class TaxCustomerModel
{
    public int? Id { get; set; }

    public bool IsTaxExempt { get; set; }

    public string VatNumberStatus { get; set; }
}

/// <summary>
/// Represents a product payload for the Node tax service
/// </summary>
public class TaxProductModel
{
    public bool IsTaxExempt { get; set; }
}

/// <summary>
/// Represents a tax rate request payload for the Node tax service
/// </summary>
public class TaxRateApiRequest
{
    public int StoreId { get; set; }

    public int TaxCategoryId { get; set; }

    public decimal Price { get; set; }

    public TaxAddressModel Address { get; set; }

    public TaxCustomerModel Customer { get; set; }

    public TaxProductModel Product { get; set; }

    public bool? CountryStateZipEnabled { get; set; }
}

/// <summary>
/// Represents a tax rate response payload from the Node tax service
/// </summary>
public class TaxRateApiResponse
{
    public bool Success { get; set; }

    public IList<string> Errors { get; set; } = new List<string>();

    public decimal TaxRate { get; set; }
}

/// <summary>
/// Represents a cart item payload for the Node tax service
/// </summary>
public class CartItemApiModel
{
    public int ProductId { get; set; }

    public int TaxCategoryId { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public bool IsTaxExempt { get; set; }
}

/// <summary>
/// Represents tax settings payload for the Node tax service
/// </summary>
public class TaxSettingsApiModel
{
    public bool PricesIncludeTax { get; set; }

    public bool ShippingIsTaxable { get; set; }

    public bool ShippingPriceIncludesTax { get; set; }

    public int ShippingTaxClassId { get; set; }

    public bool PaymentMethodAdditionalFeeIsTaxable { get; set; }

    public bool PaymentMethodAdditionalFeeIncludesTax { get; set; }

    public int PaymentMethodAdditionalFeeTaxClassId { get; set; }

    public bool? CountryStateZipEnabled { get; set; }
}

/// <summary>
/// Represents a tax total request payload for the Node tax service
/// </summary>
public class TaxTotalApiRequest
{
    public int StoreId { get; set; }

    public bool UsePaymentMethodAdditionalFee { get; set; } = true;

    public TaxCustomerModel Customer { get; set; }

    public IList<CartItemApiModel> CartItems { get; set; } = new List<CartItemApiModel>();

    public decimal ShippingPrice { get; set; }

    public decimal PaymentFee { get; set; }

    public TaxAddressModel Address { get; set; }

    public TaxSettingsApiModel Settings { get; set; }
}

/// <summary>
/// Represents a tax total response payload from the Node tax service
/// </summary>
public class TaxTotalApiResponse
{
    public bool Success { get; set; }

    public IList<string> Errors { get; set; } = new List<string>();

    public decimal TaxTotal { get; set; }

    public IDictionary<string, decimal> TaxRates { get; set; } = new Dictionary<string, decimal>();
}
