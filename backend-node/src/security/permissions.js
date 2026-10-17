import { AppError } from '../http/errors.js';

export function requirePermission(permission) {
  return (req, _res, next) => {
    const permissions = Array.isArray(req.currentUser?.permissions) ? req.currentUser.permissions : [];
    if (!permissions.includes(permission)) {
      return next(AppError.forbidden('forbidden', `Missing required permission: ${permission}`));
    }
    return next();
  };
}
