using BlogLambda.Domain;
using BlogLambda.Pagination;

namespace BlogLambda.Repositories;

public sealed record PostSlice(
    IReadOnlyList<BlogPost> Posts,
    bool HasMoreInQueryDirection);

public interface IPostRepository
{
    Task<PostSlice> GetSliceAsync(
        string userId, int pageSize, PageDirection direction,
        string? exclusiveStartSortKey, CancellationToken cancellationToken);

    Task<bool> HasPostAsync(
        string userId, string anchorSortKey,
        PageDirection direction, CancellationToken cancellationToken);
}
