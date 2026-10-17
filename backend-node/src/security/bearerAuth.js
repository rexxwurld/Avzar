import { AppError } from '../http/errors.js';
import { verifyAccessToken } from './jwt.js';
import { currentUser } from '../auth/mobileAuthService.js';

export function requireBearerAuth(env, pool) {
  const auth = {
    jwtSigningKey: env.JWT_SIGNING_KEY,
    jwtIssuer: env.JWT_ISSUER,
    jwtAudience: env.JWT_AUDIENCE,
    accessTokenMinutes: env.ACCESS_TOKEN_MINUTES,
  };
  return async (req, _res, next) => {
    const header = req.get('authorization') ?? '';
    const token = /^Bearer\s+(.+)$/i.exec(header)?.[1];
    const identity = token && verifyAccessToken(auth, token);
    if (!identity || !pool) return next(AppError.unauthorized('unauthorized', 'Authentication is required.'));
    try {
      const { rows } = await pool.query(`
        SELECT 1 FROM "UserSessions" WHERE "Id" = $1 AND "UserId" = $2 AND "RevokedAtUtc" IS NULL AND "AbsoluteExpiresAtUtc" > now()`,
      [identity.sessionId, identity.userId]);
      if (!rows.length) return next(AppError.unauthorized('unauthorized', 'Authentication is required.'));
      const user = await currentUser(pool, identity.userId, identity.businessId);
      if (!user) return next(AppError.unauthorized('membership_inactive', 'Your membership in this business is no longer active.'));
      req.auth = identity;
      req.currentUser = user;
      next();
    } catch (error) {
      next(error);
    }
  };
}