# Blogging with ASP.NET Core MVC + DynamoDB + time-limited protected cursors

**Updated 19 October 2026.** Uses .NET 10, `Amazon.AspNetCore.DataProtection.SSM` 4.0.3, `Amazon.Lambda.AspNetCoreServer.Hosting` 2.2.1, and `Microsoft.AspNetCore.DataProtection.Extensions` 10.0.13. Package versions were verified against NuGet.

## Exactly where the AWS KMS key is configured

In `src/BlogLambda/Program.cs`, inside the production (non-Development) branch:

```csharp
string parameterPath = builder.Configuration["DataProtection:ParameterPath"]
    ?? throw new InvalidOperationException("Missing SSM parameter path");
string kmsKeyArn = builder.Configuration["DataProtection:KmsKeyArn"]
    ?? throw new InvalidOperationException("Missing KMS key ARN");

builder.Services.AddDataProtection()
    .SetApplicationName("BlogPagination.Sample")
    .PersistKeysToAWSSystemsManager(
        parameterPath,
        options => { options.KMSKeyId = kmsKeyArn; });
```

The full code validates both settings and registers the DynamoDB repositories, MVC controllers and cursor codec.

The `template.yaml` file creates a KMS symmetric encryption key and passes its ARN to Lambda:

```yaml
DataProtection__ParameterPath: !Sub /${AWS::StackName}/dataprotection/keys
DataProtection__KmsKeyArn: !GetAtt DataProtectionKmsKey.Arn
```

The configuration binder maps `__` to `:`. An ARN identifies the KMS key but isn't secret. The Lambda execution role needs `ssm:PutParameter`, `ssm:GetParametersByPath`, `kms:Encrypt` and `kms:Decrypt`, and the KMS key policy must permit the role's use of the key.

**What happens:** ASP.NET Core generates its own Data Protection master keys. `PersistKeysToAWSSystemsManager` stores key-ring XML as SSM `SecureString` parameters under the specified prefix. The AWS provider sets `PutParameterRequest.KeyId` to `options.KMSKeyId` for **new** entries. SSM uses KMS to encrypt them and to decrypt them on authorised retrieval. Once loaded, `ITimeLimitedDataProtector.Protect()` and `.Unprotect()` use application-local cryptography. No public/private keys are involved in this symmetric encryption setup.

**Important rotation detail:** changing `KMSKeyId` affects newly written key-ring entries; it does **not** automatically re-encrypt older SSM parameter values. Do not disable/delete the former KMS key while those entries are still needed, unless you've carefully migrated them. Changing the Data Protection key ring or application name will invalidate existing protected cursors.

The SAM template's `DataProtectionKeyArn` and `DataProtectionParameterPath` outputs show the deployed values. To inspect SSM metadata without decrypting key values:

```bash
aws ssm describe-parameters \
  --parameter-filters 'Key=Name,Option=BeginsWith,Values=/YOUR-STACK/dataprotection/keys/' \
  --query 'Parameters[].{Name:Name,Type:Type,KeyId:KeyId}'
```

The provider writes entries on demand when a Data Protection key is created. Immediately after deployment, SSM may still be empty until the first relevant protection operation.

This is an updated sample of **bidirectional keyset pagination** for AWS Lambda.
The page at `/users/alice/posts` has **no query parameters on first load**.
The Razor view displays the author's posts and renders clickable Previous/Next
links containing encrypted, Base64Url-safe `cursor` query-string values.
The first page has **no Previous link**.

## Storage model

Single DynamoDB table `BlogItems`, keys `PK` (string partition key), `SK` (string sort key):

| Type | PK | SK | Fields |
|---|---|---|---|
| User | `USER#alice` | `PROFILE` | `Handle`, `DisplayName`, `CreatedAtUtc` |
| Post | `USER#alice` | `POST#20261018T1100000000000Z#post-001` | `PostId`, `Title`, `Excerpt`, `CreatedAtUtc`, `LastUpdatedAtUtc` |

`CreatedAtUtc` is immutable and encoded in `SK` using a fixed-width UTC
format plus unique post ID. `LastUpdatedAtUtc` can change without moving a post.
An author partition is a simple access pattern; for hot/popular authors, use sharding
or a different index strategy. This example stores only posts intended for public
visibility under `POST#`; drafts/private records would need a distinct access
pattern, not a post-query `FilterExpression`.

