namespace BlogLambda.Pagination;

public sealed record PageCursor(
    int Version,
    string UserId,
    int PageSize,
    PageDirection Direction,
    string AnchorSortKey);
