using BlogLambda.Domain;
using BlogLambda.Pagination;
using BlogLambda.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace BlogLambda.Controllers;

public sealed record BlogPostsPageViewModel(
    BlogUser Author,
    IReadOnlyList<BlogPost> Posts,
    string? NextCursor,
    string? PreviousCursor);

public sealed class PostsController(
    IBlogUserRepository users,
    IPostRepository posts,
    PaginationCursorCodec cursors) : Controller
{
    private const int PageSize = 5;
    private const int CursorVersion = 1;

    [HttpGet("/users/{userId}/posts")]
    public async Task<IActionResult> Index(
        [FromRoute] string userId,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        // Use a strict, canonical author ID as part of the PK and signed cursor scope.
        if (userId.Length > 64 || !Regex.IsMatch(userId, @"\A[a-zA-Z0-9_-]+\z"))
            return BadRequest("Invalid user ID.");

        var author = await users.GetAsync(userId, cancellationToken);
        if (author is null) return NotFound();

        PageCursor? state = null;
        if (cursor is not null &&
            (!cursors.TryDecode(cursor, out state) ||
             state is null || state.UserId != userId ||
             state.PageSize != PageSize))
            return BadRequest("Invalid or expired pagination cursor.");

        PageDirection direction = state?.Direction ?? PageDirection.Forward;
        PostSlice slice = await posts.GetSliceAsync(
            userId, PageSize, direction, state?.AnchorSortKey, cancellationToken);

        if (slice.Posts.Count == 0)
            return View(new BlogPostsPageViewModel(author, slice.Posts, null, null));

        BlogPost first = slice.Posts[0];
        BlogPost last = slice.Posts[^1];
        bool hasNewer;
        bool hasOlder;

        if (state is null)
        {
            hasNewer = false; // First page: never a Previous link.
            hasOlder = slice.HasMoreInQueryDirection;
        }
        else if (direction == PageDirection.Forward)
        {
            hasOlder = slice.HasMoreInQueryDirection;
            hasNewer = await posts.HasPostAsync(
                userId, first.SortKey, PageDirection.Backward, cancellationToken);
        }
        else
        {
            hasNewer = slice.HasMoreInQueryDirection;
            hasOlder = await posts.HasPostAsync(
                userId, last.SortKey, PageDirection.Forward, cancellationToken);
        }

        string Encode(BlogPost anchor, PageDirection travel) =>
            cursors.Encode(new PageCursor(
                CursorVersion, userId, PageSize, travel, anchor.SortKey));

        return View(new BlogPostsPageViewModel(
            author,
            slice.Posts,
            NextCursor: hasOlder ? Encode(last, PageDirection.Forward) : null,
            PreviousCursor: hasNewer ? Encode(first, PageDirection.Backward) : null));
    }
}
