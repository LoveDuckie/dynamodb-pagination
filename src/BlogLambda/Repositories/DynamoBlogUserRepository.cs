using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using BlogLambda.Domain;

namespace BlogLambda.Repositories;

public sealed class DynamoBlogUserRepository(
    IAmazonDynamoDB db, IConfiguration config) : IBlogUserRepository
{
    private readonly string _table = config["DynamoDb:TableName"]
        ?? throw new InvalidOperationException("DynamoDb:TableName is missing.");

    public async Task<BlogUser?> GetAsync(string userId, CancellationToken ct)
    {
        var response = await db.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            ConsistentRead = true,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"USER#{userId}" },
                ["SK"] = new() { S = "PROFILE" }
            }
        }, ct);

        if (response.Item is null || response.Item.Count == 0)
            return null;

        var item = response.Item;
        return new BlogUser(
            userId,
            item["Handle"].S,
            item["DisplayName"].S,
            DateTimeOffset.Parse(item["CreatedAtUtc"].S,
                System.Globalization.CultureInfo.InvariantCulture));
    }
}
