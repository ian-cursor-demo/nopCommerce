export interface AppConfig {
  port: number;
  apiKey: string;
  databaseUrl: string;
  cacheTtlSeconds: number;
  countryStateZipEnabled: boolean;
}

function parseBoolean(value: string | undefined, defaultValue: boolean): boolean {
  if (value === undefined) {
    return defaultValue;
  }

  return value.toLowerCase() === 'true' || value === '1';
}

export function loadConfig(): AppConfig {
  const databaseUrl = process.env.DATABASE_URL?.trim();
  if (!databaseUrl) {
    throw new Error('DATABASE_URL is required');
  }

  return {
    port: Number.parseInt(process.env.PORT ?? '3000', 10),
    apiKey: process.env.API_KEY?.trim() ?? '',
    databaseUrl,
    cacheTtlSeconds: Number.parseInt(process.env.CACHE_TTL_SECONDS ?? '300', 10),
    countryStateZipEnabled: parseBoolean(process.env.COUNTRY_STATE_ZIP_ENABLED, true),
  };
}
