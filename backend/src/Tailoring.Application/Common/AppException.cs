namespace Tailoring.Application.Common;

/// <summary>Expected, user-facing failure. Mapped to an RFC 7807 problem response.</summary>
public sealed class AppException(int statusCode, string code, string message, IDictionary<string, object?>? extensions = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IDictionary<string, object?>? Extensions { get; } = extensions;

    public static AppException BadRequest(string code, string message) => new(400, code, message);
    public static AppException Unauthorized(string code, string message) => new(401, code, message);
    public static AppException Forbidden(string code, string message) => new(403, code, message);
    public static AppException NotFound(string code, string message) => new(404, code, message);
    public static AppException Conflict(string code, string message, IDictionary<string, object?>? extensions = null)
        => new(409, code, message, extensions);
}
