using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Core.Tests
{
   public class NavigationAccessTests
   {
      private static readonly ClaimAction RunClaim = new() { ModuleName = "test", Name = "Run Tests" };

      private static readonly ClaimAction[] Catalog = [
         new() { ModuleName = "Administrative Tools", Name = "User Manager Access" },
         RunClaim
      ];

      [Fact]
      public void IsDeclared_NavigationWithoutModule_IsAlwaysDeclared() {
         Assert.True(NavigationAccess.IsDeclared(new FakeNavigation(null, null), []));
      }

      [Fact]
      public void IsDeclared_RequiredClaimInCatalog_IsDeclared() {
         Assert.True(NavigationAccess.IsDeclared(new FakeNavigation("test", RunClaim with { Name = "run tests" }), Catalog));
      }

      [Fact]
      public void IsDeclared_RequiredClaimMissing_IsNotDeclared() {
         var other = new ClaimAction { ModuleName = "test", Name = "Probe" };
         Assert.False(NavigationAccess.IsDeclared(new FakeNavigation("test", other), Catalog));
      }

      [Theory]
      [InlineData("TEST", true)]
      [InlineData("shop", false)]
      public void IsDeclared_WithoutRequiredClaim_ChecksModule(string module, bool expected) {
         Assert.Equal(expected, NavigationAccess.IsDeclared(new FakeNavigation(module, null), Catalog));
      }

      // A module switched off on the server leaves no claim in the catalog, so its menu must go away.
      [Fact]
      public void IsDeclared_ModuleSwitchedOffOnServer_IsNotDeclared() {
         Assert.False(NavigationAccess.IsDeclared(new FakeNavigation("test", RunClaim), Catalog[..1]));
      }

      private sealed class FakeNavigation(string? moduleName, ClaimAction? requiredClaim) : INavigation
      {
         public string Name => "fake";
         public string Title { get; set; } = "";
         public string Subtitle { get; set; } = "";
         public string Description { get; set; } = "";
         public MenuPath? MenuPath { get; set; }
         public INavigationHost NavigationHost => throw new NotSupportedException();
         public IBodyType BodyType => throw new NotSupportedException();
         public bool RequireParameter => false;
         public int OrderIndex => 0;
         public NavigationKind Kind => NavigationKind.Manager;
         public string? ModuleName => moduleName;
         public ClaimAction? RequiredClaim => requiredClaim;
      }
   }
}
