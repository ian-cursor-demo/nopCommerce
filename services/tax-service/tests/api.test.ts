import assert from 'node:assert/strict';
import { describe, it, before, after } from 'node:test';
import Fastify from 'fastify';
import { registerHealthRoutes } from '../src/routes/health.js';
import { registerTaxRateRoutes } from '../src/routes/tax-rate.js';
import { registerTaxTotalRoutes } from '../src/routes/tax-total.js';
import { RateCalculator } from '../src/services/rate-calculator.js';
import { TotalCalculator } from '../src/services/total-calculator.js';
import type { TaxRateRecord } from '../src/models/types.js';

class InMemoryTaxRateRepository {
  constructor(
    private readonly rates: TaxRateRecord[] = [],
    private readonly fixedRates: Record<number, number> = { 2: 10 },
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

    return false;
  }

  async connect(): Promise<void> {}

  async close(): Promise<void> {}
}

describe('Tax API routes', () => {
  const repository = new InMemoryTaxRateRepository();
  const rateCalculator = new RateCalculator(repository as never);
  const totalCalculator = new TotalCalculator(rateCalculator);
  const app = Fastify();

  before(async () => {
    await registerHealthRoutes(app, repository as never);
    await registerTaxRateRoutes(app, rateCalculator);
    await registerTaxTotalRoutes(app, totalCalculator);
    await app.ready();
  });

  after(async () => {
    await app.close();
  });

  it('GET /health returns ok', async () => {
    const response = await app.inject({ method: 'GET', url: '/health' });
    assert.equal(response.statusCode, 200);
    assert.deepEqual(response.json(), { status: 'ok' });
  });

  it('POST /v1/tax/rate returns tax rate', async () => {
    const response = await app.inject({
      method: 'POST',
      url: '/v1/tax/rate',
      payload: {
        storeId: 1,
        taxCategoryId: 2,
        price: 100,
      },
    });

    assert.equal(response.statusCode, 200);
    assert.deepEqual(response.json(), {
      success: true,
      errors: [],
      taxRate: 10,
    });
  });

  it('POST /v1/tax/total returns tax total', async () => {
    const response = await app.inject({
      method: 'POST',
      url: '/v1/tax/total',
      payload: {
        storeId: 1,
        cartItems: [
          {
            productId: 1,
            taxCategoryId: 2,
            unitPrice: 100,
            quantity: 1,
          },
        ],
        settings: { countryStateZipEnabled: false },
      },
    });

    assert.equal(response.statusCode, 200);
    assert.equal(response.json().taxTotal, 10);
  });
});
