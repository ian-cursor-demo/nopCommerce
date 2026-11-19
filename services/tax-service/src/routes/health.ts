import type { FastifyInstance } from 'fastify';
import type { TaxRateRepository } from '../repositories/tax-rate.repository.js';

export async function registerHealthRoutes(
  app: FastifyInstance,
  repository: TaxRateRepository,
): Promise<void> {
  app.get('/health', async (_request, reply) => {
    try {
      await repository.connect();
      return reply.send({ status: 'ok' });
    } catch (error) {
      return reply.code(503).send({
        status: 'error',
        message: error instanceof Error ? error.message : 'Unknown error',
      });
    }
  });
}
