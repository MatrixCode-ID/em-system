using Em.Api.Core.Approval;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.IntegrationTests;

/// <summary>Approval on MySQL with the core and the module in two databases of one server.</summary>
public sealed class MySqlApprovalTests(MySqlEngineDatabase database) : IClassFixture<MySqlEngineDatabase>
{
   private static CancellationToken Ct => TestContext.Current.CancellationToken;

   private Task<ApprovalRedesignTests.Fixture> FixtureAsync() => ApprovalRedesignTests.Fixture.CreateOnMySqlAsync(database, Ct);

   [Fact]
   public async Task TransactionCommitsBothDatabasesTogether() {
      await using var fx = await FixtureAsync();
      using var call = fx.Call(fx.Decider, decide: true);
      await using (var tx = await ApprovalTransaction.BeginAsync(call.Core, [typeof(ApprovalRedesignTests.ModuleContext)], call.Services, Ct)) {
         call.Core.ta_Metas.Add(new ta_Meta { cMetaKey = "tx.commit", cMetaValue = "core", ustamp = DateTime.UtcNow });
         call.Module.Roots.Add(new ApprovalRedesignTests.Root { RootId = "commit", Text = "module" });
         await call.Core.SaveChangesAsync(Ct);
         await call.Module.SaveChangesAsync(Ct);
         await tx.CommitAsync(Ct);
      }
      using var check = fx.Call(fx.Decider);
      Assert.True(await check.Core.ta_Metas.AnyAsync(x => x.cMetaKey == "tx.commit", Ct));
      Assert.True(await check.Module.Roots.AnyAsync(x => x.RootId == "commit", Ct));
   }

   [Fact]
   public async Task TransactionRollsBackBothDatabasesTogether() {
      await using var fx = await FixtureAsync();
      using var call = fx.Call(fx.Decider, decide: true);
      await using (var tx = await ApprovalTransaction.BeginAsync(call.Core, [typeof(ApprovalRedesignTests.ModuleContext)], call.Services, Ct)) {
         call.Core.ta_Metas.Add(new ta_Meta { cMetaKey = "tx.rollback", cMetaValue = "core", ustamp = DateTime.UtcNow });
         call.Module.Roots.Add(new ApprovalRedesignTests.Root { RootId = "rollback", Text = "module" });
         await call.Core.SaveChangesAsync(Ct);
         await call.Module.SaveChangesAsync(Ct);
         await tx.RollbackAsync(Ct);
      }
      using var check = fx.Call(fx.Decider);
      Assert.False(await check.Core.ta_Metas.AnyAsync(x => x.cMetaKey == "tx.rollback", Ct));
      Assert.False(await check.Module.Roots.AnyAsync(x => x.RootId == "rollback", Ct));
   }

   [Fact]
   public async Task EachContextStaysInItsOwnDatabaseOnTheSharedConnection() {
      await using var fx = await FixtureAsync();
      using var call = fx.Call(fx.Decider, decide: true);
      await using var tx = await ApprovalTransaction.BeginAsync(call.Core, [typeof(ApprovalRedesignTests.ModuleContext)], call.Services, Ct);
      Assert.Same(call.Core.Database.GetDbConnection(), call.Module.Database.GetDbConnection());
      await call.Module.Roots.CountAsync(Ct);
      await call.Core.ta_Metas.CountAsync(Ct);
      await call.Module.Roots.CountAsync(Ct);
   }

   [Fact]
   public async Task DataRequestAppliesToTheModuleWhenApproved() {
      await using var fx = await FixtureAsync();
      string request;
      using (var call = fx.Call(fx.Requester))
         request = (await call.Engine.SubmitDataAsync(DataChange.Create(new ApprovalRedesignTests.Root { Text = "proposal" }), ct: Ct)).ApprovalRequestId;
      using (var call = fx.Call(fx.Requester)) Assert.False(await call.Module.Roots.AnyAsync(Ct));

      using (var call = fx.Call(fx.Decider, decide: true)) {
         var result = await call.Engine.DataDecideAllAsync(new("Test Data", [request], ApprovalDataBatchAction.Approve, null), Ct);
         Assert.True(Assert.Single(result.Items).Success);
      }
      using var check = fx.Call(fx.Decider);
      Assert.Equal("proposal", (await check.Module.Roots.SingleAsync(Ct)).Text);
      Assert.Equal(ApprovalStage.Approved, (await check.Core.ta_ApprovalRequests.SingleAsync(x => x.cApprovalRequestId == request, Ct)).cApprovalRequestStage);
   }

   [Fact]
   public async Task DuplicateActiveSystemRequestAnswers409() {
      await using var fx = await FixtureAsync();
      using var call = fx.Call(fx.Admin, admin: true);
      await call.Engine.SaveSignerSettingsAsync("Test System", [new("Prepared", true, null, []),
         new("Check A", false, fx.Decider, [fx.Backup]), new("Check B", false, fx.Decider, []), new("Finish", false, fx.Decider, [])], Ct);
      using var requester = fx.Call(fx.Requester);
      await requester.Engine.RequestAsync("Test System", "subject", "Subject summary", Ct);
      using var again = fx.Call(fx.Requester);
      var ex = await Assert.ThrowsAsync<ActionException>(() => again.Engine.RequestAsync("Test System", "subject", "Duplicate request", Ct));
      Assert.Equal(409, ex.StatusCode);
   }
}
