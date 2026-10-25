using Em.Shared;
using Em.Ui.Maui.Shared;
using Em.Ui.Core.Release.Update;
using Microsoft.Maui.Controls;
using Application = Microsoft.Maui.Controls.Application;

namespace Em.Ui.Maui.Update;

public sealed class ReleaseUpdatePage : ContentPage {
   public ReleaseUpdatePage(ReleaseUpdatePageVm vm) {
      BindingContext = vm;
      Title = vm.Mandatory ? "Update required" : "Update available";
      var title = new Label { Text = Title, FontSize = 24 };
      var version = new Label { Text = vm.VersionText };
      var status = new Label(); status.SetBinding(Label.TextProperty, nameof(vm.Status));
      var progress = new ProgressBar(); progress.SetBinding(ProgressBar.ProgressProperty, nameof(vm.Progress));
      Button Action(string text, string command) { var button = new Button { Text = text }; button.SetBinding(Button.CommandProperty, "Commands[" + command + "]"); return button; }
      var actions = new VerticalStackLayout { Spacing = 12, Padding = 24, Children = { title, version, status, progress,
         Action(vm.Mandatory ? "Update" : "Update now", nameof(vm.UpdateCommand)), Action("Cancel download", nameof(vm.CancelCommand)) } };
      if (!vm.Mandatory) actions.Add(Action("Later", nameof(vm.LaterCommand)));
      actions.Add(Action("Exit", nameof(vm.ExitCommand)));
      Content = new ScrollView { Content = actions };
      foreach (var label in actions.Children.OfType<Label>()) label.SetDynamicResource(StyleProperty, "labelTextStyle");
      foreach (var button in actions.Children.OfType<Button>()) button.SetDynamicResource(StyleProperty, button.Text is "Update" or "Update now" ? "filledButtonStyle" : "tonalButtonStyle");
   }
   protected override bool OnBackButtonPressed() => true;
}

public sealed class ReleaseUpdatePageVm : MvvmModelBase {
   public ReleaseUpdatePageVm() {
      RegisterCommand(nameof(UpdateCommand), UpdateCommand, UpdateCommandAllowed);
      RegisterCommand(nameof(CancelCommand), CancelCommand, CancelCommandAllowed);
      RegisterCommand(nameof(LaterCommand), LaterCommand, LaterCommandAllowed);
      RegisterCommand(nameof(ExitCommand), ExitCommand);
   }
   public bool Mandatory { get; init; }
   public string VersionText { get; init; } = "";
   public ReleaseUpdateSource Source { get; init; } = null!;
   public ReleaseUpdateResult Result { get; init; } = null!;
   public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
   public string Status { get => Get<string>(""); set => Set(value); }
   public double Progress { get => Get<double>(); set => Set(value); }
   public bool Busy { get => Get<bool>(); set { Set(value); foreach (var command in Commands) command.RaiseCanExecuteChanged(); } }
   private CancellationTokenSource? _cancel;
   private bool _downloading;
   public bool UpdateCommandAllowed() => !Busy;
   public bool CancelCommandAllowed() => Busy && _downloading && _cancel is { IsCancellationRequested: false };
   public bool LaterCommandAllowed() => !Mandatory && !Busy;
   public void CancelCommand() => _cancel?.Cancel();
   public void LaterCommand() => Completion.TrySetResult(true);
   public void ExitCommand() { Application.Current?.Quit(); Completion.TrySetResult(false); }
   public async Task UpdateCommand() {
      using var cancel = new CancellationTokenSource(); _cancel = cancel; _downloading = true; Busy = true;
      var folder = Path.Combine(FileSystem.CacheDirectory, "updates"); Directory.CreateDirectory(folder);
      var apk = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".apk");
      try {
         Status = "Downloading update…";
         using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
         await new ReleaseUpdateChecker(client).DownloadAsync(Source, Result.Manifest!, apk, new Progress<double>(value => Progress = value), cancel.Token);
         Status = "Confirm installation in Android…";
         _downloading = false; Commands[nameof(CancelCommand)]?.RaiseCanExecuteChanged();
#if ANDROID
         await AndroidUpdateInstaller.InstallAsync(apk, cancel.Token);
#endif
         Status = "Update installed. Reopen the application.";
      } catch (OperationCanceledException) { Status = "Download cancelled. You can retry."; }
      catch (Exception ex) { Status = ex.Message; }
      finally { if (File.Exists(apk)) File.Delete(apk); _cancel = null; _downloading = false; Busy = false; }
   }
}
