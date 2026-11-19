import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { RateCalculator } from '../src/services/rate-calculator.js';
import { TotalCalculator } from '../src/services/total-calculator.js';
import type { TaxRateRecord } from '../src/models/types.js';

class InMemoryTaxRateRepository {
  constructor(
    private readonly rates: TaxRateRecord[],
    private readonly fixedRates: Record<number, number> = {},
    private readonly countryStateZipEnabled = true,
  ) {}

  async getAllTaxRates(): Promise<TaxRateRecord[]> {
    return this.rates;
  }

  async getFixedRate(taxCategoryId: number): Promise<number> {
    return this.fixedRates[taxCategoryId] ?? 0;
  }

  async isCountryStateZipEnabled(requestOverride?: boolean): Promise<boolean> {
    if (requestOverride !== undefined) {
      return requestOverride;
    }

    return this.countryStateZipEnabled;
  }

  async connect(): Promise<void> {}

  async close(): Promise<void> {}
}

describe('RateCalculator', () => {
  it('returns fixed rate when country/state/zip mode is disabled', async () => {
    const repository = new InMemoryTaxRateRepository([], { 2: 7.5 }, false);
    const calculator = new RateCalculator(repository as never);

    const result = await calculator.calculate({
      storeId: 1,
      taxCategoryId: 2,
      price: 100,
      address: { countryId: 1, stateProvinceId: 5, zip: '90210' },
    });

    assert.equal(result.success, true);
    assert.equal(result.taxRate, 7.5);
  });

  it('matches the most specific geo tax rate', async () => {
    const repository = new InMemoryTaxRateRepository([
      {
        id: 1,
        storeId: 0,
        taxCategoryId: 2,
        countryId: 1,
        stateProvinceId: 0,
        zip: '',
        percentage: 5,
      },
      {
        id: 2,
        storeId: 1,
        taxCategoryId: 2,
        countryId: 1,
        stateProvinceId: 5,
        zip: '90210',
        percentage: 8.25,
      },
    ]);
    const calculator = new RateCalculator(repository as never);

    const result = await calculator.calculate({
      storeId: 1,
      taxCategoryId: 2,
      price: 100,
      address: { countryId: 1, stateProvinceId: 5, zip: '90210' },
    });

    assert.equal(result.success, true);
    assert.equal(result.taxRate, 8.25);
  });

  it('returns an error when address is missing in geo mode', async () => {
    const repository = new InMemoryTaxRateRepository([]);
    const calculator = new RateCalculator(repository as never);

    const result = await calculator.calculate({
      storeId: 1,
      taxCategoryId: 2,
      price: 100,
    });

    assert.equal(result.success, false);
    assert.deepEqual(result.errors, ['Address is not set']);
  });
});

describe('TotalCalculator', () => {
  it('aggregates cart, shipping, and payment tax', async () => {
    const repository = new InMemoryTaxRateRepository([], { 2: 10, 3: 10 });
    const rateCalculator = new RateCalculator(repository as never);
    const calculator = new TotalCalculator(rateCalculator);

    const result = await calculator.calculate({
      storeId: 1,
      cartItems: [
        {
          productId: 1,
          taxCategoryId: 2,
          unitPrice: 50,
          quantity: 2,
        },
      ],
      shippingPrice: 10,
      paymentFee: 5,
      settings: {
        pricesIncludeTax: false,
        shippingIsTaxable: true,
        shippingTaxClassId: 3,
        paymentMethodAdditionalFeeIsTaxable: true,
        paymentMethodAdditionalFeeTaxClassId: 3,
        countryStateZipEnabled: false,
      },
    });

    assert.equal(result.success, true);
    assert.equal(result.taxTotal, 11.5);
    assert.equal(result.taxRates['10'], 11.5);
  });

  it('returns zero tax for tax-exempt customers', async () => {
    const repository = new InMemoryTaxRateRepository([], { 2: 10 });
    const rateCalculator = new RateCalculator(repository as never);
    const calculator = new TotalCalculator(rateCalculator);

    const result = await calculator.calculate({
      storeId: 1,
      customer: { isTaxExempt: true },
      cartItems: [
        {
          productId: 1,
          taxCategoryId: 2,
          unitPrice: 100,
          quantity: 1,
        },
      ],
      settings: { countryStateZipEnabled: false },
    });

    assert.equal(result.success, true);
    assert.equal(result.taxTotal, 0);
  });
});
