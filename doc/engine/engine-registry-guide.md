# Container registry: user guide

[Bahasa Indonesia](engine-registry-guide.id.md)

A step-by-step guide to the built-in container registry from the WPF client: the **Container Manager**
(Containers, Settings and Publish tabs) and **User Manager → Robots**. Every option on those screens is
explained next to an annotated screenshot.

For server setup, the HTTP actions and Docker testing, see [Container registry](engine-registry.md). Related
references: [Publish](engine-publish.md), [Storage settings](engine-storage-settings.md),
[Robot identities](engine-robots.md) and [container image naming](../convention/container-naming.md).

The screenshots use sample data only: the server `registry.example.com`, the root `acme`, the container `api`
and the robot `acme-publisher`.

## Contents

- [Before you start](#before-you-start)
- [Quick path: your first image](#quick-path-your-first-image)
- [1. Containers tab](#1-containers-tab)
- [2. Robots and access](#2-robots-and-access)
- [3. Settings tab](#3-settings-tab)
- [4. Publish tab](#4-publish-tab)
- [Troubleshooting](#troubleshooting)

## Before you start

| Need | Why |
| --- | --- |
| The registry is enabled on the API server | See [Container registry → Enabling](engine-registry.md#enabling). Otherwise the Containers tab shows "Container registry is not enabled on this server." |
| Claim `Container Manager Access` | Opens the Container Manager and lists roots and containers for publishing |
| Claim `Container Registry Settings Manage` | Edits the Settings tab (status alone needs only the manager claim) |
| Claim `User Manager Access` | Creates robots and grants their access |
| HTTPS on the server | Docker accepts plain HTTP only for `localhost`. The screens warn when the active connection is plain HTTP to another host |
| Docker on the publishing machine | Build and push run the local Docker CLI. Buildx is needed only when a platform is set; the .NET SDK only for the Template and Set modes |

All claims belong to module `Administrative Tools`; administrators have them all.

## Quick path: your first image

1. **Containers** tab → **New Root** (for example `acme`).
2. Select the root → **New Container** (for example `api`). A push never creates a name; the container has to
   exist first.
3. **User Manager → Robots** → **New robot**, then copy the token from the dialog. It is shown only once.
4. In the robot's **Manager access**, set `Container / acme` to **Write**.
5. **Container Manager → Publish** → **New Profile**: choose a mode, select the destination `acme/api`, a version
   tag and the robot credential.
6. **Check**, then **Build & Push**, then **Verify**.
7. Back on **Containers**, the container `acme/api` lists the new tags.

The pull name is always `host/root/container`, exactly two segments after the host:
`registry.example.com/acme/api:1.0.0-alpha.1`. Tags follow
[container image naming](../convention/container-naming.md).

## 1. Containers tab

![Containers tab](images/registry-guide/containers-overview.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Containers** tab | Roots, folders and containers of the registry |
| 2 | **Settings** tab | Turns the registry on or off and moves its storage; see [3. Settings tab](#3-settings-tab) |
| 3 | **Publish** tab | Builds and pushes images; see [4. Publish tab](#4-publish-tab) |
| 4 | **New Root** | Creates a root: the owner, the unit of access and the first part of every pull name |
| 5 | **New Folder** | Creates a folder in the selected root or folder (up to 8 levels). Folders only group containers on this screen and never appear in the pull name |
| 6 | **New Container** | Creates a container in the selected root or folder. It has to exist before anything can be pushed to it |
| 7 | **Edit** | With a root selected: description and Active switch. With a folder: rename (F2). With a container: description and Active switch |
| 8 | **Move to...** | Moves the selected folder or container to another folder of the same root. Drag and drop in the tree does the same. The pull name does not change |
| 9 | **Delete** | Deletes the selected item (Delete key). A root or folder has to be empty; a container is deleted with its manifests and tags. Only metadata is removed; layer files stay on disk |
| 10 | Storage chip and its refresh | Total size of stored layers and manifests, read from registry metadata. The small button reloads only this figure |
| 11 | **Refresh** | Reloads roots and containers (F5) |
| 12 | Roots list | Every root with its container count. A **Disabled** root refuses every push and pull |
| 13 | Tree | Folders and containers of the selected root, with the item and tag counts. Right-click opens the same actions as the toolbar |
| 14 | Details | The selected root, folder or container |

Names use lowercase letters and digits separated by `.`, `_` or `-` (root up to 64 characters, container up to
128). Root and container names cannot be changed after creation; only folders can be renamed.

### New root and new container

![New Root dialog](images/registry-guide/new-root.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Root name** | The first segment of the pull name, for example `acme` in `host/acme/api` |
| 2 | **Description** | Optional note for other operators |
| 3 | **Create** | Creates the root, active. Use **Edit** later to switch it off |

![New Container dialog](images/registry-guide/new-container.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Container name** | The second segment of the pull name |
| 2 | **Pull name** preview | The `root/container` this name produces |
| 3 | **Description** | Optional |
| 4 | **Create** | Creates the container in the root (and folder) selected before opening the dialog |

### Container details

![Container details](images/registry-guide/container-detail.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Pull name** and copy | `host/root/container` for the active connection. The copy button copies it |
| 2 | Tag chips | One chip per tag of a manifest. Click copies `docker pull` for that tag; right-click offers **Copy docker pull** and **Copy docker tag + push** |
| 3 | Copy digest | Copies the manifest digest (`sha256:...`) |
| 4 | Copy docker pull by digest | Copies `docker pull host/root/container@sha256:...`, which always fetches exactly this build |
| 5 | Media type and details | Manifest type, manifest size, push time and the robot that pushed it |

The details also show **Tags**, **Manifests**, **Last pushed** and **Created**. For a root, the details show its
folder and container counts and its **Pull prefix**.

## 2. Robots and access

A robot is the account `docker login` uses: its name is the username and its token is the password. Robots are
managed in **User Manager → Robots** because other managers (for example the NuGet server) use them too.

![Robots in User Manager](images/registry-guide/robots.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **New robot** (+) | Opens the New Robot dialog |
| 2 | Robot list | Every robot with its expiry chip: grey when valid, amber when it expires within 14 days, **Expired** after the date |
| 3 | **Edit** | Changes the description, the Active switch and the token expiry. A disabled robot cannot log in |
| 4 | **Regenerate token** | Issues a new token and shows it once; the old token stops working at once |
| 5 | **Delete** | Deletes the robot and all its access (Delete key) |
| 6 | Owner, token, expiry, last use | The owner account (metadata only, it grants nothing), the first characters of the token, the expiry date and the last successful login |
| 7 | Access row | One row per manager resource. For the registry each root is a row named `Container / <root>` |
| 8 | Access choice | **No access**, **Read** (pull) or **Write** (push and pull). The choice is saved as soon as it changes |

A robot without access to a root cannot even see it (`NAME_UNKNOWN`). One robot can hold Write on one root and
Read on another, so `FROM host/base/runtime` in a Dockerfile and a push to `host/acme/api` work with one login.

### New robot

![New Robot dialog](images/registry-guide/new-robot.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Robot name** | The `docker login` username. Up to 100 lowercase letters, digits, `.`, `_` or `-`, starting with a letter or digit. It cannot be changed later |
| 2 | **Description** | Optional, for example where the robot is used |
| 3 | **Owner account** | Optional link to an active user account, with a search box. It is information only: the robot never inherits the owner's rights |
| 4 | **Token expiry** | **No expiry**, **30**, **90** or **365 days from now**, or **Choose a date...** (the last day the token may be used). After that day `docker login` is refused |
| 5 | **Create** | Creates the robot without any access and shows its token |

### The token

![Robot token dialog](images/registry-guide/robot-token.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Token** | The password for `docker login`. Only its hash is stored on the server |
| 2 | **Container login command** | The ready `docker login` line for this server and robot |
| 3 | **Copy token** | Copies the token, for example into a publish profile credential |
| 4 | **Copy docker login** | Copies the login command |
| 5 | **I have saved it** | Closes the dialog. The token cannot be shown again; if it is lost, use **Regenerate token** |

## 3. Settings tab

![Registry settings](images/registry-guide/settings.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | Status line | `active` or `disabled`, whether saved changes are waiting for an API restart, and `static host configuration` when the host fixes the path in code (Save is then unavailable) |
| 2 | Refresh | Reloads status and settings. It asks first when there is an unsaved draft |
| 3 | **Enabled on next API startup** | Turns the registry on or off from the next start |
| 4 | **Directory on API server** | Where layers and uploads are stored, on the server's file system, relative to its content root. Not a folder on your computer |
| 5 | Active, saved, revision | The directory in use now, the saved directory for the next start, and the revision used to detect concurrent saves |
| 6 | **Validate directory** | Checks the draft directory on the server with a test file. For a new registry root it also checks every stored layer |
| 7 | **Save** | Stores the draft. It takes effect only after an operator restarts the API |

Saving never moves, copies or deletes files. To move the registry, copy the whole directory yourself, make sure
no upload is in progress, validate, save and restart. The server checks every layer again at startup. Details:
[Storage settings](engine-storage-settings.md).

## 4. Publish tab

Publishing runs on your computer with the local Docker CLI. The signed-in session only lists destinations; the
push itself logs in with a robot token kept in the profile's credentials. Profiles, logs and work folders are
local files.

### The page and profiles

![Publish page](images/registry-guide/publish-page.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Publish / History** | Switches between the work page and the run history |
| 2 | **New Profile** | Opens an empty profile form, with the workspace set to your Documents folder |
| 3 | Profile tools | **Import** a profile `.json` file; **Edit** the selected profile; **Duplicate** it (new ID and credential references); **Export** it (sensitive data is left out unless you choose otherwise; separately stored secrets are never exported); **Delete** the profile file (its history stays) |
| 4 | More (⋮) | **Create profile from .slnx** (reads the solution, picks the host project and starts a Template profile), **Refresh profiles**, **Open profiles folder**, **Publisher settings** |
| 5 | Profiles | The container profiles on this computer. Selecting one loads it into the work area |

### The work area

![Publish work area](images/registry-guide/publish-operations.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Source** card | Profile name and workspace. Its refresh lists the projects behind a Template profile |
| 2 | **Target** card | The full target `host/root/container:tag`. Refresh resolves it against the server and shows the last version pushed from this computer and the latest tag in the registry. The other buttons copy the target and open the Containers tab |
| 3 | **Tools** card | Versions of the tools the profile needs (Docker, and the .NET SDK for Template and Set) |
| 4 | **Check** | Validates the profile, the workspace and the tools, and that the root and container exist and are active. It also checks that a credential exists for the registry host. It does not log in |
| 5 | **Build** | Builds the image locally, without pushing. The artifact appears in the table |
| 6 | **Push** | After a confirmation, logs in with the robot, pushes the version tag, then every extra tag. It needs a Build from the same session; if the source or build settings changed since, build again |
| 7 | **Verify** | Reads the manifest of the version tag from the registry and compares its digest with the pushed one |
| 8 | **Build & Push** | Check, Build and Push in one run with one confirmation |
| 9 | **Cancel** | Stops the running operation. Only processes started by the publisher are stopped. Review partial results before retrying |
| 10 | **Release notes** | Stored with the run. Required when the profile has **Require Release Notes** |
| 11 | Message line | The state of the last operation, or its error. "Operation completed" only means it ended without an error; read the **Result** column |
| 12 | Artifacts | One row per image: the checkbox selects it for Push and Verify; **Version**, **Target**, **Result** (`NotRun`, `Running`, `Success`, `Failed`, `Cancelled`) and **Message** (errors and the verification result) |
| 13 | Live log | Output of the Docker commands. A line `<tag>: digest: sha256:... size: ...` means that tag was pushed. `Layer already exists` on extra tags is normal, because the version tag already uploaded the layers |

Build, Push and Verify work on the artifacts built in the current session of the application. After restarting
it, run **Build** (or **Build & Push**) again. Items that already succeeded are skipped when a push is retried.

### Profile form

Open it with **New Profile** or **Edit**. The form has five tabs; **Save** keeps the profile, **Save As** saves a
copy with a new ID, and **Cancel** discards the changes. Errors appear above the buttons.

#### Source

![Profile: Source](images/registry-guide/profile-source.png)

| # | Field | What it means |
| --- | --- | --- |
| 1 | **Name** | Shown in the profile list and the history |
| 2 | **Description** | Optional |
| 3 | **Workspace** and **Browse folder** | The base folder. Every relative path in the profile (context, Dockerfile, project) starts here |
| 4 | **Project**, **Publish Source**, **Publish Profile** | Used by the Template mode only: the host project to `dotnet publish`, and whether its settings come from the form fields (**Fields**) or from a `.pubxml` publish profile (**PublishProfile**) |
| 5 | **Re-read project information** | Reads the project (or solution) and lets you choose the container host project |

#### Build

![Profile: Build](images/registry-guide/profile-build.png)

| # | Field | What it means |
| --- | --- | --- |
| 1 | **Mode** | How the image is produced (see the table below) |
| 2 | **Context** | The Docker build context, relative to the workspace (`.` = the workspace) |
| 3 | **File** | The Dockerfile, relative to the workspace |
| 4 | **Target** | The Dockerfile stage to build (`--target`). Empty builds the last stage |
| 5 | **Platform** | For example `linux/amd64` (`--platform`). Leave it empty to build for the local Docker engine; when set, Buildx is required |
| 6 | **Build Args**, **Named Contexts**, **Secrets** | `--build-arg KEY=value`, `--build-context name=path`, and BuildKit secrets whose value comes from a profile credential (never written to the Dockerfile or the log) |
| 7 | **Local Image** and **Select local image** | For the LocalImage mode: an image that already exists in the local Docker, chosen from a list |
| 8 | **.NET template**, **Compose**, **Ordered Set** | Settings of the other modes |

| Mode | Use it when |
| --- | --- |
| **Dockerfile** | The repository has a Dockerfile. Fields 2 to 6 apply |
| **LocalImage** | The image is already built locally; the publisher only tags and pushes it |
| **Template** | A .NET project without its own Dockerfile. The publisher runs `dotnet publish` (runtime, framework, self-contained, or a `.pubxml`), copies the output selected by the **File Set** into a generated Dockerfile (base image, working directory, environment, ports, time zone, entrypoint, extra `RUN` lines), or uses an existing Dockerfile against that output. **Preview generated Dockerfile** and **Preview file set from folder** show the result first |
| **Compose** | A `compose.yml` with `build:` services. **Read Compose build services** lists them; each selected service gets its own `root/container` and version tag. Only `build` runs, never `up` |
| **Set** | Several profiles in order, for example a Base image and an App image built on it. Steps choose Build and Push; named file lists split one publish output between the images |

#### Target

![Profile: Target](images/registry-guide/profile-target.png)

| # | Field | What it means |
| --- | --- | --- |
| 1 | **Type** | **BuiltIn**: the registry of the server you are connected to. **Custom**: any other registry (for example GHCR) |
| 2 | **Server**, **Root**, **Container** | The built-in destination. Filled by button 6; the server is read-only and has to match the active connection |
| 3 | **Host**, **Repository** | The Custom destination, for example `ghcr.io` and `acme/api` |
| 4 | **Version Tag** | Required. The fixed tag of this build, for example `1.0.0-alpha.1`. By convention it is never overwritten |
| 5 | **Extra Tags** and **Import lines from .txt** | One tag per line, pushed after the version tag, for example the channel tag `alpha`. They are not added automatically. Adding `latest` shows a warning before the push |
| 6 | **Use active built-in server and select destination** | Shows two lists under the button: choose the root, then the container. The message `Built-in target: root/container` confirms the choice |
| 7 | Server address | The active connection, with a copy button. Paste it into the credential's **Scope Host** |
| 8 | **Refresh latest versions** | Shows the last version pushed from this computer and the latest tag in the registry |

Known issues with button 6 (see [publish-ui-review](../ideas/publish-ui-review.md)):
- The lists show `Em.Api.Core.Models.CtnRootInfo` instead of the root or container names. Rely on the
  confirmation message `Built-in target: root/container`.
- A root without containers gives an empty second list, without a message. Create the container first.
- Each click adds another pair of lists below the old one. Use the bottom pair.

#### Credentials

![Profile: Credentials](images/registry-guide/profile-credentials.png)

| # | Field | What it means |
| --- | --- | --- |
| 1 | **Sensitive Data Storage** | **Separate** (default): secrets are kept outside the profile file, encrypted for your Windows account. **Plaintext**: secrets are written into the profile JSON as plain text; anyone who can read the file can use them |
| 2 | **Id** | Generated reference of the credential |
| 3 | **Purpose** | Keep `push` for a registry login |
| 4 | **Scope Host** | The registry host with its port when there is one, for example `registry.example.com`. A leading `https://` and letter case are ignored. A credential is never sent to another host, so a host that does not match gives "No credential for host '...'". A built-in destination uses the host of the active connection, so check that connection too |
| 5 | **Username** | The robot name |
| 6 | **Secret** | The robot token |
| 7 | **Remember** | Keeps the secret for the next sessions (Windows DPAPI, current user). Without it the secret lasts until the application closes |
| 8 | **Remove item** | Removes this credential |
| 9 | **Add Credentials** | Adds another credential, for example for a second registry used by `FROM` |

Each credential also has **Allow Http**, below **Remember**. It makes the publisher's own registry requests (Verify and
tag discovery) use plain `http://` for that host. It does not change how Docker pushes: the Docker daemon decides
between https and http, so a plain-HTTP registry must also be listed under `insecure-registries` in Docker. **Check**
reads Docker's registry settings and prints a `Warning:` line when the host would be refused, or when it is
`localhost` on a Docker Desktop whose engine runs in a VM.

To list the host without opening Docker's settings, use **More (⋮) > Add registry to Docker insecure-registries**. After
you confirm, it adds the profile's registry host to `insecure-registries` in `%USERPROFILE%\.docker\daemon.json`,
keeps every other setting, and saves a backup next to the file first. Restart Docker Desktop (tray icon > Restart) for
it to apply, then run Check again. Nothing is removed automatically; delete the entry by hand to undo it.

#### Advanced

![Profile: Advanced](images/registry-guide/profile-advanced.png)

| # | Field | What it means |
| --- | --- | --- |
| 1 | **Keep Workspace** | Keeps the run's work folder after the run, for inspection |
| 2 | **Require Release Notes** | Push and Build & Push refuse to run with empty release notes |
| 3 | **Use My Docker Login** | Uses the login already stored in your Docker (`docker login` or a credential helper) instead of the profile credential |
| 4 | **Save** | Saves the profile |
| 5 | **Save As** | Saves a copy with a new ID |

### History

![Publish history](images/registry-guide/publish-history.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Refresh** | Reloads the run list |
| 2 | **All profiles (including deleted)** | Shows runs of every profile; unchecked shows only the selected profile |
| 3 | **Open Log**, **Logs Folder**, **Export** | Opens the run's `output.log`, the logs folder, or saves the run as a `.zip` |
| 4 | **Delete Run**, **Delete All** | Deletes the selected run, or every run of its profile, permanently |
| 5 | Runs | Start time, profile, operation and result. A run that never finished shows **Interrupted** |
| 6 | Run detail | Everything recorded: target, release notes, artifacts with digests, stages and the non-secret settings |

### Publisher settings

![Publisher settings](images/registry-guide/publisher-settings.png)

| # | Control | What it does |
| --- | --- | --- |
| 1 | **Profiles** | Folder of the profile files (default `Documents\Em\Publish\Profiles`) |
| 2 | **Logs** | Folder of the run logs (default `Documents\Em\Publish\Logs`). Logs are kept until deleted |
| 3 | **Work** | Temporary build folders (default `AppData\Local\Em\Publish\Work`) |
| 4 | **Browse** | Picks a folder |
| 5 | **Open folder** | Opens it in Explorer |
| 6 | **Use default** | Restores the default path |
| 7 | **Save** | Saves. The three folders must be separate (none inside another) |

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `NAME_UNKNOWN` | The container was not created, the robot has no access to the root, or the root/container is disabled |
| `DENIED` on push | The robot has only Read on the root; set Write |
| `NAME_INVALID` | Uppercase letters, or more than two segments after the host |
| `unauthorized` on login | Wrong or regenerated token, expired token, or a disabled robot |
| Docker refuses `http://` | The server must be reached over HTTPS; plain HTTP works only for `localhost` or a host listed under `insecure-registries` |
| `Get "https://localhost:5132/v2/": ... Client.Timeout exceeded` on Build & Push | Docker Desktop's daemon runs in a VM, so its `localhost` is not Windows. Use a server the daemon can reach (the test server, or `Em.Api` bound to a non-loopback address) and add that `host:port` to Docker's `insecure-registries`, then select that connection |
| "No credential for host '...'" / "The credential for host '...' has no secret in this session" | No credential whose **Scope Host** equals the registry host (the message lists the hosts the profile does have), or its secret was not remembered. The registry host of a built-in destination is the active connection, so a profile saved for one server fails Check while another server is selected |
| "Built-in connection changed. Select this server and Check again." | The active connection differs from the profile's server; select the destination again |
| "Root missing. Open Containers, then refresh." / "Container missing..." | Create it on the Containers tab, or select it again |
| "Source/build settings changed; Prepare ulang." | The profile changed after Build; run Build again |
| "Version tag is required and must be a valid Docker tag." | Fill **Version Tag**: letters, digits, `_`, `.`, `-`, up to 128 characters |
| "Release notes are required." | Fill **Release notes** or clear **Require Release Notes** |
| "Registry storage disabled." | The registry is off on the server; see [3. Settings tab](#3-settings-tab) |

## Maintainer notes

The screenshots are rendered offline from the real controls with sample data by a harness kept outside the
repository. Run from the repo root:
`dotnet run --project ..\.artefacts\em-system\scripts\registry-guide-render -- doc/engine/images/registry-guide`.
It writes the annotated PNGs into that folder. Publisher paths are kept in memory under a temporary folder, so
it never touches the real profiles, logs or registry keys.
