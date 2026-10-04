# Module protocol endpoints

Product modules can register a protocol branch with `EmAppBuilder.AddPublicEndpoint("/protocol", handler)`. The engine installs it after forwarded headers and alongside CDN/registry, before routing and action fallback. ASP.NET Core moves the prefix into `Request.PathBase`; the handler sees the remaining `Request.Path`. Build absolute resource URLs from scheme, host and PathBase.

The branch owns its authentication, authorization, request body limits, cancellation and errors. Action dispatcher claims, action rate limits and GET timeouts do not apply. Empty/root prefixes, reserved `/api`, `/cdn`, `/v2`, and overlapping branches are rejected during registration. `AddSingleton<T>(factory)` and `AddHostedService<T>()` register module infrastructure without exposing the service collection.

`AddRobotAccessManager<T>()` registers a scoped resource/grant provider for User Manager. Its deletion callbacks share the supplied identity transaction. `RobotAuth.AuthenticateAsync(context, identities)` handles Basic credentials; `AuthenticateAsync(token, identities, cancellationToken)` handles a token without username. Both enforce active state, expiration and periodic LastUsed updates. Invalid credentials return null. No authentication failure throttle is currently implemented.
