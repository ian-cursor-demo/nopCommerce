import type { FastifyReply, FastifyRequest } from 'fastify';
import type { AppConfig } from '../config.js';

export function createAuthHook(config: AppConfig) {
  return async function authHook(request: FastifyRequest, reply: FastifyReply): Promise<void> {
    if (!config.apiKey) {
      return;
    }

    const headerKey = request.headers['x-api-key'];
    const providedKey = Array.isArray(headerKey) ? headerKey[0] : headerKey;

    if (providedKey !== config.apiKey) {
      await reply.code(401).send({
        success: false,
        errors: ['Unauthorized'],
      });
    }
  };
}
