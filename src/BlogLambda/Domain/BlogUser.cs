namespace BlogLambda.Domain;

public sealed record BlogUser(
    string Id,
    string Handle,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);
