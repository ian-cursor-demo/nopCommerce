import type { FastifyInstance } from 'fastify';
import type { RateCalculator } from '../services/rate-calculator.js';
import type { TaxRateRequestBody } from '../models/types.js';

export async function registerTaxRateRoutes(
  app: FastifyInstance,
  rateCalculator: RateCalculator,
): Promise<void> {
  app.post<{ Body: TaxRateRequestBody }>('/v1/tax/rate', async (request, reply) => {
    const body = request.body;

    if (body.storeId === undefined || body.taxCategoryId === undefined || body.price === undefined) {
      return reply.code(400).send({
        success: false,
        errors: ['storeId, taxCategoryId, and price are required'],
        taxRate: 0,
      });
    }

    const result = await rateCalculator.calculate(body);
    const statusCode = result.success ? 200 : 400;
    return reply.code(statusCode).send(result);
  });
}
