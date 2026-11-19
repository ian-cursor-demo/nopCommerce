import type { TaxRateRepository } from '../repositories/tax-rate.repository.js';
import type { TaxAddress, TaxRateRequestBody, TaxRateResponse } from '../models/types.js';
import { round } from '../utils/decimal.js';

export class RateCalculator {
  constructor(private readonly repository: TaxRateRepository) {}

  async calculate(request: TaxRateRequestBody): Promise<TaxRateResponse> {
    const countryStateZipEnabled = await this.repository.isCountryStateZipEnabled(
      request.countryStateZipEnabled,
    );

    if (!countryStateZipEnabled) {
      const taxRate = await this.repository.getFixedRate(request.taxCategoryId);
      return { success: true, errors: [], taxRate };
    }

    if (!request.address) {
      return { success: false, errors: ['Address is not set'], taxRate: 0 };
    }

    const taxRate = await this.lookupGeoRate(
      request.storeId,
      request.taxCategoryId,
      request.address,
    );

    return { success: true, errors: [], taxRate };
  }

  private async lookupGeoRate(
    storeId: number,
    taxCategoryId: number,
    address: TaxAddress,
  ): Promise<number> {
    const allTaxRates = await this.repository.getAllTaxRates();
    const zip = address.zip?.trim() ?? '';

    const existingRates = allTaxRates.filter(
      (rate) => rate.countryId === address.countryId && rate.taxCategoryId === taxCategoryId,
    );

    const matchedByStore = existingRates.filter(
      (rate) => storeId === rate.storeId || rate.storeId === 0,
    );

    const matchedByStateProvince = matchedByStore.filter(
      (rate) => address.stateProvinceId === rate.stateProvinceId || rate.stateProvinceId === 0,
    );

    const matchedByZip = matchedByStateProvince.filter(
      (rate) => !rate.zip?.trim() || rate.zip.localeCompare(zip, undefined, { sensitivity: 'accent' }) === 0,
    );

    const foundRecord = matchedByZip
      .sort((left, right) => {
        if (left.storeId === 0 && right.storeId !== 0) {
          return 1;
        }

        if (left.storeId !== 0 && right.storeId === 0) {
          return -1;
        }

        if (left.stateProvinceId === 0 && right.stateProvinceId !== 0) {
          return 1;
        }

        if (left.stateProvinceId !== 0 && right.stateProvinceId === 0) {
          return -1;
        }

        if (!left.zip && right.zip) {
          return 1;
        }

        if (left.zip && !right.zip) {
          return -1;
        }

        return 0;
      })[0];

    return foundRecord ? round(foundRecord.percentage) : 0;
  }
}
