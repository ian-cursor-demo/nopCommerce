import Fastify from 'fastify';
import { loadConfig } from './config.js';
import { createAuthHook } from './middleware/auth.js';
import { TaxRateRepository } from './repositories/tax-rate.repository.js';
import { RateCalculator } from './services/rate-calculator.js';
import { TotalCalculator } from './services/total-calculator.js';
import { registerHealthRoutes } from './routes/health.js';
import { registerTaxRateRoutes } from './routes/tax-rate.js';
import { registerTaxTotalRoutes } from './routes/tax-total.js';

export async function buildApp() {
  const config = loadConfig();
  const app = Fastify({ logger: true });
  const repository = new TaxRateRepository(config);
  const rateCalculator = new RateCalculator(repository);
  const totalCalculator = new TotalCalculator(rateCalculator);
  const authHook = createAuthHook(config);

  app.addHook('onRequest', async (request, reply) => {
    if (request.url === '/health') {
      return;
    }

    await authHook(request, reply);
  });

  await registerHealthRoutes(app, repository);
  await registerTaxRateRoutes(app, rateCalculator);
  await registerTaxTotalRoutes(app, totalCalculator);

  app.addHook('onClose', async () => {
    await repository.close();
  });

  return { app, config, repository };
}

async function start(): Promise<void> {
  const { app, config } = await buildApp();

  try {
    await app.listen({ port: config.port, host: '0.0.0.0' });
  } catch (error) {
    app.log.error(error);
    process.exit(1);
  }
}

import { fileURLToPath } from 'node:url';

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  await start();
}
