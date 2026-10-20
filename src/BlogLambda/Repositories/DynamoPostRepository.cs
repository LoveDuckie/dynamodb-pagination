using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using BlogLambda.Domain;
using BlogLambda.Pagination;
using System.Globalization;

namespace BlogLambda.Repositories;

public sealed class DynamoPostRepository(
    IAmazonDynamoDB db, IConfiguration config) : IPostRepository
{
    private readonly string _table = config["DynamoDb:TableName"]
        ?? throw new InvalidOperationException("DynamoDb:TableName is missing.");

    public async Task<PostSlice> GetSliceAsync(
        string userId, int pageSize, PageDirection direction,
        string? exclusiveStartSortKey, CancellationToken ct)
    {
        // Query only POST# items, so no FilterExpression is needed.
        // Pull the +1 lookahead item to discover whether another page exists.
        // DynamoDB may stop at 1MB, so loop across service pages if necessary.
        var collected = new List<BlogPost>(pageSize + 1);
        string? start = exclusiveStartSortKey;

        while (collected.Count < pageSize + 1)
        {
            var response = await db.QueryAsync(CreateQuery(
                userId, direction, start, pageSize + 1 - collected.Count), ct);

            foreach (var item in response.Items ?? [])
                collected.Add(Deserialize(userId, item));

            if (response.LastEvaluatedKey is not { Count: > 0 } key)
                break;

            start = key["SK"].S;
        }

        bool hasMore = collected.Count > pageSize;
        List<BlogPost> displayed = collected.Take(pageSize).ToList();

        // DynamoDB has queried ascending for Backward; UI is always newest-first.
        if (direction == PageDirection.Backward)
            displayed.Reverse();

        return new PostSlice(displayed, hasMore);
    }

    public async Task<bool> HasPostAsync(
        string userId, string anchorSortKey,
        PageDirection direction, CancellationToken ct)
    {
        var response = await db.QueryAsync(
            CreateQuery(userId, direction, anchorSortKey, 1), ct);
        return response.Items is { Count: > 0 };
    }

    private QueryRequest CreateQuery(
        string userId, PageDirection direction,
        string? anchor, int limit)
    {
        var request = new QueryRequest
        {
            TableName = _table,
            ConsistentRead = true,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :prefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = $"USER#{userId}" },
                [":prefix"] = new() { S = "POST#" }
            },
            ScanIndexForward = direction == PageDirection.Backward,
            Limit = limit
        };

        if (anchor is not null)
        {
            request.ExclusiveStartKey = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"USER#{userId}" },
                ["SK"] = new() { S = anchor }
            };
        }
        return request;
    }

    private static BlogPost Deserialize(
        string userId, Dictionary<string, AttributeValue> item)
    {
        var sk = item["SK"].S;
        return new BlogPost(
            item["PostId"].S,
            userId,
            sk,
            item["Title"].S,
            item["Excerpt"].S,
            DateTimeOffset.Parse(item["CreatedAtUtc"].S, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(item["LastUpdatedAtUtc"].S, CultureInfo.InvariantCulture));
    }
}
