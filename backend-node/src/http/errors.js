// Mirrors Tailoring.Application.Common.AppException + AppExceptionHandler: RFC 7807 body with a stable `code`.
export class AppError extends Error {
  constructor(status, code, message, extensions) {
    super(message);
    this.status = status;
    this.code = code;
    this.extensions = extensions;
  }
  static badRequest = (code, message) => new AppError(400, code, message);
  static unauthorized = (code, message) => new AppError(401, code, message);
  static forbidden = (code, message) => new AppError(403, code, message);
  static notFound = (code, message) => new AppError(404, code, message);
  static conflict = (code, message, extensions) => new AppError(409, code, message, extensions);
}

export function problemBody(err) {
  return { type: `https://httpstatuses.io/${err.status}`, title: err.message, status: err.status, code: err.code, ...(err.extensions ?? {}) };
}

// Express 5 error middleware: expected AppErrors become problem+json; anything else is a generic 500 (details logged, never leaked).
export function errorHandler(log = console) {
  return (err, req, res, _next) => {
    if (res.headersSent) return;
    if (err instanceof AppError) return res.status(err.status).type('application/problem+json').send(problemBody(err));
    if (err?.type === 'entity.parse.failed' || err?.status === 400) return res.status(400).type('application/problem+json').send(problemBody(AppError.badRequest('invalid_json', 'Malformed request body.')));
    log.error?.({ err: String(err?.message ?? err), path: req.path }, 'unhandled error');
    res.status(500).type('application/problem+json').send(problemBody(new AppError(500, 'internal_error', 'An unexpected error occurred.')));
  };
}
