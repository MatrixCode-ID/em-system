using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Em.Shared;
using Em.Test.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using Microsoft.Win32;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Test.Wpf
{
   /// <summary>Navigasi ke layar anak contoh: judulnya mengandung nama, jadi satu nama satu tab.</summary>
   public sealed class TestChildPayload(string name) : NavigationPayloadBase(null)
   {
      public string Name { get; } = name;

      public override string? Title => $"Test child {Name}";
   }

   /// <summary>
   /// Lab UI module uji: viewer PDF, navigasi (editor, fokus ke judul yang sudah terbuka, payload yang
   /// hilang, navigasi tidak dikenal), tema, dialog, NumericBox, lapisan tunggu, dan drag-drop.
   /// </summary>
   public partial class TestUiLab : UserControl, INavigationBody
   {
      public TestUiLab() {
         InitializeComponent();
      }

      public TestUiLabVm Vm => (TestUiLabVm)DataContext;

      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         Vm.RefreshBranding();
         return Task.CompletedTask;
      }

      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   public class TestUiLabVm : TestVmBase
   {
      public TestUiLabVm() {
         RegisterCommand<string?>(nameof(ViewSampleCommand), ViewSampleCommand);
         RegisterCommand(nameof(ViewLocalCommand), ViewLocalCommand);
         RegisterCommand(nameof(ViewBrokenCommand), ViewBrokenCommand);
         RegisterCommand(nameof(ViewTwiceCommand), ViewTwiceCommand);
         RegisterCommand<string?>(nameof(OpenChildCommand), OpenChildCommand);
         RegisterCommand(nameof(OpenWithoutPayloadCommand), OpenWithoutPayloadCommand);
         RegisterCommand(nameof(OpenUnknownCommand), OpenUnknownCommand);
         RegisterCommand(nameof(ToggleThemeCommand), ToggleThemeCommand);
         RegisterCommand<string?>(nameof(MessageBoxCommand), MessageBoxCommand);
         RegisterCommand(nameof(TextInputCommand), TextInputCommand);
         RegisterCommand(nameof(PasswordInputCommand), PasswordInputCommand);
         RegisterCommand(nameof(PasswordConfirmCommand), PasswordConfirmCommand);
         RegisterCommand(nameof(ExceptionDialogCommand), ExceptionDialogCommand);
         RegisterCommand(nameof(ErrorFromExceptionCommand), ErrorFromExceptionCommand);
         RegisterCommand(nameof(OverlayCommand), OverlayCommand, OverlayCommandAllowed);
         RegisterCommand(nameof(ButtonWaitCommand), ButtonWaitCommand, ButtonWaitCommandAllowed);
         RegisterCommand<object?>(nameof(DropAnyCommand), DropAnyCommand, DropAnyCommandAllowed);
         RegisterCommand<object?>(nameof(DropPdfCommand), DropPdfCommand, DropPdfCommandAllowed);
         RegisterCommand<object?>(nameof(DropChipCommand), DropChipCommand, DropChipCommandAllowed);
      }

      #region Properties

      public ObservableCollection<string> Chips { get; } = ["Apple", "Banana", "Cherry", "Durian"];

      public int NumericValue {
         get => Get(50);
         set => Set(value, v => Log("NumericBox", $"The view model received {v}."));
      }

      public bool ButtonWaiting {
         get => Get(false);
         private set => Set(value);
      }

      public string BoxOneText {
         get => Get("Box one is empty") ?? string.Empty;
         private set => Set(value);
      }

      public string BoxTwoText {
         get => Get("Box two is empty") ?? string.Empty;
         private set => Set(value);
      }

      public string BrandingText {
         get => Get(string.Empty) ?? string.Empty;
         private set => Set(value);
      }

      public void RefreshBranding() {
         if (EmApp is null) return;
         BrandingText =
            $"Application : {EmApp.ApplicationName}\n" +
            $"Branding    : {EmApp.Branding.Title} - {EmApp.Branding.Tagline}\n" +
            $"Layout      : {EmApp.ApplicationLayout}\n" +
            $"Theme       : {EmApp.CurrentTheme}\n" +
            $"Connection  : {EmApp.ActiveConnection?.ProfileName ?? "(none)"}";
      }

      #endregion

      #region PDF viewer

      public async Task ViewSampleCommand(string? pages) {
         if (NavigationEntry is null || !int.TryParse(pages, out var count)) return;

         var title = $"Sample PDF ({count} page{(count == 1 ? "" : "s")})";
         var opened = await NavigationEntry.ViewPdf(title, _ => Service.GetMeta_TestPdfSample(count), $"sample-{count}.pdf");
         Log("View sample PDF", opened ? $"'{title}' was opened." : "The viewer could not be opened.", !opened);
      }

      public async Task ViewLocalCommand() {
         if (NavigationEntry is null) return;

         var dialog = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Choose a PDF" };
         if (dialog.ShowDialog() != true) return;

         var path = dialog.FileName;
         var opened = await NavigationEntry.ViewPdf($"Disk: {Path.GetFileName(path)}",
            _ => Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)),
            Path.GetFileName(path));
         Log("View local PDF", opened ? $"{path} was opened." : "The viewer could not be opened.", !opened);
      }

      public async Task ViewBrokenCommand() {
         if (NavigationEntry is null) return;

         // The loader fails on purpose: the viewer has to show the failure, not crash or stay blank.
         var opened = await NavigationEntry.ViewPdf("Broken PDF",
            _ => Task.FromResult<Stream>(new MemoryStream("This is not a PDF."u8.ToArray())), "broken.pdf");
         Log("View a broken PDF", opened ? "The viewer opened; it should say that the file cannot be read." : "The viewer could not be opened.", !opened);
      }

      public async Task ViewTwiceCommand() {
         if (NavigationEntry is null) return;

         await NavigationEntry.ViewPdf("Same title", _ => Service.GetMeta_TestPdfSample(2), "same.pdf");
         var second = await NavigationEntry.ViewPdf("Same title", _ => Service.GetMeta_TestPdfSample(7), "same.pdf");
         Log("View the same title twice", second
            ? "Only one 'Same title' tab exists: the second request brought the first back (it still shows 2 pages, not 7)."
            : "The second request was refused.", !second);
      }

      #endregion

      #region Navigation

      public async Task OpenChildCommand(string? name) {
         if (NavigationEntry is null || string.IsNullOrEmpty(name)) return;
         var opened = await NavigationEntry.NavigateTo("test.ui.child", new TestChildPayload(name));
         Log($"Open child '{name}'", opened ? "Opened, or an existing tab with that title was focused." : "Refused.", !opened);
      }

      public async Task OpenWithoutPayloadCommand() {
         if (NavigationEntry is null) return;
         var opened = await NavigationEntry.NavigateTo("test.ui.child");
         Log("Open a child without a payload", opened ? "Opened - the child should have refused it." : "Refused, as the child requires its payload.", opened);
      }

      public async Task OpenUnknownCommand() {
         if (NavigationEntry is null) return;
         var opened = await NavigationEntry.NavigateTo("test.does.not.exist");
         Log("Open an unknown navigation", opened ? "Opened?!" : "Refused: no such navigation.", opened);
      }

      public Task ToggleThemeCommand() {
         if (EmApp is null) return Task.CompletedTask;
         EmApp.CurrentTheme = EmApp.CurrentTheme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
         RefreshBranding();
         Log("Theme", $"Switched to {EmApp.CurrentTheme}.");
         return Task.CompletedTask;
      }

      #endregion

      #region Dialogs

      public Task MessageBoxCommand(string? kind) {
         if (DialogOwner is not { } owner) return Task.CompletedTask;

         var result = kind switch {
            "info" => owner.ShowMboxInfo("This is an information box.", "Info"),
            "warning" => owner.ShowMboxWarning("This is a warning box.", "Warning"),
            "error" => owner.ShowMboxError("This is an error box.", "Error"),
            "decide" => owner.ShowMboxDecide("Do you want to continue?", "Decide"),
            "decide-warning" => owner.ShowMboxDecideWarning("This will do something you cannot undo. Continue?", "Decide"),
            "decide-cancel" => owner.ShowMboxDecideCancel("Save the changes before leaving?", "Unsaved changes"),
            _ => MessageBoxResult.None
         };
         Log($"Message box: {kind}", $"You chose {result}.");
         return Task.CompletedTask;
      }

      public Task TextInputCommand() {
         var dialog = new TextInputDialog("Name the thing", "Type a short name. It cannot stay empty.", "For example: Warehouse", "Save") {
            Owner = DialogOwner
         };
         Log("Text input dialog", dialog.ShowDialog() == true ? $"You typed '{dialog.Vm.Result}'." : "Cancelled.");
         return Task.CompletedTask;
      }

      public Task PasswordInputCommand() => ShowPassword(false);

      public Task PasswordConfirmCommand() => ShowPassword(true);

      private Task ShowPassword(bool confirm) {
         var dialog = new PasswordInputDialog("Set a password", "The password is not stored anywhere by this test.", confirm, false, "Set") {
            Owner = DialogOwner
         };
         Log(confirm ? "Password dialog with confirmation" : "Password dialog",
            dialog.ShowDialog() == true ? $"Accepted a password of {dialog.Vm.Password.Length} character(s)." : "Cancelled.");
         return Task.CompletedTask;
      }

      public Task ExceptionDialogCommand() {
         try {
            try {
               throw new InvalidOperationException("The inner failure.");
            }
            catch (Exception) {
               throw new ActionException("The outer failure, raised on purpose.", 500) { Data = { ["Hint"] = "Look at the inner exception below." } };
            }
         }
         catch (Exception x) {
            DiaplayException(x);
         }

         return Task.CompletedTask;
      }

      public Task ErrorFromExceptionCommand() {
         AlertError(new TimeoutException("A deliberate timeout, to see how an error box reads."));
         return Task.CompletedTask;
      }

      #endregion

      #region Waiting

      public async Task OverlayCommand() {
         try {
            WaiterText = "Holding for three seconds...";
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();
            await Task.Delay(TimeSpan.FromSeconds(3));
            Log("Wait overlay", "The overlay covered the screen for three seconds.");
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      public bool OverlayCommandAllowed() => IsNotBusy;

      public async Task ButtonWaitCommand() {
         try {
            ButtonWaiting = true;
            RaiseCommandsChanged();
            await Task.Delay(TimeSpan.FromSeconds(3));
            Log("Button wait", "The button showed dots for three seconds.");
         }
         finally {
            ButtonWaiting = false;
            RaiseCommandsChanged();
         }
      }

      public bool ButtonWaitCommandAllowed() => !ButtonWaiting;

      #endregion

      #region Drag and drop

      // The zone is a drop target for as long as this says yes; it is asked on every drag-over, so it must
      // look at the payload only.
      public bool DropAnyCommandAllowed(object? payload) => payload is DroppedFiles;

      public Task DropAnyCommand(object? payload) {
         if (payload is not DroppedFiles files) return Task.CompletedTask;

         var lines = files.Paths.Select(p => File.Exists(p) ? $"{Path.GetFileName(p)}  ({new FileInfo(p).Length:N0} B)" : $"{Path.GetFileName(p)}  (folder)");
         Log("Drop: any files", $"{files.Paths.Count} item(s):\n{string.Join("\n", lines)}");
         return Task.CompletedTask;
      }

      public bool DropPdfCommandAllowed(object? payload) =>
         payload is DroppedFiles files && files.Paths.Count > 0 &&
         files.Paths.All(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

      public async Task DropPdfCommand(object? payload) {
         if (payload is not DroppedFiles files || NavigationEntry is null) return;

         var path = files.Paths[0];
         var opened = await NavigationEntry.ViewPdf($"Dropped: {Path.GetFileName(path)}",
            _ => Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)),
            Path.GetFileName(path));
         Log("Drop: PDF files", $"{files.Paths.Count} PDF(s) dropped; the first {(opened ? "was opened" : "could not be opened")}.", !opened);
      }

      public bool DropChipCommandAllowed(object? payload) => payload is DropRequest { Payload: string };

      public Task DropChipCommand(object? payload) {
         if (payload is not DropRequest { Payload: string chip, Target: string box }) return Task.CompletedTask;

         if (box == "Box one") BoxOneText = $"Box one holds {chip}";
         else BoxTwoText = $"Box two holds {chip}";
         Log("Drop: chip", $"'{chip}' was dropped on '{box}'.");
         return Task.CompletedTask;
      }

      #endregion
   }
}
