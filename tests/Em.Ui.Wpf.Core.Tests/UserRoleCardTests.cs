using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;

namespace Em.Ui.Wpf.Core.Tests
{
   public sealed class UserRoleCardTests
   {
      private static readonly DateTime Now = new(2026, 10, 7, 10, 0, 0);

      private sealed class StubApp : IEmApp, IServiceProvider
      {
         public IServiceProvider ServiceProvider => this;
         public object? GetService(Type serviceType) => null;
         public Task<DateTime> GetDateStampAsync() => Task.FromResult(Now);
      }

      private static Role NewRole(string id, RoleState state = RoleState.Active) =>
         Role.Build(new StubApp(), new vi_Role { cRoleId = id, cRoleName = "Role " + id, cRoleState = state });

      private static ta_UserRole Row(string roleId, DateTime? start, DateTime? expiry) => new() {
         cUserId = "U1", cRoleId = roleId, cUserRoleStart = start, cUserRoleExpiry = expiry,
         ustamp = new DateTime(2026, 1, 1), datestamp = new DateTime(2025, 12, 31)
      };

      [Fact]
      public void StartsAtBaselineWithoutChanges() {
         var card = new UserRoleCardVm(NewRole("R1"), Row("R1", null, null), Now);

         Assert.True(card.IsAssigned);
         Assert.False(card.IsChanged);
         Assert.False(card.HasPeriodError);
      }

      [Fact]
      public void ComparesPeriodPerDayAndKeepsStoredTimes() {
         var start = new DateTime(2026, 9, 1, 8, 30, 0);
         var card = new UserRoleCardVm(NewRole("R1"), Row("R1", start, null), Now);

         // A date picker hands the same day back at midnight: that is not a change.
         card.StartDate = start.Date;
         Assert.False(card.IsChanged);
         Assert.Equal(start, card.EffectiveStart);

         card.StartDate = new DateTime(2026, 9, 2);
         Assert.True(card.IsChanged);
         Assert.Equal(new DateTime(2026, 9, 2), card.EffectiveStart);
      }

      [Fact]
      public void ConvertsPickedDatesToStoredBoundaries() {
         Assert.Equal(new DateTime(2026, 10, 7), UserRoleCardVm.ToStart(new DateTime(2026, 10, 7, 15, 0, 0)));
         Assert.Equal(new DateTime(2026, 10, 7, 23, 59, 59), UserRoleCardVm.ToExpiry(new DateTime(2026, 10, 7)));
         Assert.Null(UserRoleCardVm.ToStart(null));
         Assert.Null(UserRoleCardVm.ToExpiry(null));
         Assert.True(UserRoleCardVm.SameDate(null, null));
         Assert.False(UserRoleCardVm.SameDate(null, Now));
      }

      [Fact]
      public void FlagsExpiryBeforeStart() {
         var card = new UserRoleCardVm(NewRole("R1"), null, Now) { IsAssigned = true };
         card.StartDate = new DateTime(2026, 10, 10);
         card.ExpiryDate = new DateTime(2026, 10, 9);
         Assert.True(card.HasPeriodError);

         card.ExpiryDate = new DateTime(2026, 10, 10);
         Assert.False(card.HasPeriodError);
      }

      [Fact]
      public void RevertAndAcceptMoveTheBaseline() {
         var card = new UserRoleCardVm(NewRole("R1"), null, Now);
         var raised = 0;
         card.Changed += (_, _) => raised++;

         card.IsAssigned = true;
         Assert.Equal(1, raised);
         Assert.True(card.IsChanged);

         card.Revert();
         Assert.False(card.IsAssigned);
         Assert.False(card.IsChanged);
         Assert.Equal(1, raised);

         card.IsAssigned = true;
         card.ExpiryDate = new DateTime(2026, 12, 31);
         var sent = Row("R1", null, new DateTime(2026, 12, 31, 23, 59, 59));
         card.Accept(sent, Now);

         Assert.False(card.IsChanged);
         Assert.True(card.OriginalIsAssigned);
         Assert.Equal(new DateTime(2026, 12, 31, 23, 59, 59), card.OriginalExpiry);
         Assert.Same(sent, card.OriginalRow);
      }

      [Fact]
      public void ShowsScheduledAndExpiredStates() {
         var scheduled = new UserRoleCardVm(NewRole("R1"), Row("R1", Now.AddDays(3), null), Now);
         var expired = new UserRoleCardVm(NewRole("R2"), Row("R2", null, Now.AddDays(-1)), Now);
         var inactive = new UserRoleCardVm(NewRole("R3", RoleState.Disabled), null, Now);

         Assert.Equal("SCHEDULED", scheduled.PeriodStateCaption);
         Assert.Equal("EXPIRED", expired.PeriodStateCaption);
         Assert.Equal(string.Empty, inactive.PeriodStateCaption);
         Assert.True(inactive.IsRoleInactive);
      }

      [Fact]
      public void BuildsRemovedAddedAndRescheduledRows() {
         var stamp = new DateTime(2026, 10, 7, 12, 0, 0);
         var kept = new UserRoleCardVm(NewRole("KEEP"), Row("KEEP", null, null), Now);
         var removed = new UserRoleCardVm(NewRole("DEL"), Row("DEL", null, null), Now) { IsAssigned = false };
         var added = new UserRoleCardVm(NewRole("ADD"), null, Now) { IsAssigned = true };
         added.StartDate = new DateTime(2026, 11, 1);
         var moved = new UserRoleCardVm(NewRole("MOVE"), Row("MOVE", null, null), Now);
         moved.ExpiryDate = new DateTime(2027, 1, 31);
         var untouched = new UserRoleCardVm(NewRole("NONE"), null, Now);

         var changes = UserRoleCardVm.BuildChanges("NEWUSER", [kept, removed, added, moved, untouched], stamp);

         var r = Assert.Single(changes.Removed);
         Assert.Equal(("NEWUSER", "DEL"), (r.cUserId, r.cRoleId));

         var a = Assert.Single(changes.Added);
         Assert.Equal(("NEWUSER", "ADD"), (a.cUserId, a.cRoleId));
         Assert.Equal(new DateTime(2026, 11, 1), a.cUserRoleStart);
         Assert.Null(a.cUserRoleExpiry);
         Assert.Equal(stamp, a.ustamp);
         Assert.Equal(stamp, a.datestamp);

         var m = Assert.Single(changes.Rescheduled);
         Assert.Equal("MOVE", m.cRoleId);
         Assert.Equal(new DateTime(2027, 1, 31, 23, 59, 59), m.cUserRoleExpiry);
         Assert.Equal(stamp, m.ustamp);
         // A rescheduled assignment keeps the moment it was first given.
         Assert.Equal(new DateTime(2025, 12, 31), m.datestamp);

         Assert.Same(a, changes.SentRowFor("ADD"));
         Assert.Same(m, changes.SentRowFor("MOVE"));
         Assert.Null(changes.SentRowFor("KEEP"));
         Assert.False(changes.IsEmpty);
         Assert.True(UserRoleCardVm.BuildChanges("U", [kept, untouched], stamp).IsEmpty);
      }
   }
}
