# Calling other services in-process

An action sometimes needs work that another service already exposes as actions - for example an import that
creates many users. Doing that from the client costs one HTTP request per step and quickly runs into the
per-address rate limit. Instead, the action can call the other service directly on the server, on behalf of
the same caller.

## How it works

Every service registered with `AddService<TInterface, TService>()` (engine services included) is built by DI
through a factory that connects it to the request it is resolved in. A service injected into another
service therefore sees:

| Member | Value inside a request | Inside a business task | Outside both |
| --- | --- | --- | --- |
| `Request` | the caller of the running request | the task's starter | `ActionRequest.None` |
| `HttpContext` | the running request's context | `null` | `null` |
| `AbortToken` | `HttpContext.RequestAborted` (caller leaves) | `None` | `None` |
| `App`, `Logger` | set | set | set |

The values are read on every access rather than copied, so a service resolved before the gate identified the
caller still answers for the identified caller. The service that owns the running action keeps the values
the gate assigns (including the GET time limit on `AbortToken`).

## Usage

Inject the other service's interface and call its methods:

```csharp
[Module("Invoices")]
public class InvoiceServices(InvoiceContext ctx, ICredentialServices credentials) : ServicesBase, IInvoiceServices
{
   [PostAction(claim: InvoiceClaims.Import)]
   public async Task<ImportResult> PostGetMeta_Invoice_ImportUsers(ImportRequest request) {
      Request.RequireAdmin();                       // the right to do what is called below
      foreach (var row in request.Rows) {
         await credentials.PostTa_User_New(BuildUser(row));
         await credentials.PostMeta_ResetPassword(row.UserId, request.InitialPassword);
      }
      ...
   }
}
```

`PostMeta_ResetPassword` runs its own `Request.RequireSelfOrAdmin(...)` against the caller of
`PostGetMeta_Invoice_ImportUsers`, exactly as if the caller had sent it.

## Rights

Only the checks written **inside** the called method run. The gate does not run for an in-process call, so
these are **not** checked:

- the claim on `[GetAction(claim: ...)]`/`[PostAction(claim: ...)]` and the module's "any claim" default;
- `IsPublicAction` and the signed-in requirement;
- the HTTP verb and the rate limit.

The calling action is responsible for holding the right to everything it calls - typically its own claim
plus an explicit check such as `Request.RequireAdmin()`. Never expose an action that forwards caller input to
an in-process call the caller could not make directly.

## Limits

- One request remains one rate-limit unit and one trace line; the calls inside it are not logged as separate
  requests.
- Calls share the request's DI scope, so they share its `DbContext` instances. Each called action manages its
  own transaction; there is no transaction spanning several calls unless the caller opens one on the same
  context and the called actions use that context.
- POST actions have no time limit; a long loop should still observe `AbortToken` between steps.
