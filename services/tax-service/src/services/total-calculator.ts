import type { RateCalculator } from './rate-calculator.js';
import type {
  TaxTotalRequestBody,
  TaxTotalResponse,
} from '../models/types.js';
import { calculateTaxAmount, round, taxRateKey } from '../utils/decimal.js';

export class TotalCalculator {
  constructor(private readonly rateCalculator: RateCalculator) {}

  async calculate(request: TaxTotalRequestBody): Promise<TaxTotalResponse> {
    const settings = request.settings ?? {};
    const taxRates = new Map<string, number>();
    let taxTotal = 0;

    if (request.customer?.isTaxExempt) {
      return this.buildResponse(0, taxRates);
    }

    for (const item of request.cartItems) {
      if (item.isTaxExempt) {
        continue;
      }

      const lineTotal = round(item.unitPrice * item.quantity);
      const rateResult = await this.rateCalculator.calculate({
        storeId: request.storeId,
        taxCategoryId: item.taxCategoryId,
        price: lineTotal,
        address: request.address,
        customer: request.customer,
        countryStateZipEnabled: settings.countryStateZipEnabled,
      });

      if (!rateResult.success) {
        return {
          success: false,
          errors: rateResult.errors,
          taxTotal: 0,
          taxRates: {},
        };
      }

      const taxValue = calculateTaxAmount(
        lineTotal,
        rateResult.taxRate,
        settings.pricesIncludeTax ?? false,
      );

      if (rateResult.taxRate > 0 && taxValue > 0) {
        this.addTaxRate(taxRates, rateResult.taxRate, taxValue);
        taxTotal = round(taxTotal + taxValue);
      }
    }

    if (settings.shippingIsTaxable && request.shippingPrice && request.shippingPrice > 0) {
      const shippingTax = await this.calculateComponentTax(
        request,
        settings,
        request.shippingPrice,
        settings.shippingTaxClassId ?? 0,
        settings.shippingPriceIncludesTax ?? false,
      );

      if (shippingTax.rate > 0 && shippingTax.amount > 0) {
        this.addTaxRate(taxRates, shippingTax.rate, shippingTax.amount);
        taxTotal = round(taxTotal + shippingTax.amount);
      }
    }

    if (
      request.usePaymentMethodAdditionalFee !== false &&
      settings.paymentMethodAdditionalFeeIsTaxable &&
      request.paymentFee &&
      request.paymentFee > 0
    ) {
      const paymentTax = await this.calculateComponentTax(
        request,
        settings,
        request.paymentFee,
        settings.paymentMethodAdditionalFeeTaxClassId ?? 0,
        settings.paymentMethodAdditionalFeeIncludesTax ?? false,
      );

      if (paymentTax.rate > 0 && paymentTax.amount > 0) {
        this.addTaxRate(taxRates, paymentTax.rate, paymentTax.amount);
        taxTotal = round(taxTotal + paymentTax.amount);
      }
    }

    if (taxRates.size === 0) {
      taxRates.set(taxRateKey(0), 0);
    }

    if (taxTotal < 0) {
      taxTotal = 0;
    }

    return this.buildResponse(taxTotal, taxRates);
  }

  private async calculateComponentTax(
    request: TaxTotalRequestBody,
    settings: NonNullable<TaxTotalRequestBody['settings']>,
    amount: number,
    taxCategoryId: number,
    priceIncludesTax: boolean,
  ): Promise<{ rate: number; amount: number }> {
    const rateResult = await this.rateCalculator.calculate({
      storeId: request.storeId,
      taxCategoryId,
      price: amount,
      address: request.address,
      customer: request.customer,
      countryStateZipEnabled: settings.countryStateZipEnabled,
    });

    if (!rateResult.success) {
      return { rate: 0, amount: 0 };
    }

    const taxAmount = calculateTaxAmount(amount, rateResult.taxRate, priceIncludesTax);
    return { rate: rateResult.taxRate, amount: taxAmount };
  }

  private addTaxRate(
    taxRates: Map<string, number>,
    rate: number,
    amount: number,
  ): void {
    const key = taxRateKey(rate);
    taxRates.set(key, round((taxRates.get(key) ?? 0) + amount));
  }

  private buildResponse(
    taxTotal: number,
    taxRates: Map<string, number>,
  ): TaxTotalResponse {
    const serializedRates: Record<string, number> = {};
    for (const [rate, amount] of taxRates.entries()) {
      serializedRates[rate] = amount;
    }

    return {
      success: true,
      errors: [],
      taxTotal,
      taxRates: serializedRates,
    };
  }
}
