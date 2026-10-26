# Debug mode: Switch User, Simulate Login and Debug Sign In

A WPF host enters debug mode when it is built with `builder.AddDebug(...)` and at least one debug
connection. The application then starts signed in as the **SYSTEM DEBUGGER** account, and every request
carries the debug token signed with the developer's key (see `EM_DEBUG_TOKEN` in
[src/backend/README.md](../../src/backend/README.md)). Two tools in the **Tools** menu (multi-tab) or on the
home screen (single page) help test what a real user sees, without restarting the application.

## Three switches

`EmApp` exposes the state as three properties:

| Property | Meaning |
| --- | --- |
| `IsDebugMode` | The build has a debug configuration. Never changes while the application runs. |
| `IsDebugActive` | Debug features are on: `IsDebugMode` and not simulating a login. Controls the debug connection pick, the debug card on home, Switch User and Simulate Login. |
| `IsDebugBypass` | The client skips its own permission checks: only while debug is active **and** the active user is the debugger account. |

Code that hides or refuses something by permission should check `IsDebugBypass` (for example
`EmApp.CanOpen`, the Approval Manager and Business Task Manager checks); code that shows a debug-only
feature should check `IsDebugActive`. WPF raises `DebugStateChanged` whenever Simulate Login starts or ends;
MAUI refreshes the host through its active-user notifications.

## Switch User

**Tools > Switch User** (only while debug is active) lists the built-in administrator and every stored
user as cards, with a search box. The built-in administrator card appears only when the server confirms
the account is switched on (`GetMeta_AdminAccountEnabled`, answered only to debug token callers and 404
to everyone else); when that cannot be confirmed - server unreachable, error, older server - the card is
hidden. Picking an account and pressing **Switch** (or double-clicking it):

1. asks every open screen to close, the same way closing the window does - one refusal cancels the switch;
2. closes every tab and window;
3. makes the account the active user. Requests keep the debug token and add the account's identity in the
   `X-Em-User` header; the server then runs them with that account's real permissions;
4. reloads the claims and rebuilds the menus for that account.

The client follows the same permissions as the server: once another account is active, the debug bypass
is off, so menus, `NavigateTo`, approval and task screens behave as they would for that user. The account
button's tooltip reads *Debug: acting as &lt;account&gt;*.

Suspended, pending and deleted accounts are shown dimmed and cannot be picked; the server refuses them
anyway. If the server refuses the account (for example an administrator switched off after the list
loaded), the debugger
account is restored and the error is shown. The list itself is read through a separate client without
the identity header, so it loads even while the active account may not read users.

The debugger is not a card: it has a strip of its own under the dialog's banner. While another account is
active the strip turns warning (*Acting as &lt;account&gt;, not as the debugger.*) with a **Back to
debugger** button that switches back in one click; on the debugger itself it only says so.

The choice is not remembered: every start of a debug build begins as SYSTEM DEBUGGER.

## Simulate Login

**Tools > Simulate Login** runs the debug build as the application runs without debug, in the same process:

- open screens are asked to close, then the workspace is closed;
- the debug token is taken off every debug connection, so no request carries it;
- the login screen appears. Signing in uses a real account and password, the session is a real JWT
  session, and sign out or an expired session returns to the login screen with its notice;
- **Keep me signed in** is hidden and nothing about the session is stored or cleared in the Registry
  (`RememberSignIn`, the remembered account and profile, the stored session belong to the runs without
  debug);
- Switch User, Simulate Login, the debug connection pick and the debug card are hidden.

While signed in during the simulation, a `SIMULATED` chip with an exit button sits in the title bar
(multi-tab) or the navigation header (single page). The login screen shows **Exit simulation (back to
debugger)**. Leaving the simulation while signed in asks open screens to close, signs the session out of
the server (a failure is ignored), puts the debug token back, returns to the default debug connection and
makes SYSTEM DEBUGGER active again.

## Debug Sign In

During Simulate Login the login screen (Material and Classic) shows **Debug Sign In** next to **Sign in**.
It signs in as the typed account on the chosen connection **without its password**; the password field is
ignored:

- the client sends `PostGetMeta_DebugSignIn` through a client of its own that carries the debug token for
  that one request only; every other request of the simulation still runs without it;
- the server answers only debug token callers (404 to everyone else, like `GetMeta_AdminAccountEnabled`).
  Only the password is skipped: an unknown account, a suspended, pending or deleted user, and a switched off
  built-in administrator are refused with the same *Incorrect username or password* as a normal sign in;
- the session is a real JWT session opened the same way as a normal sign in, and is not marked or audited
  differently. Nothing about it is stored, as with any simulated sign in. From there on everything behaves
  as after **Sign in**: the `SIMULATED` chip, sign out, an expired session, and leaving the simulation.

A holder of the debug key can therefore open a session for any account that may sign in. That is no more
than Switch User already allows; the debug token stays outside `#if DEBUG` on the server by design.

MAUI also tracks `IsSimulatingLogin` and removes the debug token from all known connections during
simulation. Open **Simulate Login** from the account panel. The login screen pairs **Debug Sign In**
with **Cancel simulation · Return to SD**; neither cancellation nor returning to SD requires an account,
password, or successful sign-in. Both WPF login styles use the same cancellation label.

After signing in on MAUI, a **SIMULATED** strip with **Return to SD** remains visible above the screen;
the account panel also offers **Exit simulation · Return to SD**. Sign out works during simulation and
returns to login, where cancellation remains available. Returning to SD ends the simulated server session,
releases its navigation history, restores the debug token and the previously selected debug connection,
and makes SYSTEM DEBUGGER active again. Simulated password and debug sign-ins never save or clear the
ordinary remembered session. Recreating the Android activity preserves the active simulation.

When a real session is installed, both clients use its user ID for request identity while the user's
profile is loading. The old debugger identity must not accompany a Bearer request without a debug token:
the API deliberately refuses that combination. The account name is attached once the matching user has
loaded. This avoids reporting a successful simulated sign-in as an unreachable server during bootstrap.
The identity refresh happens inside `BeginSessionAsync`, before the first profile lookup, because
credential services may already hold the API client and do not reacquire it for every call.

MAUI currently has no separate Connection Manager screen. Debug server choices are declared by the host
and selected in the account panel; the server field on Simulate Login follows that choice. In a normal
build, the login screen accepts the server address and retains the profile after successful sign-in.

## Sign out in debug mode

While debug is active, **Sign Out** is hidden from the account menu of both layouts and `SignOutAsync`
does nothing: the debugger account never signed in. Use Simulate Login to test signing in and out. During
the simulation Sign Out is shown and works as without debug.

## Maintainer notes

- Unit tests: `tests/Em.Ui.Wpf.Core.Tests/DebugStateTests.cs` (the three switches).
- Session bootstrap identity regression: `tests/Em.Ui.Wpf.Core.Tests/SessionCallerIdentityTests.cs`.
  Local test logs are in `../.artefacts/em-system/scripts/session-caller-smoke/out/`.
- Render harness for the dialog and the simulated login screens:
  `dotnet run --project ..\.artefacts\em-system\scripts\user-roles-render`.
- Live smoke (impersonation, suspended account, simulated sign in and sign out against a local `Em.Api`):
  `dotnet run --project ..\.artefacts\em-system\scripts\user-roles-smoke`, then `cleanup.sql` in the same
  folder.
