export function round(value: number, digits = 4): number {
  const factor = 10 ** digits;
  return Math.round(value * factor) / factor;
}

export function calculateTaxAmount(
  price: number,
  taxRate: number,
  priceIncludesTax: boolean,
): number {
  if (taxRate <= 0 || price <= 0) {
    return 0;
  }

  if (priceIncludesTax) {
    return round(price - (price / (100 + taxRate)) * taxRate);
  }

  return round((price * taxRate) / 100);
}

export function taxRateKey(rate: number): string {
  return round(rate).toString();
}
