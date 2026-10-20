# AWS KMS / SSM / Data Protection configuration map

| File | Field | Role |
|---|---|---|
| `template.yaml` | `DataProtectionKmsKey` | Creates the symmetric customer-managed AWS KMS key |
| `template.yaml` | `DataProtection__KmsKeyArn` | Passes `!GetAtt DataProtectionKmsKey.Arn` into Lambda environment |
| `template.yaml` | `DataProtection__ParameterPath` | SSM path prefix for shared Data Protection key ring |
| `src/BlogLambda/Program.cs` | `options.KMSKeyId = kmsKeyArn` | Sets the KMS key on newly created SSM SecureString parameters |
| `src/BlogLambda/Pagination/PaginationCursorCodec.cs` | `provider.CreateProtector(...)` | Creates time-limited encrypted and authenticated cursors locally |

## Security boundaries

- Do not confuse the KMS key with the key-ring entries stored in SSM. KMS keeps its own root key material. Lambda loads Data Protection keys decrypted through SSM/KMS.
- `KMSKeyId` is the encryption key selector for the **key ring at rest**; it is not used for per-cursor KMS requests.
- An SSM key-ring parameter is stored as `SecureString`. Keep `ssm:GetParametersByPath` and KMS decrypt permissions limited to the function's role.
- Maintain a persistent, shared key-ring path and identical `SetApplicationName()` on every Lambda deployment handling compatible cursors.
- Existing SSM parameters do not automatically switch encryption key if the configuration changes; plan migration before retiring a KMS key.
- For real private posts, perform authorization separately. Data Protection cursors do not grant access.
- This project has public-read browsing only, does not implement write endpoints, and uses a local-only key directory in Development.
