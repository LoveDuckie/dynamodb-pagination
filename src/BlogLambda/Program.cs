using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.Runtime;
using BlogLambda.Pagination;
using BlogLambda.Repositories;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
{
    var localUrl = builder.Configuration["DynamoDb:ServiceUrl"];
    if (!string.IsNullOrWhiteSpace(localUrl))
    {
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("DynamoDB local endpoint must only be used in Development.");
        return new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = localUrl });
    }
    // Lambda credentials and AWS_REGION are resolved by the AWS SDK.
    return new AmazonDynamoDBClient();
});

builder.Services.AddScoped<IBlogUserRepository, DynamoBlogUserRepository>();
builder.Services.AddScoped<IPostRepository, DynamoPostRepository>();
builder.Services.AddSingleton<PaginationCursorCodec>();

if (builder.Environment.IsDevelopment())
{
    var dir = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".local-keys"));
    dir.Create();
    builder.Services.AddDataProtection()
        .SetApplicationName("BlogPagination.Sample")
        .PersistKeysToFileSystem(dir);
}
else
{
    // Provided as Lambda environment variables by the AWS SAM template:
    //   DataProtection__ParameterPath -> DataProtection:ParameterPath
    //   DataProtection__KmsKeyArn    -> DataProtection:KmsKeyArn
    string parameterPath = builder.Configuration["DataProtection:ParameterPath"]
        ?? throw new InvalidOperationException("Missing DataProtection:ParameterPath");
    string kmsKeyArn = builder.Configuration["DataProtection:KmsKeyArn"]
        ?? throw new InvalidOperationException("Missing DataProtection:KmsKeyArn");

    if (string.IsNullOrWhiteSpace(parameterPath) ||
        string.IsNullOrWhiteSpace(kmsKeyArn))
    {
        throw new InvalidOperationException(
            "Data Protection parameter path and KMS key ARN must not be empty.");
    }

    builder.Services.AddDataProtection()
        .SetApplicationName("BlogPagination.Sample")
        .PersistKeysToAWSSystemsManager(
            parameterPath,
            options =>
            {
                // EXACT KMS selection: this is passed as the KeyId in the
                // SSM PutParameter(SecureString) request for new key-ring entries.
                // It does NOT call KMS for every Protect/Unprotect operation.
                options.KMSKeyId = kmsKeyArn;
            });
}

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.Run();
