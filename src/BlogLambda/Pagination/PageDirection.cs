namespace BlogLambda.Pagination;

public enum PageDirection
{
    Forward = 1,  // Older posts, descending sort key
    Backward = 2  // Newer posts, ascending query, then reverse for display
}
