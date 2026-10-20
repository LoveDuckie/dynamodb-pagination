namespace BlogLambda.Domain;

// CreatedAtUtc is immutable and encoded in SortKey.
// LastUpdatedAtUtc is mutable and NEVER changes this post's pagination position.
public sealed record BlogPost(
    string Id,
    string UserId,
    string SortKey,
    string Title,
    string Excerpt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUpdatedAtUtc);
