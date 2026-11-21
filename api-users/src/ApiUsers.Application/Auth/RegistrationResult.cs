namespace ApiUsers.Application.Auth;

public enum RegistrationStatus { Created, Conflict, Invalid, Failed }

public sealed record RegistrationResult(
    RegistrationStatus Status,
    UserResponse? User = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