## Navigation

- First: `GET /users/alice/posts`, query `ScanIndexForward=false` with no start key.
- Next (older): `ScanIndexForward=false`, `ExclusiveStartKey` = **last displayed** post.
- Previous (newer): `ScanIndexForward=true`, `ExclusiveStartKey` = **first displayed** post; reverse the returned slice for newest-first display.
- Query `Limit=PageSize+1` to detect whether more records exist in the requested direction.
- Loop across DynamoDB 1 MB query response boundaries if needed.
- Query the opposite direction with `Limit=1` on subsequent pages to determine the other link.
- Use the first/last **displayed** item as the outgoing anchor. Do not blindly use `LastEvaluatedKey` after fetching a look-ahead item, or it can skip that item.
- DynamoDB's `LastEvaluatedKey` internally resumes multi-response reads within *one* application page.

The view receives `NextCursor`/`PreviousCursor` produced by `ITimeLimitedDataProtector`.
Data Protection authenticates and encrypts the payload, rather than merely
Base64-encoding readable JSON. Payload: `Version`, `UserId`, `PageSize`,
`Direction`, `AnchorSortKey`. The 20-minute expiry is enforced by the protector.
The cursor is bound to the requested author and a fixed page size of 5.

## Run locally

Prerequisites: .NET 10 SDK, Docker, Python 3.10+ and `boto3`.

```bash
# At the project root

docker compose up -d
python -m pip install boto3
python scripts/seed_local.py

# Bash/macOS
DOTNET_ENVIRONMENT=Development dotnet run --project src/BlogLambda --urls http://localhost:5000
# PowerShell
# $env:DOTNET_ENVIRONMENT='Development'
# dotnet run --project src/BlogLambda --urls http://localhost:5000
```

Then open `http://localhost:5000/users/alice/posts` and click Next/Previous.
Local development uses a filesystem key ring and DynamoDB Local without any AWS credentials.
Stop/restarting the app preserves locally issued cursors while `.local-keys` persists.

Optional smoke test:

```bash
python -m pip install requests beautifulsoup4
python scripts/smoke_test.py
```

## Deploy to AWS Lambda

The provided AWS SAM template creates DynamoDB, a customer-managed KMS key,
Lambda, API Gateway HTTP API, and read-only DynamoDB/IAM plus SSM/KMS key-ring
permissions. The production Lambda uses the default AWS SDK credential chain
from its execution role; it is not placed inside a VPC, so no NAT or VPC endpoints
are required by this example.

```bash
sam build
sam deploy --guided
```

The stack does **not** seed production DynamoDB. Add separate provisioning/import
or authenticated write endpoints before expecting user records to exist. The
`BlogExampleUrl` output is a route example, not a guaranteed populated page.

AWS SSM persists the **Data Protection key ring** as KMS-encrypted SecureString
parameters. KMS encrypts/decrypts *stored keys*, not every pagination cursor.
Once a Lambda execution environment has obtained the key ring, its normal
Protect/Unprotect operations are in process. Do not configure a Lambda-local
filesystem key ring in production.

## Important trade-offs

- This is a **public read-only** blog example. Add authentication/authorization for
  authoring, editing, drafts, and private post access. Cursors are not access tokens.
- Strongly consistent reads are used on the DynamoDB base table, but multiple
  Query requests are not a transactional snapshot. Inserts and deletes can change
  page boundaries between requests; sorting by mutable LastUpdatedAtUtc would
  make that worse. For an updates feed, use a dedicated GSI and accept the
  resulting movement, or design an immutable activity-event feed.
- DynamoDB Query consumes up to 1 MB per service response; multi-query looping
  supports large records but full guarantees under concurrent writes require
  additional design.
- Keep writes to `CreatedAtUtc`/`SK` immutable and use conditional updates for
  `LastUpdatedAtUtc` when implementing editing endpoints.
- Production reads cost RCUs; the extra look-ahead item and reverse-side
  existence check are intentional to avoid emitting dead links.
- The example currently has **no integration compile test** in this environment:
  .NET and AWS SAM CLIs were not available where it was authored. Run `dotnet
  build` and the smoke test before deployment.
