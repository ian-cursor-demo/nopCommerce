import sql from 'mssql';
import NodeCache from 'node-cache';
import type { AppConfig } from '../config.js';
import type { TaxRateRecord } from '../models/types.js';

const FIXED_RATE_PREFIX = 'Tax.TaxProvider.FixedOrByCountryStateZip.TaxCategoryId';

export class TaxRateRepository {
  private pool: sql.ConnectionPool | null = null;
  private readonly cache: NodeCache;

  constructor(private readonly config: AppConfig) {
    this.cache = new NodeCache({ stdTTL: config.cacheTtlSeconds, checkperiod: 60 });
  }

  async connect(): Promise<void> {
    if (this.pool?.connected) {
      return;
    }

    this.pool = await sql.connect(this.config.databaseUrl);
  }

  async close(): Promise<void> {
    if (this.pool) {
      await this.pool.close();
      this.pool = null;
    }
  }

  async getAllTaxRates(): Promise<TaxRateRecord[]> {
    const cacheKey = 'all-tax-rates';
    const cached = this.cache.get<TaxRateRecord[]>(cacheKey);
    if (cached) {
      return cached;
    }

    await this.connect();
    const result = await this.pool!.request().query(`
      SELECT Id AS id,
             StoreId AS storeId,
             TaxCategoryId AS taxCategoryId,
             CountryId AS countryId,
             StateProvinceId AS stateProvinceId,
             ISNULL(Zip, '') AS zip,
             Percentage AS percentage
      FROM TaxRate
      ORDER BY StoreId, CountryId, StateProvinceId, Zip, TaxCategoryId
    `);

    const records = result.recordset as TaxRateRecord[];
    this.cache.set(cacheKey, records);
    return records;
  }

  async getFixedRate(taxCategoryId: number): Promise<number> {
    const cacheKey = `fixed-rate:${taxCategoryId}`;
    const cached = this.cache.get<number>(cacheKey);
    if (cached !== undefined) {
      return cached;
    }

    await this.connect();
    const settingName = `${FIXED_RATE_PREFIX}${taxCategoryId}`;
    const result = await this.pool!.request()
      .input('name', sql.NVarChar, settingName)
      .query(`
        SELECT TOP 1 Value
        FROM Setting
        WHERE Name = @name
      `);

    const value = result.recordset[0]?.Value ?? '0';
    const rate = Number.parseFloat(value) || 0;
    this.cache.set(cacheKey, rate);
    return rate;
  }

  async isCountryStateZipEnabled(requestOverride?: boolean): Promise<boolean> {
    if (requestOverride !== undefined) {
      return requestOverride;
    }

    if (!this.config.countryStateZipEnabled) {
      return false;
    }

    const cacheKey = 'country-state-zip-enabled';
    const cached = this.cache.get<boolean>(cacheKey);
    if (cached !== undefined) {
      return cached;
    }

    await this.connect();
    const result = await this.pool!.request().query(`
      SELECT TOP 1 Value
      FROM Setting
      WHERE Name = 'FixedOrByCountryStateZipTaxSettings.CountryStateZipEnabled'
    `);

    const value = `${result.recordset[0]?.Value ?? 'False'}`.toLowerCase();
    const enabled = value === 'true' || value === '1';
    this.cache.set(cacheKey, enabled);
    return enabled;
  }

  clearCache(): void {
    this.cache.flushAll();
  }
}
