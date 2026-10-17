import { z } from 'zod';

const EnvSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().min(1).max(65535).default(8081),
  HOST: z.string().min(1).default('0.0.0.0'),
  DATABASE_URL: z.string().url().optional(),
  DATABASE_POOL_MAX: z.coerce.number().int().min(1).max(100).default(10),
  JWT_SIGNING_KEY: z.string().optional(),
  JWT_ISSUER: z.string().min(1).default('avzar-api'),
  JWT_AUDIENCE: z.string().min(1).default('avzar-mobile'),
  ACCESS_TOKEN_MINUTES: z.coerce.number().int().min(1).max(60).default(10),
  REFRESH_IDLE_DAYS: z.coerce.number().int().min(1).max(90).default(30),
  REFRESH_ABSOLUTE_DAYS: z.coerce.number().int().min(1).max(365).default(90),
  MAX_SESSIONS_PER_USER: z.coerce.number().int().min(1).max(100).default(10),
  REQUIRE_VERIFIED_EMAIL: z.enum(['true', 'false']).default('false').transform((value) => value === 'true'),
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
