namespace Avzar.Mobile;
public sealed record MobileBusinessChoice(Guid BusinessId, string BusinessName);
public sealed record MobileUser(string FullName, string Email, string BusinessName, string CurrencyCode);
public sealed record MobileTokens(string AccessToken, DateTime AccessExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc, MobileUser User);
public sealed record MobileTask(Guid Id, Guid OrderId, string Title, string StageName, string Status, string? DeadlineUtc, string? Instructions);
public sealed record QueuedTaskAction(Guid TaskId, string Action, string? Note, DateTime QueuedAtUtc);
