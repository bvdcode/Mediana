# Publishing executables

The build workflow publishes Windows x64, Linux x64 and macOS ARM64 executables
and archives to Cloudflare R2 after all platform builds and tests pass. Every
push to `main` determines the version with GitVersion and publishes automatically.
GitHub releases and NuGet packages are published by the same workflow.

The public origin is `https://releases.splidex.com`. Each project occupies its own
prefix. Mediana uses `mediana/<version>/` for executables, archives, their SHA-256
files and a versioned `release.json` manifest. Versioned objects have an immutable
one-year cache policy. `mediana/latest.json` has `Cache-Control: no-store`.

`.github/PublishRelease.ps1` verifies all executable and archive checksums before
uploading. It publishes the versioned files, verifies public executable sizes,
then publishes `latest.json` last. An upload or public verification failure before
that final step leaves the previous latest manifest in place. The updater constructs executable URLs
from the fixed public origin, manifest version and platform asset name.

The manifest format is:

```json
{
  "version": "1.2.3",
  "assets": [
    {
      "name": "Mediana-1.2.3-win-x64.exe",
      "size": 123456,
      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    }
  ]
}
```

Published manifests contain all three supported platform executables.

Configure the following repository settings for the R2 publication job:

| Setting | Type | Purpose |
| --- | --- | --- |
| `R2_ACCOUNT_ID` | Actions variable | Cloudflare account owning the bucket |
| `R2_BUCKET` | Actions variable | Bucket connected to `releases.splidex.com` |
| `R2_ACCESS_KEY_ID` | Actions secret | S3 access key scoped to the release bucket |
| `R2_SECRET_ACCESS_KEY` | Actions secret | S3 secret for object read and write access |

Connect the bucket to the custom domain in the same Cloudflare account and keep
`latest.json` outside cache rules that override its cache policy. Publication uses
the R2 S3 API with region `auto`; public downloads use the custom domain.
