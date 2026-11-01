using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Core.Tests
{
   public class MenuOrderTests
   {
      [Fact]
      public void OrdersByIndexThenTitle() {
         var items = new[] { Nav("Delta", 2), Nav("charlie", 1), Nav("Bravo", 1), Nav("Alpha", 3) };

         var titles = items.InMenuOrder().Select(r => r.Title).ToArray();

         Assert.Equal(["Bravo", "charlie", "Delta", "Alpha"], titles);
      }

      [Fact]
      public void InsertsGroupsAlphabetically() {
         var level = new List<string>();
         foreach (var caption in new[] { "830-SOE", "120-SHW", "810-SCO", "650-sys" })
            level.Insert(MenuOrder.GroupInsertIndex(level, caption, r => r), caption);

         Assert.Equal(["120-SHW", "650-sys", "810-SCO", "830-SOE"], level);
      }

      [Fact]
      public void SkipsEntriesThatAreNotGroups() {
         // Items (null caption) keep their place; only groups are compared.
         var level = new List<string?> { "item", "B" };
         level.Insert(MenuOrder.GroupInsertIndex(level, "A", r => r == "item" ? null : r), "A");

         Assert.Equal(["item", "A", "B"], level);
      }

      private static FakeNavigation Nav(string title, int order) => new() { Title = title, Order = order };

      private sealed class FakeNavigation : INavigation
      {
         public int Order { get; init; }
         public string Name => Title;
         public string Title { get; set; } = "";
         public string Subtitle { get; set; } = "";
         public string Description { get; set; } = "";
         public MenuPath? MenuPath { get; set; }
         public INavigationHost NavigationHost => throw new NotSupportedException();
         public IBodyType BodyType => throw new NotSupportedException();
         public bool RequireParameter => false;
         public int OrderIndex => Order;
         public NavigationKind Kind => NavigationKind.Manager;
         public string? ModuleName => null;
         public ClaimAction? RequiredClaim => null;
      }
   }
}
