# Module protocol endpoints

Product modules that speak their own protocol (for example a package feed) can hook into the API host
without going through the action dispatcher. These hooks are available on the API builder
(`EmAppBuilder`) during startup only.

## Public endpoint branch

```csharp
builder.AddPublicEndpoint("/protocol", async context => {
   // context.Request.PathBase == "/protocol", context.Request.Path is the remainder
});
```

- The branch is installed after forwarded headers and alongside the CDN (`/cdn`) and container registry
  (`/v2`), before routing and the action fallback.
- ASP.NET Core moves the prefix into `Request.PathBase`; the handler sees the remaining `Request.Path`.
  Build absolute resource URLs from scheme, host and `PathBase`.
- The branch owns its authentication, authorization, request body limits, cancellation and errors. Action
  claims, action rate limits and GET timeouts do not apply.
- Registration rejects empty or root prefixes, prefixes containing `\`, `%`, whitespace, `?`, `#`, `//` or
  `.`/`..` segments, the reserved `/api`, `/cdn` and `/v2`, and any prefix that overlaps another branch.

## Module infrastructure

The service collection is not exposed to modules; use these builder methods instead:

| Method | Registers |
| --- | --- |
| `AddSingleton<T>(factory)` | A singleton created by a factory |
| `AddHostedService<T>()` | A background `IHostedService` |
| `AddRobotAccessManager<T>()` | A scoped `IRobotAccessManager`, shown as a resource/grant provider in User Manager (see [Robots](engine-robots.md)) |

## Robot authentication

`RobotAuth` validates robot credentials for protocol branches:

- `RobotAuth.AuthenticateAsync(httpContext, robotContext)` reads Basic credentials (robot name + token).
- `RobotAuth.AuthenticateAsync(token, robotContext, cancellationToken)` validates a token without a
  username (for example an API key header).

Both enforce the active state and expiration, and update `LastUsed` periodically (at most once every five
minutes per robot). Invalid credentials return `null`; the caller answers 401. Authentication only proves
identity: the module **must still check its own grants** for every operation. There is no authentication
failure throttle yet.
