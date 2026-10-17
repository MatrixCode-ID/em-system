using Em.Api.Core.Approval;
using Em.Api.Core.Models;
using Em.Api.Core.ObjectStorage;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Em.Api.Core.IntegrationTests;

public sealed class ApprovalRedesignTests(SqlServerDatabase database) {
   private static CancellationToken Ct => TestContext.Current.CancellationToken;

   [Fact]
   public async Task DataTree_DeferredKeysConflictOverrideDeleteRestoreAndHistory() {
      await using var fx = await Fixture.CreateAsync(database, Ct);
      string request;
      using (var call = fx.Call(fx.Requester)) {
         var result = await call.Engine.SubmitDataAsync(DataChange.Create(new Root { Text = "first" })
            .With(DataChange.Create(new Child { Text = "child" })), "Create tree", Ct);
         request = result.ApprovalRequestId;
         Assert.False(result.AppliedImmediately);
         Assert.Empty(await call.Module.Roots.ToArrayAsync(Ct));
         Assert.Equal(2, await call.Core.ta_ApprovalRequestItems.CountAsync(x => x.cApprovalRequestId == request && x.json_object != null, Ct));
      }
      string rootId;
      using (var call = fx.Call(fx.Decider, decide: true)) {
         Assert.True((await call.Engine.DecideAsync(new(request, true), Ct)).Applied);
         var root = await call.Module.Roots.SingleAsync(Ct); rootId = root.RootId;
         Assert.Equal(rootId, (await call.Module.Children.SingleAsync(Ct)).RootId);
         Assert.False(string.IsNullOrEmpty(rootId));
         Assert.All(await call.Core.ta_ApprovalRequestItems.Where(x => x.cApprovalRequestId == request).ToArrayAsync(Ct), x => Assert.Null(x.json_object));
      }
      string update;
      using (var call = fx.Call(fx.Requester)) {
         update = (await call.Engine.SubmitDataAsync(DataChange.Update(new Root { RootId = rootId, Text = "first" },
            new Root { RootId = rootId, Text = "proposed" }), ct: Ct)).ApprovalRequestId;
         await call.Module.Roots.Where(x => x.RootId == rootId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Text, "concurrent"), Ct);
      }
      using (var call = fx.Call(fx.Decider, decide: true)) {
         Assert.False((await call.Engine.DecideAsync(new(update, true), Ct)).Applied);
         Assert.Equal("concurrent", (await call.Module.Roots.AsNoTracking().SingleAsync(Ct)).Text);
         var field = await call.Core.ta_ApprovalRequestItemFields.SingleAsync(Ct);
         Assert.True(field.cApprovalRequestItemFieldConflict);
         Assert.True((await call.Engine.DecideAsync(new(update, true, Overrides: [new(field.cApprovalRequestItemId, nameof(Root.Text), "Reviewed the concurrent change")]), Ct)).Applied);
         Assert.Equal("proposed", (await call.Module.Roots.AsNoTracking().SingleAsync(Ct)).Text);
      }
      using (var call = fx.Call(fx.Decider, decide: true)) {
         var deletion = await call.Engine.SubmitDataAsync(DataChange.Delete(new Root { RootId = rootId }), ct: Ct);
         Assert.True(deletion.AppliedImmediately);
         var row = await call.Module.Roots.AsNoTracking().SingleAsync(Ct);
         Assert.True(row.Deleted); Assert.NotNull(row.DeletedAt);
         await call.Engine.SubmitDataAsync(DataChange.Restore(new Root { RootId = rootId }), ct: Ct);
         row = await call.Module.Roots.AsNoTracking().SingleAsync(Ct);
         Assert.False(row.Deleted); Assert.Null(row.DeletedAt);
         var child = await call.Module.Children.AsNoTracking().SingleAsync(Ct);
         await call.Engine.SubmitDataAsync(DataChange.Update(row, row).With(DataChange.Delete(child)), ct: Ct);
         Assert.Empty(await call.Module.Children.ToArrayAsync(Ct));
      }
      string rejected;
      using (var call = fx.Call(fx.Requester)) rejected = (await call.Engine.SubmitDataAsync(DataChange.Create(new Root { Text = "reject" }), ct: Ct)).ApprovalRequestId;
      using (var call = fx.Call(fx.Decider, decide: true)) {
         await call.Engine.DecideAsync(new(rejected, false, Note: "No longer needed"), Ct);
         Assert.NotNull((await call.Core.ta_ApprovalRequestItems.SingleAsync(x => x.cApprovalRequestId == rejected, Ct)).json_object);
      }
      using (var call = fx.Call(fx.Requester)) {
         var withdrawn = await call.Engine.SubmitDataAsync(DataChange.Create(new Root { Text = "withdraw" }), ct: Ct);
         await call.Engine.WithdrawAsync(withdrawn.ApprovalRequestId, "Replaced draft", Ct);
         Assert.Equal(ApprovalStage.Cancelled, (await call.Core.ta_ApprovalRequests.FindAsync([withdrawn.ApprovalRequestId], Ct))!.cApprovalRequestStage);
      }
      using (var call = fx.Call(Defaults.AdminUserId, admin: true)) {
         Assert.Equal(403, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.SubmitDataAsync(DataChange.Create(new Root()), ct: Ct))).StatusCode);
      }
   }

   [Fact]
   public async Task System_ParallelLevelsBackupAdminVoidReinstateAndUnresolvedDynamicSigner() {
      await using var fx = await Fixture.CreateAsync(database, Ct);
      using (var call = fx.Call(fx.Admin, admin: true)) {
         await call.Engine.SaveSignerSettingsAsync("Test System", [new("Prepared", true, null, []),
            new("Check A", false, fx.Decider, [fx.Backup]), new("Check B", false, fx.Decider, []), new("Finish", false, fx.Decider, [])], Ct);
      }
      string request;
      using (var call = fx.Call(fx.Requester)) {
         request = await call.Engine.RequestAsync("Test System", "subject", "Summary", Ct);
         var header = await call.Core.ta_ApprovalRequests.FindAsync([request], Ct);
         Assert.Equal(2, header!.cApprovalRequestLevel);
         Assert.Single(await call.Core.ta_ApprovalRequestSignatures.Where(x => x.cApprovalRequestId == request).ToArrayAsync(Ct));
         Assert.Equal(409, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.RequestAsync("Test System", "subject", "Duplicate", Ct))).StatusCode);
      }
      string Step(Call call, string name) => call.Core.ta_ApprovalRequestSteps.Single(x => x.cApprovalRequestId == request && x.cApprovalRequestStepName == name).cApprovalRequestStepId;
      using (var call = fx.Call(fx.Requester)) {
         Assert.Equal(403, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.DecideAsync(new(request, true, Step(call, "Check A")), Ct))).StatusCode);
      }
      using (var call = fx.Call(fx.Backup)) {
         Assert.True((await call.Engine.DecideAsync(new(request, true, Step(call, "Check A")), Ct)).Applied);
         Assert.Equal(2, (await call.Core.ta_ApprovalRequests.FindAsync([request], Ct))!.cApprovalRequestLevel);
         Assert.True((await call.Core.ta_ApprovalRequestSignatures.SingleAsync(x => x.cUserId == fx.Backup, Ct)).cApprovalRequestSignatureBackup);
      }
      using (var call = fx.Call(fx.Decider)) {
         await call.Engine.DecideAsync(new(request, true, Step(call, "Check B")), Ct);
         Assert.Equal(3, (await call.Core.ta_ApprovalRequests.FindAsync([request], Ct))!.cApprovalRequestLevel);
      }
      using (var call = fx.Call(fx.Admin, admin: true)) {
         await call.Engine.DecideAsync(new(request, true, Step(call, "Finish")), Ct);
         Assert.True((await call.Core.ta_ApprovalRequestSignatures.SingleAsync(x => x.cUserId == fx.Admin, Ct)).cApprovalRequestSignatureAdmin);
         Assert.Equal(ApprovalStage.Approved, (await call.Engine.FindByKeyAsync("Test System", "subject", Ct))!.cApprovalRequestStage);
         var replacement = await call.Engine.ReinstateAsync(request, "Replacement", "Recheck", Ct);
         Assert.NotEqual(request, replacement);
         Assert.Equal(ApprovalStage.ReinstatedAfterFinish, (await call.Core.ta_ApprovalRequests.FindAsync([request], Ct))!.cApprovalRequestStage);
         Assert.Equal(request, (await call.Core.ta_ApprovalRequests.FindAsync([replacement], Ct))!.cReinstateOf);
         await call.Engine.VoidAsync(replacement, "Edit subject", Ct);
         Assert.Equal(ApprovalStage.Cancelled, (await call.Core.ta_ApprovalRequests.FindAsync([replacement], Ct))!.cApprovalRequestStage);
         var another = await call.Engine.RequestAsync("Test System", "subject", "Retry", Ct);
         Assert.NotEqual(replacement, another);
         var step = await call.Core.ta_ApprovalRequestSteps.FirstAsync(x => x.cApprovalRequestId == another && x.cApprovalRequestStepName == "Check A", Ct);
         await call.Engine.DecideAsync(new(another, false, step.cApprovalRequestStepId, "Rejected"), Ct);
         Assert.Equal(ApprovalStage.Rejected, (await call.Engine.FindByKeyAsync("Test System", "subject", Ct))!.cApprovalRequestStage);
         Assert.Equal(409, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.RequestAsync("Test System", "missing", "Unresolved DS", Ct))).StatusCode);
         await call.Engine.RequestAsync("Test System", "after-failure", "Retry in the same scope", Ct);
         Assert.False(await call.Core.ta_ApprovalRequests.AnyAsync(x => x.cSubjectKey == "missing", Ct));
      }
   }

   [Fact]
   public async Task MissingEntityAndIncompatibleCreateSnapshotCanStillBeRejected() {
      await using var fx = await Fixture.CreateAsync(database, Ct);
      string rootId;
      using (var call = fx.Call(fx.Decider, decide: true)) {
         await call.Engine.SubmitDataAsync(DataChange.Create(new Root { Text = "original" }), ct: Ct);
         rootId = (await call.Module.Roots.SingleAsync(Ct)).RootId;
      }
      string missing;
      string incompatible;
      using (var call = fx.Call(fx.Requester)) {
         missing = (await call.Engine.SubmitDataAsync(DataChange.Update(new Root { RootId = rootId, Text = "original" }, new Root { RootId = rootId, Text = "changed" }), ct: Ct)).ApprovalRequestId;
         incompatible = (await call.Engine.SubmitDataAsync(DataChange.Create(new Root { Text = "stale schema" }), ct: Ct)).ApprovalRequestId;
         await call.Module.Roots.ExecuteDeleteAsync(Ct);
         await call.Core.ta_ApprovalRequestItems.Where(x => x.cApprovalRequestId == incompatible)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.json_object, "{\"unexpected\":true}"), Ct);
      }
      foreach (var request in new[] { missing, incompatible }) {
         using (var call = fx.Call(fx.Decider, decide: true))
            Assert.Equal(409, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.DecideAsync(new(request, true), Ct))).StatusCode);
         using (var call = fx.Call(fx.Decider, decide: true)) {
            await call.Engine.DecideAsync(new(request, false, Note: "Source changed"), Ct);
            Assert.Equal(ApprovalStage.Rejected, (await call.Core.ta_ApprovalRequests.FindAsync([request], Ct))!.cApprovalRequestStage);
         }
      }
      using (var call = fx.Call(fx.Requester)) {
         await call.Core.ta_Users.Where(x => x.cUserId == fx.Requester).ExecuteUpdateAsync(s => s.SetProperty(x => x.cUserState, UserState.Inactive), Ct);
         Assert.Equal(403, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.SubmitDataAsync(DataChange.Create(new Root()), ct: Ct))).StatusCode);
      }
   }

   [Fact]
   public async Task Attachments_KeepExactVersionRequireAccessAndCompensateFailedMetadata() {
      await using var storage = await ObjectStoreFixture.CreateAsync(database, Ct);
      var bucket = await storage.AddBucketAsync(Ct);
      await storage.NewContext().Buckets.Where(x => x.cStoreBucketName == bucket)
         .ExecuteUpdateAsync(s => s.SetProperty(x => x.cStoreBucketName, "approval-attachments"), Ct);
      var store = storage.NewStore();
      await using var fx = await Fixture.CreateAsync(database, Ct, store);
      string request;
      using (var call = fx.Call(fx.Admin, admin: true)) {
         await call.Engine.SaveSignerSettingsAsync("Test System", new SystemDefinition().Steps
            .Select(x => new ApprovalSignerSetting(x.Name, false, fx.Decider, [])).ToArray(), Ct);
      }
      using (var call = fx.Call(fx.Requester)) request = await call.Engine.RequestAsync("Test System", "attachments", "Files", Ct);
      string attachment;
      using (var call = fx.Call(fx.Requester)) {
         Assert.Equal(400, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.AttachAsync(
            new(request, "unknown-comment", "document.txt"), ObjectStoreFixture.Content("invalid"), Ct))).StatusCode);
         var row = await call.Engine.AttachAsync(new(request, null, "document.txt"), ObjectStoreFixture.Content("first version"), Ct);
         attachment = row.cApprovalRequestAttachmentId;
         await store.PutAsync("approval-attachments", row.cApprovalRequestAttachmentKey, ObjectStoreFixture.Content("later version"), ct: Ct);
         Assert.Equal("first version", await ObjectStoreFixture.ReadAsync(await call.Engine.AttachmentAsync(attachment, Ct), Ct));
         await call.Core.Database.ExecuteSqlRawAsync("ALTER TABLE ta_ApprovalRequestAttachment ADD CONSTRAINT CK_TestAttachmentFailure CHECK (cApprovalRequestAttachmentName <> 'reject.txt')", Ct);
         var before = (await store.ListAsync("approval-attachments", ct: Ct)).Objects.Length;
         await Assert.ThrowsAsync<DbUpdateException>(() => call.Engine.AttachAsync(new(request, null, "reject.txt"), ObjectStoreFixture.Content("compensate"), Ct));
         Assert.Equal(before, (await store.ListAsync("approval-attachments", ct: Ct)).Objects.Length);
      }
      using (var call = fx.Call(fx.Backup)) {
         Assert.Equal(403, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.AttachmentAsync(attachment, Ct))).StatusCode);
         Assert.Equal(403, (await Assert.ThrowsAsync<ActionException>(() => call.Engine.AttachAsync(new(request, null, "forbidden.txt"), ObjectStoreFixture.Content("forbidden"), Ct))).StatusCode);
      }
   }

   [Fact]
   public async Task ConcurrentSystemRequestsKeepOnlyOneActiveRequest() {
      await using var fx = await Fixture.CreateAsync(database, Ct);
      using (var call = fx.Call(fx.Admin, admin: true)) {
         await call.Engine.SaveSignerSettingsAsync("Test System", new SystemDefinition().Steps
            .Select(x => new ApprovalSignerSetting(x.Name, false, fx.Decider, [])).ToArray(), Ct);
      }
      async Task<bool> Submit() {
         using var call = fx.Call(fx.Requester);
         try { await call.Engine.RequestAsync("Test System", "concurrent", "Concurrent", Ct); return true; }
         catch (ActionException e) when (e.StatusCode == 409) { return false; }
      }
      var results = await Task.WhenAll(Submit(), Submit());
      Assert.Single(results, x => x);
      using var verify = fx.Call(fx.Requester);
      Assert.Single(await verify.Core.ta_ApprovalRequests.Where(x => x.cSubjectKey == "concurrent").ToArrayAsync(Ct));
   }

   private sealed class Fixture(ServiceProvider provider, ApprovalRegistry registry) : IAsyncDisposable {
      public string Requester { get; } = Ulid.NewUlid().ToString();
      public string Decider { get; } = Ulid.NewUlid().ToString();
      public string Backup { get; } = Ulid.NewUlid().ToString();
      public string Admin { get; } = Ulid.NewUlid().ToString();
      public static async Task<Fixture> CreateAsync(SqlServerDatabase database, CancellationToken ct, IObjectStore? store = null) {
         var coreConnection = await database.CreateExtraDatabaseAsync(ct);
         var moduleConnection = await database.CreateExtraDatabaseAsync(ct);
         var services = new ServiceCollection().AddLogging();
         if (store is not null) services.AddSingleton(store);
         services.AddDbContext<ApiCoreContext>(o => o.UseSqlServer(coreConnection).AddInterceptors(ApprovalDatabaseSwitch.Instance));
         services.AddDbContext<ModuleContext>(o => o.UseSqlServer(moduleConnection).AddInterceptors(ApprovalDatabaseSwitch.Instance));
         services.AddScoped<RootDefinition>(); services.AddScoped<ChildDefinition>(); services.AddScoped<DynamicHandler>();
         var provider = services.BuildServiceProvider();
         var registry = new ApprovalRegistry([new(typeof(RootDefinition), typeof(Root), null), new(typeof(ChildDefinition), typeof(Child), typeof(Root))],
            [new(typeof(SystemDefinition), new SystemDefinition(), new SystemHandlerAdapter<SystemDefinition, string, DynamicHandler>())], [typeof(ModuleContext)], _ => { });
         registry.Initialize(provider);
         var fixture = new Fixture(provider, registry);
         using var scope = provider.CreateScope();
         var core = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
         await core.Database.EnsureCreatedAsync(ct);
         await scope.ServiceProvider.GetRequiredService<ModuleContext>().Database.EnsureCreatedAsync(ct);
         foreach (var id in new[] { fixture.Requester, fixture.Decider, fixture.Backup, fixture.Admin })
            core.ta_Users.Add(new ta_User { cUserId = id, cUserAccount = id, cContactId = id, cUserState = UserState.Active, datestamp = DateTime.Now, ustamp = DateTime.Now });
         await core.SaveChangesAsync(ct);
         return fixture;
      }
      public Call Call(string userId, bool decide = false, bool admin = false) => new(provider.CreateScope(), registry,
         new ActionRequest { cUserId = userId, IsAdmin = admin, Source = CallerSource.AccessToken,
            Claims = decide ? [new ClaimAction { ModuleName = "Test", Name = "Decide" }] : [] });
      public ValueTask DisposeAsync() => provider.DisposeAsync();
   }
   private sealed class Call : IDisposable {
      private readonly IServiceScope _scope;
      public Call(IServiceScope scope, ApprovalRegistry registry, ActionRequest caller) {
         _scope = scope; Core = scope.ServiceProvider.GetRequiredService<ApiCoreContext>(); Module = scope.ServiceProvider.GetRequiredService<ModuleContext>();
         Engine = new ApprovalEngine(Core, registry, scope.ServiceProvider, caller, NullLogger<ApprovalEngine>.Instance);
      }
      public ApiCoreContext Core { get; }
      public ModuleContext Module { get; }
      public ApprovalEngine Engine { get; }
      public void Dispose() => _scope.Dispose();
   }
   public sealed class Root { public string RootId { get; set; } = ""; public string Text { get; set; } = ""; public bool Deleted { get; set; } public DateTime? DeletedAt { get; set; } }
   public sealed class Child { public string ChildId { get; set; } = ""; public string RootId { get; set; } = ""; public string Text { get; set; } = ""; }
   public sealed class ModuleContext(DbContextOptions<ModuleContext> options) : EmDbContext(options) {
      public DbSet<Root> Roots => Set<Root>(); public DbSet<Child> Children => Set<Child>();
      protected override void OnModelCreating(ModelBuilder b) { b.Entity<Root>().HasKey(x => x.RootId); b.Entity<Child>().HasKey(x => x.ChildId); b.Entity<Child>().HasOne<Root>().WithMany().HasForeignKey(x => x.RootId); }
   }
   [Approval("Test Data")]
   public sealed class RootDefinition : IEmApprovalData<Root> {
      public string[] KeyColumns => [nameof(Root.RootId)]; public string[] TrackedColumns => [nameof(Root.Text)];
      public ApprovalDeleteMode DeleteMode => ApprovalDeleteMode.Soft;
      public string DeleteStateColumn => nameof(Root.Deleted); public string DeleteStampColumn => nameof(Root.DeletedAt);
      public bool CanView(ActionRequest caller) => true;
      public bool CanDecide(ActionRequest caller) => caller.Claims.Any(x => x.Name == "Decide");
      public Task CreateKeyAsync(Root row, IEmApprovalDataContext context, CancellationToken ct = default) { row.RootId = Ulid.NewUlid().ToString(); return Task.CompletedTask; }
   }
   public sealed class ChildDefinition : IEmApprovalDataChild<Child, Root> {
      public string[] KeyColumns => [nameof(Child.ChildId)]; public string[] TrackedColumns => [nameof(Child.Text)]; public string[] ForeignKeyColumns => [nameof(Child.RootId)];
      public ApprovalDeleteMode DeleteMode => ApprovalDeleteMode.Hard;
      public Task CreateKeyAsync(Child row, IEmApprovalDataContext context, CancellationToken ct = default) { row.ChildId = Ulid.NewUlid().ToString(); return Task.CompletedTask; }
   }
   [Approval("Test System")]
   public sealed class SystemDefinition : IEmApprovalSystem<string> { public IReadOnlyList<ApprovalSystemStep> Steps => [new(1, "Prepared"), new(2, "Check A"), new(2, "Check B"), new(3, "Finish")]; }
   public sealed class DynamicHandler : IEmApprovalSystemHandler<SystemDefinition, string> {
      public Task<ApprovalDynamicSigner?> ResolveDynamicSignerAsync(ApprovalSystemStep step, string key, ActionRequest requester, CancellationToken ct = default) =>
         Task.FromResult(key == "missing" ? null : new ApprovalDynamicSigner(requester.RequireUserId(), true));
   }
}
