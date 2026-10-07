# WPF debug mode: Switch User and Simulate Login

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
feature should check `IsDebugActive`. `DebugStateChanged` is raised whenever Simulate Login starts or ends.

## Switch User

**Tools > Switch User** (only while debug is active) lists the debugger account, the built-in
administrator and every stored user, with a search box. Picking an account and pressing **Switch** (or
double-clicking it):

1. asks every open screen to close, the same way closing the window does - one refusal cancels the switch;
2. closes every tab and window;
3. makes the account the active user. Requests keep the debug token and add the account's identity in the
   `X-Em-User` header; the server then runs them with that account's real permissions;
4. reloads the claims and rebuilds the menus for that account.

The client follows the same permissions as the server: once another account is active, the debug bypass
is off, so menus, `NavigateTo`, approval and task screens behave as they would for that user. The account
button's tooltip reads *Debug: acting as &lt;account&gt;*.

Suspended, pending and deleted accounts are shown dimmed and cannot be picked; the server refuses them
anyway. If the server refuses the account (for example a disabled built-in administrator), the debugger
account is restored and the error is shown. The list itself is read through a separate client without
the identity header, so it loads even while the active account may not read users.

The choice is not remembered: every start of a debug build begins as SYSTEM DEBUGGER. Pick SYSTEM DEBUGGER
in the same dialog to go back.

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

## Sign out in debug mode

While debug is active, **Sign Out** is hidden from the account menu of both layouts and `SignOutAsync`
does nothing: the debugger account never signed in. Use Simulate Login to test signing in and out. During
the simulation Sign Out is shown and works as without debug.

## Maintainer notes

- Unit tests: `tests/Em.Ui.Wpf.Core.Tests/DebugStateTests.cs` (the three switches).
- Render harness for the dialog and the simulated login screens:
  `dotnet run --project ..\.artefacts\em-system\scripts\user-roles-render`.
- Live smoke (impersonation, suspended account, simulated sign in and sign out against a local `Em.Api`):
  `dotnet run --project ..\.artefacts\em-system\scripts\user-roles-smoke`, then `cleanup.sql` in the same
  folder.
