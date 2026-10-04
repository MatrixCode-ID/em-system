# Publish NuGet and containers from WPF

NuGet Manager and Container Manager include **Publish** and **Publish history**. Publishing is a local developer operation; the GUI session lists destinations, while a separately configured robot/API key performs pushes. Robot ownership does not inherit the owner's rights. Built-in NuGet requires W on the matching prefix; registry requires W on the root. Manager and settings claims remain separate.

1. Create a profile or **Create profile from .slnx**. Select workspace and sources, then review evaluated package IDs/frameworks. Nonpackable projects cannot be selected as packages; desktop projects are excluded as Linux hosts.
2. Set the build options, destination, and host-scoped credentials. Built-in destinations come from the active connection; changing connections requires selection/check again. Missing feeds/prefixes/roots/containers must be created in their manager.
3. **Check** validates tools, sources and target. **Prepare/Build** produces artifacts without pushing. NuGet also accepts existing `.nupkg` by picker/drop.
4. Select artifacts, enter release notes if required, review the confirmation and **Push**. **Prepare & Push / Build & Push** combines these operations. Cancel terminates only runner-owned process trees. Review partial results before retrying; successful items are skipped on retry.
5. **Verify** checks NuGet package identity/SHA-512 or the remote OCI manifest digest. Verification results are distinct from push results. Remote tag discovery uses OCI, not a vendor API.

## Profiles and local files

Defaults: Documents `Em/Publish/Profiles/{NuGet,Container}` and `Em/Publish/Logs`; LocalApplicationData `Em/Publish/Work` and `Em/Publish/Secrets`. Publisher settings can change Profiles, Logs and Work; preferences use the application's registry key. Profile files are written atomically; external edits require reload or explicit overwrite. Import/duplicate creates independent credential references. Delete profile retains history; history includes deleted profiles. Run logs are retained until manually deleted.

```json
{
  "formatVersion": 1,
  "id": "8b89b6538a83469d894951e13efad3793",
  "name": "Engine packages",
  "kind": "NuGet",
  "description": "",
  "workspace": ".",
  "sensitiveDataStorage": "Separate",
  "nuGet": {
    "sources": [{ "path": "src/backend/Em.Api.slnx", "projects": [] }],
    "configuration": "Release",
    "versionOverride": "1.0.0",
    "msbuildProperties": [],
    "target": { "type": "Custom", "serviceIndex": "https://packages.example/v3/index.json" },
    "duplicateHandling": "Skip"
  },
  "credentials": [],
  "keepWorkspace": false,
  "requireReleaseNotes": true
}
```

One file per profile; format version 1, stable GUID IDs; relative source paths resolve from workspace. NuGet supports multiple project/solution sources. Container modes are Dockerfile, LocalImage, Template, Compose and Set. Build arguments, named contexts, BuildKit secret references, configuration, runtime, framework, publish profile, environment, ports and entrypoint are editable in the form. Generated Dockerfile preview and file-set preview are available before building. Compose builds selected services only and never runs `up`.

Template can use a project `.pubxml`; `PublishDir` is overridden into the run workspace. Sets order Base before App and share identical publish inputs. Named file lists support IncludeOnly and Exclude complements, with Error (default) or Warning for missing entries. Existing Dockerfiles build against staged output, optionally under a named subfolder (`.file-base`, `.file-module`). App can use the Base digest from this run, last successful Base log, or explicit reference. Duplicate profiles for .NET/repository variants. Immutable Base references and release notes are recorded in history.

## Credentials and tools

Separate is the default. Remember stores DPAPI CurrentUser-encrypted credentials under LocalApplicationData; without Remember they last only for the application's session. Failed decryption means unavailable. Credentials are scoped to the effective host (including port) and are never forwarded to a different host. Plaintext stores secrets as ordinary JSON text: anyone who can read/copy that file can use them. JSON encryption is not implemented. Secret fields are masked; export excludes inline secrets by default and never inserts separately stored secrets.

Requires .NET SDK 8+ metadata evaluation (the project's own SDK/global.json still applies), Docker daemon for images, Compose v2 for Compose and Buildx for platform builds. NuGet.Protocol handles pushes in-process so API keys do not appear in process arguments. NuGet conflicts in the recycle bin remain failures and require manual restore/purge. Only `.nupkg` is supported.

Docker uses a temporary `DOCKER_CONFIG`, login via password stdin and BuildKit secret environment variables. Use my Docker login is an explicit profile option; OCI verification/tag discovery can read that host's existing Docker config or credential helper without modifying it or saving its secret to the profile. Version tags push before additional tags; `latest` is optional and may overwrite a previous release. Work cleanup validates ownership and containment; user source/output directories are never removed. Prepared package/shared publish output is retained with its run so later Push can use the snapshot. Logs contain masked output, stage exit codes, effective destinations, artifacts, separate verification, nonsecret settings and retry references. An unfinished run appears Interrupted.

External registries that send bearer credentials to a separate authentication host require a separately scoped integration; the publisher does not send registry credentials across host boundaries. Symbols, encrypted profile JSON, automatic log retention and remote/MAUI builds remain future ideas.
