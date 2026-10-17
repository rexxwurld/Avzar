import { z } from 'zod';

const EnvSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().min(1).max(65535).default(8081),
  HOST: z.string().min(1).default('0.0.0.0'),
  API_URL: z.string().url().optional(),
  DATABASE_URL: z.string().url().optional(),
  TRUST_PROXY_HEADERS: z.enum(['true', 'false']).default('false').transform((value) => value === 'true'),
  MAX_PROXY_BODY_BYTES: z.coerce.number().int().min(1024).max(100 * 1024 * 1024).default(25 * 1024 * 1024),
  UPSTREAM_TIMEOUT_MS: z.coerce.number().int().min(1000).max(120000).default(30000),
  LOG_LEVEL: z.enum(['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent']).default('info'),
});

export function loadEnv(source = process.env) {
  const parsed = EnvSchema.safeParse(source);
  if (!parsed.success) {
    const details = parsed.error.issues.map((issue) => `${issue.path.join('.')}: ${issue.message}`).join('; ');
    throw new Error(`Invalid backend-node environment: ${details}`);
  }
  return parsed.data;
}
