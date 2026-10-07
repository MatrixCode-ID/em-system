using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Em.Api.Core.Models;
using Em.Api.Shared;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>Base class of every module service: request info, logging, database access, and business tasks.</summary>
   public abstract class ServicesBase
   {
      /// <summary>The application host. Filled by the engine before the action is called.</summary>
      public EmApp App { get; internal set; } = null!;

      /// <summary>The HTTP context of the request being handled. Filled by the engine before the action is called.</summary>
      public HttpContext HttpContext { get; internal set; } = null!;

      /// <summary>
      /// Logger whose category automatically follows the type of the running service
      /// (e.g. <c>IEmApiCoreServices</c>), filled in automatically by the engine before the action is called.
      /// </summary>
      public ILogger Logger { get; internal set; } = null!;

      /// <summary>
      /// All information about the request being handled - who the caller is, by which way they proved it,
      /// and which action it is aimed at - together with its rights checker
      /// (<c>Request.RequireAdmin()</c> and friends). Filled by the gate before the action is called, so
      /// inside an action it is never null.
      /// </summary>
      /// <remarks>
      /// When its value is <see cref="ActionRequest.None"/>, the code is being called from outside the
      /// request path - that does not mean the gate has a hole.
      /// </remarks>
      public ActionRequest Request { get; internal set; } = ActionRequest.None;

      /// <summary>
      /// Signals when the work of this action is no longer worth continuing - either because the caller left
      /// (the application was closed, the network dropped, or later: a cancel button was pressed), or because
      /// the time limit set by <c>EmAppBuilder.HttpRequestTimeout</c> has passed.
      /// </summary>
      /// <remarks>
      /// Observing it is a <b>choice</b>, not a requirement: an action that ignores it still runs to completion
      /// as before. The best candidates to observe it are long read jobs - big loops, reports, exports - by
      /// passing it on to EF calls (<c>ToListAsync(AbortToken)</c> and the like).
      /// <para>
      /// <b>Write actions should preferably not observe it</b> unless the author really knows at which point
      /// stopping is safe. Stopping in the middle of a series of writes does not produce a "not done" state,
      /// but a state nobody knows - including the caller who has already left. That is why the engine itself
      /// never limits the time of <c>POST</c> actions; in a <c>POST</c> action this token only signals when
      /// the caller leaves, and the decision of how to react lies entirely with the action's author.
      /// </para>
      /// <para>
      /// Only the gate fills it, and only on the service that owns the running action. Other classes that also
      /// derive from <see cref="ServicesBase"/> but are obtained through DI still hold
      /// <see cref="CancellationToken.None"/> - just like <see cref="Request"/> stays
      /// <see cref="ActionRequest.None"/> outside the request path. A helper class that really needs to observe
      /// it can ask for <c>IHttpContextAccessor</c> and read <c>HttpContext.RequestAborted</c> itself.
      /// </para>
      /// </remarks>
      public CancellationToken AbortToken { get; internal set; } = CancellationToken.None;

      /// <summary>
      /// The user calling this action, according to the token they carry. Stays <c>null</c> for public actions
      /// that are called without a token. Successor of <see cref="ActionRequest.cUserId"/> on
      /// <see cref="Request"/>.
      /// </summary>
      public string? CallerUserId => Request.cUserId;

      /// <summary>
      /// The session that issued the caller's token - this is what gets ended when the user signs out of this
      /// device only. Successor of <see cref="ActionRequest.cUserSessionId"/> on <see cref="Request"/>.
      /// </summary>
      public string? CallerSessionId => Request.cUserSessionId;

      /// <summary>
      /// Whether the caller of this action has full rights as an administrator. Always <c>false</c> while
      /// <see cref="CallerUserId"/> is still empty - no identity, no rights. Successor of
      /// <see cref="ActionRequest.IsAdmin"/> on <see cref="Request"/>.
      /// </summary>
      public bool CallerIsAdmin => Request.IsAdmin;

      /// <summary>
      /// Gets a service from the DI container of the request being handled. Read-only route: service
      /// registration all happens at startup through <c>EmAppBuilder</c>.
      /// </summary>
      public object? GetService(Type serviceType) {
         return App.ServiceProvider.GetService(serviceType);
      }

      /// <inheritdoc cref="GetService(Type)" />
      public T? GetService<T>() {
         return App.ServiceProvider.GetService<T>();
      }

      /// <summary>
      /// Gets a service from the DI container registered with the key <paramref name="connectionName"/> -
      /// used for the <see cref="System.Data.IDbConnection"/> of connections other than "Default", which are
      /// registered by name through <c>EmAppBuilder.AddExtraDbConn</c>.
      /// </summary>
      /// <exception cref="InvalidOperationException">
      /// Thrown when <paramref name="connectionName"/> is not a registered database connection name, naming
      /// the names that are registered - unlike the ordinary <see cref="GetService(Type)"/>, which may return
      /// <c>null</c> for an unregistered service, because a misspelled connection name here must not disguise
      /// itself as a <see cref="NullReferenceException"/> a few lines later.
      /// </exception>
      public object? GetService(Type serviceType, string connectionName) {
         var value = App.ServiceProvider.GetKeyedService(serviceType, connectionName);
         if (value is not null) {
            return value;
         }

         if (GetService<IEmDbConnectionFactory>() is { } connectionFactory &&
             !connectionFactory.ConnectionNames.Contains(connectionName, StringComparer.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
               $"No database connection named '{connectionName}' is registered. Registered connections: {EmDbConnectionFactory.DescribeKnownConnections(connectionFactory.ConnectionNames)}.");
         }

         return value;
      }

      /// <inheritdoc cref="GetService(Type,string)" />
      public T? GetService<T>(string connectionName) {
         return (T?)GetService(typeof(T), connectionName);
      }

      /// <summary>
      /// Reads a metadata value by key, a cross-module core datum available to all services without having
      /// to inject ApiCoreContext/ApiCoreServices themselves.
      /// </summary>
      public async Task<string?> GetMetaValue(string key) {
         var ctx = GetService<ApiCoreContext>()!;
         return (await ctx.ta_Metas.SingleOrDefaultAsync(r => r.cMetaKey == key))?.cMetaValue;
      }

      /// <summary>
      /// Same as <see cref="GetMetaValue(string)"/>, but the result is converted to type <typeparamref name="T"/>.
      /// </summary>
      public async Task<T?> GetMetaValue<T>(string key) {
         var rawValue = await GetMetaValue(key);
         if (rawValue is null) {
            return default;
         }

         var nullableType = Nullable.GetUnderlyingType(typeof(T));
         var actualType = nullableType ?? typeof(T);

         if (actualType == typeof(string)) {
            return (T)(object)rawValue;
         }

         if (actualType.IsEnum) {
            return (T)Enum.Parse(actualType, rawValue, ignoreCase: true);
         }

         if (actualType == typeof(Guid)) {
            return (T)(object)Guid.Parse(rawValue);
         }

         var converter = TypeDescriptor.GetConverter(actualType);
         if (converter.CanConvertFrom(typeof(string))) {
            return (T?)converter.ConvertFromInvariantString(rawValue);
         }

         return (T)Convert.ChangeType(rawValue, actualType, CultureInfo.InvariantCulture);
      }

      /// <summary>
      /// Saves (inserts/updates) a metadata value for a key.
      /// </summary>
      protected async Task SetMetaValue(string key, string value, string description = "") {
         var ctx = GetService<ApiCoreContext>()!;
         // Tracked on purpose: an existing row is updated by editing it in place below, and
         // reads are no-tracking by default.
         var meta = await ctx.ta_Metas.AsTracking().SingleOrDefaultAsync(r => r.cMetaKey == key);

         if (meta is null) {
            ctx.ta_Metas.Add(new ta_Meta {
               cMetaKey = key,
               cMetaValue = value,
               cMetaDescription = description,
               ustamp = DateTime.UtcNow,
            });
         }
         else {
            meta.cMetaValue = value;
            meta.ustamp = DateTime.UtcNow;
         }

         await ctx.SaveChangesAsync();
      }
      
      /// <summary>
      /// Gets the server's RSA public/private key pair from metadata. If there is none yet, or the requested
      /// <paramref name="keySize"/> differs from the stored one, a new key pair of that size is generated
      /// automatically and stored (replacing the old one).
      /// </summary>
      public async Task<RsaKeyPair> GetServerRsaKeyAsync(int keySize = 2048) {
         const string ServerRsaPublicKeyMetaKey = "ServerRsaPublicKey";
         const string ServerRsaPrivateKeyMetaKey = "ServerRsaPrivateKey";
         const string ServerRsaKeySizeMetaKey = "ServerRsaKeySize";

         var ctx = GetService<ApiCoreContext>()!;
         var metaKeys = new[] { ServerRsaPublicKeyMetaKey, ServerRsaPrivateKeyMetaKey, ServerRsaKeySizeMetaKey };

         async Task<(string? PublicKey, string? PrivateKey, int? KeySize)> ReadRsaMeta() {
            var existing = await ctx.ta_Metas
               .Where(r => metaKeys.Contains(r.cMetaKey))
               .ToDictionaryAsync(r => r.cMetaKey, r => r.cMetaValue);

            existing.TryGetValue(ServerRsaPublicKeyMetaKey, out var publicKey);
            existing.TryGetValue(ServerRsaPrivateKeyMetaKey, out var privateKey);
            int? readKeySize = existing.TryGetValue(ServerRsaKeySizeMetaKey, out var keySizeRaw) &&
                               int.TryParse(keySizeRaw, out var parsed)
               ? parsed
               : null;

            return (
               string.IsNullOrWhiteSpace(publicKey) ? null : publicKey,
               string.IsNullOrWhiteSpace(privateKey) ? null : privateKey,
               readKeySize
            );
         }

         var existingMeta = await ReadRsaMeta();
         if (existingMeta is { PublicKey: not null, PrivateKey: not null } && existingMeta.KeySize == keySize) {
            return new RsaKeyPair(existingMeta.PublicKey, existingMeta.PrivateKey);
         }

         using var rsa = RSA.Create(keySize);
         var publicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
         var privateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

         try {
            await SetMetaValue(ServerRsaPublicKeyMetaKey, publicKey, "Auto-generated server RSA public key.");
            await SetMetaValue(ServerRsaPrivateKeyMetaKey, privateKey, "Auto-generated server RSA private key.");
            await SetMetaValue(ServerRsaKeySizeMetaKey, $"{keySize}", "Server RSA key size.");
         }
         catch (DbUpdateException) {
            var winner = await ReadRsaMeta();
            if (winner is { PublicKey: not null, PrivateKey: not null }) {
               return new RsaKeyPair(winner.PublicKey, winner.PrivateKey);
            }

            throw;
         }

         return new RsaKeyPair(publicKey, privateKey);
      }

      #region Business Task

      private BusinessTaskRunner BusinessTasks => App.ServiceProvider.GetRequiredService<BusinessTaskRunner>();

      /// <summary>
      /// Starts a business task without a result to fetch (e.g. creating an archive, sending email), then
      /// returns right away without waiting for the work to finish.
      /// </summary>
      /// <remarks>
      /// <para>
      /// The work runs <b>outside this request</b>, even after its client is closed. Therefore, inside that
      /// work do not use anything belonging to this action: <c>this</c>, <see cref="Request"/>,
      /// <see cref="AbortToken"/>, <see cref="GetService{T}()"/>, or services taken from the request. Use
      /// <see cref="BusinessTaskContext.Services"/> for services (DbContext and the like) and
      /// <see cref="BusinessTaskContext.Starter"/> for the starter's identity. Copy values needed from the
      /// request (action arguments, request content) into local variables before calling this method.
      /// </para>
      /// <para>
      /// <see cref="BusinessTaskContext.CancellationToken"/> only signals when the task is cancelled or the
      /// server is shut down. Pass it on to every call that may take long; stopping because of it is recorded
      /// as cancelled. Any other exception makes the task recorded as failed, with the exception message as
      /// its error message, and a failed task stays visible until cleared.
      /// </para>
      /// <para>
      /// <see cref="BusinessTaskOptions.Scope"/> decides who sees and manages this task: a personal task
      /// belongs to its starter and appears in their personal task list; a global task belongs to this
      /// module's screen, which asks for its status through <see cref="FindBusinessTask"/>. A task may wait
      /// with queued status when the limit on concurrently running tasks is full.
      /// </para>
      /// </remarks>
      /// <param name="options">Key, title, and scope of the task.</param>
      /// <param name="work">The work.</param>
      /// <returns>A snapshot of the task that was just started.</returns>
      /// <exception cref="ActionException">
      /// 409 when a task with the same key is still queued or running; its message names who started it.
      /// 401 when this action is called without identity.
      /// </exception>
      protected BusinessTaskInfo StartBusinessTask(BusinessTaskOptions options, Func<BusinessTaskContext, Task> work) {
         ArgumentNullException.ThrowIfNull(work);
         return BusinessTasks.Start(options, BusinessTaskOutputKind.None, Request, ModuleName, async (ctx, _) => {
            await work(ctx);
            return null;
         });
      }

      /// <summary>
      /// Starts a business task whose result is JSON data (e.g. loading a year of invoice data). The value
      /// returned by its work is kept by the server until cleared, and fetched by its owner through their
      /// personal task list.
      /// </summary>
      /// <remarks>
      /// The rules for using the work are the same as
      /// <see cref="StartBusinessTask(BusinessTaskOptions, Func{BusinessTaskContext, Task})"/>: it runs
      /// outside the request, so it may only use the content of <see cref="BusinessTaskContext"/>.
      /// </remarks>
      /// <param name="options">Key, title, and scope of the task.</param>
      /// <param name="work">The work; its return value becomes the task result.</param>
      /// <returns>A snapshot of the task that was just started.</returns>
      /// <exception cref="ActionException">409 when a task with the same key is still queued or running.</exception>
      protected BusinessTaskInfo StartBusinessTask<TResult>(BusinessTaskOptions options,
         Func<BusinessTaskContext, Task<TResult>> work) {
         ArgumentNullException.ThrowIfNull(work);
         return BusinessTasks.Start(options, BusinessTaskOutputKind.Json, Request, ModuleName, async (ctx, folder) => {
            var result = await work(ctx);
            Directory.CreateDirectory(folder);
            var path = BusinessTaskRunner.ResultPath(folder, BusinessTaskOutputKind.Json);
            await using var file = File.Create(path);
            await System.Text.Json.JsonSerializer.SerializeAsync(file, result, Defaults.ResponseJsonOptions,
               ctx.CancellationToken);
            return file.Length;
         });
      }

      /// <summary>
      /// Starts a business task whose result is a file (e.g. Excel). The work writes the file content to the
      /// stream it is given; the stream belongs to the engine and is closed by the engine.
      /// <see cref="BusinessTaskOptions.ResultFileName"/> is required.
      /// </summary>
      /// <remarks>
      /// The rules for using the work are the same as
      /// <see cref="StartBusinessTask(BusinessTaskOptions, Func{BusinessTaskContext, Task})"/>: it runs
      /// outside the request, so it may only use the content of <see cref="BusinessTaskContext"/>.
      /// </remarks>
      /// <param name="options">Key, title, scope, and result file name of the task.</param>
      /// <param name="writeResult">The work, which writes the result to the stream it is given.</param>
      /// <returns>A snapshot of the task that was just started.</returns>
      /// <exception cref="ActionException">409 when a task with the same key is still queued or running.</exception>
      protected BusinessTaskInfo StartBusinessTask(BusinessTaskOptions options,
         Func<BusinessTaskContext, Stream, Task> writeResult) {
         ArgumentNullException.ThrowIfNull(writeResult);
         return BusinessTasks.Start(options, BusinessTaskOutputKind.File, Request, ModuleName, async (ctx, folder) => {
            Directory.CreateDirectory(folder);
            var path = BusinessTaskRunner.ResultPath(folder, BusinessTaskOutputKind.File);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
               FileOptions.Asynchronous);
            await writeResult(ctx, file);
            await file.FlushAsync(ctx.CancellationToken);
            return file.Length;
         });
      }

      /// <summary>
      /// The live task with key <paramref name="key"/>, or if there is none, the last one that failed and has
      /// not been cleared; <c>null</c> when there is neither. For <see cref="BusinessTaskScope.Personal"/>
      /// the one looked up belongs to the caller of this action.
      /// </summary>
      /// <remarks>
      /// This method checks no rights: who may ask is left to the claim of the action that calls it. For that
      /// reason the <see cref="BusinessTaskInfo.CanCancel"/> and <see cref="BusinessTaskInfo.CanClear"/> flags
      /// in its result only follow the task status; <see cref="BusinessTaskInfo.CanReadResult"/> remains only
      /// for the owner and administrators.
      /// </remarks>
      protected BusinessTaskInfo? FindBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.Find(key, scope, Request);

      /// <summary>
      /// Same as <see cref="FindBusinessTask"/>, for every key that starts with
      /// <paramref name="keyPrefix"/>: one result per key.
      /// </summary>
      /// <remarks>
      /// This method checks no rights: who may ask is left to the claim of the action that calls it.
      /// </remarks>
      protected BusinessTaskInfo[] FindBusinessTasks(string keyPrefix, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.FindMany(keyPrefix, scope, Request);

      /// <summary>
      /// Cancels the live task with key <paramref name="key"/>. A running task only really stops when its work
      /// observes the cancellation token.
      /// </summary>
      /// <remarks>
      /// This method does not check who started the task: who may cancel is left to the claim of the action
      /// that calls it.
      /// </remarks>
      /// <exception cref="ActionException">404 when there is no task with that key, 409 when it has already finished.</exception>
      protected void CancelBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.CancelByKey(key, scope, Request);

      /// <summary>
      /// Clears the finished task with key <paramref name="key"/>, together with its stored result.
      /// </summary>
      /// <remarks>
      /// This method does not check who started the task: who may clear is left to the claim of the action
      /// that calls it.
      /// </remarks>
      /// <exception cref="ActionException">404 when there is no task with that key, 409 when it is still live.</exception>
      protected void ClearBusinessTask(string key, BusinessTaskScope scope = BusinessTaskScope.Global) =>
         BusinessTasks.ClearByKey(key, scope, Request);

      // Only action services start tasks, and every one of them carries [Module].
      private string ModuleName => ModuleAttribute.ResolveName(GetType());

      #endregion
   }
}
