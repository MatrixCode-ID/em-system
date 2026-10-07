using System.Windows;
using Em.Ui.Core.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>
   /// A sample child screen to test the navigation lifecycle: a required payload, refusing to be left,
   /// changing the title, opening a manager from an editor, and opening the PDF viewer from this screen's
   /// window.
   /// </summary>
   public partial class TestChild : UserControl, INavigationBody
   {
      public TestChild() {
         InitializeComponent();
      }

      public TestChildVm Vm => (TestChildVm)DataContext;

      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         if (args.Data is not TestChildPayload payload) {
            args.Cancel = true;
            args.Message = "The test child can only be opened with a TestChildPayload.";
            return Task.CompletedTask;
         }

         Vm.Name = payload.Name;
         Vm.Count(nameof(OnNavigatingIn));
         return Task.CompletedTask;
      }

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         Vm.Count(nameof(OnNavigatingAway));
         if (!Vm.BlockLeaving || string.IsNullOrWhiteSpace(Vm.Draft)) return Task.CompletedTask;

         args.Cancel = true;
         args.Message = $"'{Vm.Name}' still holds a draft, so it refuses to be left.";
         return Task.CompletedTask;
      }

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         Vm.Count(nameof(OnReloadRequested));
         return Task.CompletedTask;
      }

      public Task OnRelease(INavigation sender) {
         Vm.Count(nameof(OnRelease));
         return Task.CompletedTask;
      }
   }

   public class TestChildVm : TestVmBase
   {
      private readonly Dictionary<string, int> _calls = [];

      public TestChildVm() {
         RegisterCommand(nameof(RenameCommand), RenameCommand);
         RegisterCommand(nameof(OpenItemsCommand), OpenItemsCommand);
         RegisterCommand(nameof(ViewPdfCommand), ViewPdfCommand);
         RegisterCommand(nameof(CloseCommand), CloseCommand);
      }

      public string Name {
         get => Get(string.Empty) ?? string.Empty;
         set => Set(value, _ => { NotifyChanged(nameof(Heading)); NotifyChanged(nameof(Subheading)); });
      }

      public string Draft {
         get => Get(string.Empty) ?? string.Empty;
         set => Set(value);
      }

      public bool BlockLeaving {
         get => Get(false);
         set => Set(value);
      }

      public string Heading => $"Test child '{Name}'";

      public string Subheading =>
         "Opened with a payload. The host raises the lifecycle calls below; they are counted so you can see which fire when you go back, forward, reload, or close.";

      public string Status => string.Join("   ", _calls.OrderBy(r => r.Key).Select(r => $"{r.Key}: {r.Value}"));

      public void Count(string call) {
         _calls[call] = _calls.GetValueOrDefault(call) + 1;
         NotifyChanged(nameof(Status));
      }

      public Task RenameCommand() {
         var stamp = DateTime.Now.ToString("HH:mm:ss");
         var renamed = NavigationEntry?.SetTitle($"Test child {Name} @ {stamp}") ?? false;
         Count(renamed ? "Renamed" : "RenameRefused");
         return Task.CompletedTask;
      }

      public async Task OpenItemsCommand() {
         if (NavigationEntry is null) return;
         var opened = await NavigationEntry.NavigateTo("test.items");
         Count(opened ? "ManagerOpened" : "ManagerRefused");
      }

      public async Task ViewPdfCommand() {
         if (NavigationEntry is null) return;
         await NavigationEntry.ViewPdf($"PDF from {Name}", _ => Service.GetMeta_TestPdfSample(3), $"{Name}.pdf");
      }

      public async Task CloseCommand() {
         if (NavigationEntry is null) return;
         var closed = await NavigationEntry.Close();
         if (!closed) Count("CloseRefused");
      }
   }
}
