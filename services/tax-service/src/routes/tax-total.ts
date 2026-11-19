import type { FastifyInstance } from 'fastify';
import type { TotalCalculator } from '../services/total-calculator.js';
import type { TaxTotalRequestBody } from '../models/types.js';

export async function registerTaxTotalRoutes(
  app: FastifyInstance,
  totalCalculator: TotalCalculator,
): Promise<void> {
  app.post<{ Body: TaxTotalRequestBody }>('/v1/tax/total', async (request, reply) => {
    const body = request.body;

    if (body.storeId === undefined || !Array.isArray(body.cartItems)) {
      return reply.code(400).send({
        success: false,
        errors: ['storeId and cartItems are required'],
        taxTotal: 0,
        taxRates: {},
      });
    }

    const result = await totalCalculator.calculate(body);
    const statusCode = result.success ? 200 : 400;
    return reply.code(statusCode).send(result);
  });
}
