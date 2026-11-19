export interface TaxAddress {
  countryId: number;
  stateProvinceId: number;
  zip?: string;
}

export interface TaxCustomer {
  id?: number;
  isTaxExempt?: boolean;
  vatNumberStatus?: string;
}

export interface TaxProduct {
  isTaxExempt?: boolean;
}

export interface TaxRateRequestBody {
  storeId: number;
  taxCategoryId: number;
  price: number;
  address?: TaxAddress;
  customer?: TaxCustomer;
  product?: TaxProduct;
  countryStateZipEnabled?: boolean;
}

export interface CartItemRequest {
  productId: number;
  taxCategoryId: number;
  unitPrice: number;
  quantity: number;
  isTaxExempt?: boolean;
}

export interface TaxSettingsRequest {
  pricesIncludeTax?: boolean;
  shippingIsTaxable?: boolean;
  shippingPriceIncludesTax?: boolean;
  shippingTaxClassId?: number;
  paymentMethodAdditionalFeeIsTaxable?: boolean;
  paymentMethodAdditionalFeeIncludesTax?: boolean;
  paymentMethodAdditionalFeeTaxClassId?: number;
  countryStateZipEnabled?: boolean;
}

export interface TaxTotalRequestBody {
  storeId: number;
  usePaymentMethodAdditionalFee?: boolean;
  customer?: TaxCustomer;
  cartItems: CartItemRequest[];
  shippingPrice?: number;
  paymentFee?: number;
  address?: TaxAddress;
  settings?: TaxSettingsRequest;
}

export interface BaseNopResult {
  success: boolean;
  errors: string[];
}

export interface TaxRateResponse extends BaseNopResult {
  taxRate: number;
}

export interface TaxTotalResponse extends BaseNopResult {
  taxTotal: number;
  taxRates: Record<string, number>;
}

export interface TaxRateRecord {
  id: number;
  storeId: number;
  taxCategoryId: number;
  countryId: number;
  stateProvinceId: number;
  zip: string;
  percentage: number;
}
