using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace BlogLambda.Pagination;

public sealed class PaginationCursorCodec
{
    private const int Version = 1;
    private const int MaxLength = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITimeLimitedDataProtector _protector;

    public PaginationCursorCodec(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("blog.posts.by-author.created.v1")
            .ToTimeLimitedDataProtector();
    }

    public string Encode(PageCursor cursor) => _protector.Protect(
        JsonSerializer.Serialize(cursor, JsonOptions), Lifetime);

    public bool TryDecode(string? token, out PageCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxLength)
            return false;

        try
        {
            var decoded = JsonSerializer.Deserialize<PageCursor>(
                _protector.Unprotect(token), JsonOptions);
            if (decoded is null || decoded.Version != Version ||
                decoded.PageSize != 5 ||
                !Enum.IsDefined(decoded.Direction) ||
                string.IsNullOrWhiteSpace(decoded.UserId) ||
                string.IsNullOrWhiteSpace(decoded.AnchorSortKey) ||
                decoded.AnchorSortKey.Length > 200 ||
                !decoded.AnchorSortKey.StartsWith("POST#", StringComparison.Ordinal))
                return false;

            cursor = decoded;
            return true;
        }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
    }
}
