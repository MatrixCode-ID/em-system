using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Dialogs;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Core.Tests;

public sealed class ReleaseSettingsDialogTests : IDisposable {
   private readonly string _root = Path.Combine(Path.GetTempPath(), "ReleaseSettingsTests-" + Guid.NewGuid().ToString("N"));
   private readonly string _registry = @"Software\Em.Tests\ReleaseSettings-" + Guid.NewGuid().ToString("N");
   public ReleaseSettingsDialogTests() => Directory.CreateDirectory(_root);
   public void Dispose() { Directory.Delete(_root, true); Registry.CurrentUser.DeleteSubKeyTree(_registry, false); }
   private ReleaseManagerPreferences Preferences() => new(() => Registry.CurrentUser.CreateSubKey(_registry));
   private static EmApp App() => (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
   private ReleaseProfile Profile() => new() { Name = "Mobile draft", Platform = ReleasePlatform.Android,
      TargetKind = ReleaseTargetKind.Folder, TargetFolder = Path.Combine(_root, "release"), PublishFolder = Path.Combine(_root, "publish"),
      SolutionPath = Path.Combine(_root, "App.slnx"), HostProject = "Mobile.csproj",
      Product = new() { AppName = "Inventory", Publisher = "Contoso", AppId = "Contoso.Inventory.Mobile" } };

   [WpfFact]
   public void FormLoadsOrderedSectionsAndOnlyShowsPlatformFields() {
      var view = new ReleaseSettingsDialog();
      var profile = Profile(); var store = new ReleaseProfileStore(_root);
      view.Vm.Configure(App(), store, profile, default, [], Preferences(), false);
      var form = Assert.IsType<StackPanel>(view.FindName("profileForm"));
      Assert.Equal(new[] { "sourceSection", "identitySection", "buildSection", "destinationSection", "signingSection" },
         form.Children.Cast<FrameworkElement>().Select(child => child.Name));
      Assert.True(view.Vm.IsAndroid); Assert.False(view.Vm.IsWindows);
      Assert.Equal("Mobile.csproj", view.Vm.SelectedHost);
      var android = Assert.IsType<ReleaseAndroidSettings>(view.FindName("androidEnvironment"));
      Assert.Same(view.Vm.Android, android.DataContext);
      Assert.Equal(ReleaseAndroidSection.Environment, android.Section);
      Assert.Equal(Visibility.Visible, android.Visibility);
      var save = Assert.IsType<Button>(view.FindName("saveButton"));
      Assert.Same(view.Vm.Commands[nameof(view.Vm.SaveCommand)], save.Command);
      view.Vm.Android.SelectedPlatform = ReleasePlatform.Windows;
      Assert.False(view.Vm.SaveCommandAllowed());
      view.Close();
   }

   [WpfFact]
   public async Task SdkDiscoveryPublishesSnapshotsOnUiThreadAndEditorsKeepTypedPaths() {
      var view = new ReleaseAndroidSettings { Section = ReleaseAndroidSection.Environment };
      view.Vm.Profile.Platform = ReleasePlatform.Android; view.Vm.Refresh();
      var thread = Environment.CurrentManagedThreadId; var notifications = 0;
      view.Vm.PropertyChanged += (_, args) => {
         if (args.PropertyName is nameof(view.Vm.SdkCandidates) or nameof(view.Vm.JavaCandidates)) {
            Assert.Equal(thread, Environment.CurrentManagedThreadId); notifications++;
         }
      };
      var discovery = view.Vm.RefreshEnvironmentCommand();
      Assert.True(view.Vm.Busy); Assert.False(view.Vm.AndroidAllowed());
      Assert.Contains("SDK", view.Vm.BusyText);
      await discovery;
      Assert.False(view.Vm.Busy); Assert.Equal(2, notifications);
      Assert.Same(view.Vm.JavaCandidates, view.Vm.JavaCandidates);
      foreach (var name in new[] { "sdkCombo", "javaCombo" }) {
         var combo = Assert.IsType<ComboBox>(view.FindName(name));
         combo.ApplyTemplate();
         var editor = Assert.IsType<TextBox>(combo.Template.FindName("PART_EditableTextBox", combo));
         combo.SetCurrentValue(ItemsControl.ItemsSourceProperty, new[] { "sdk-one", "sdk-two" });
         combo.SelectedIndex = 1;
         await Dispatcher.Yield(DispatcherPriority.DataBind);
         Assert.Equal("sdk-two", editor.Text);
         combo.SetCurrentValue(ComboBox.TextProperty, "custom-sdk-path");
         await Dispatcher.Yield(DispatcherPriority.DataBind);
         Assert.Equal("custom-sdk-path", editor.Text);
      }
      Assert.Equal("custom-sdk-path", view.Vm.Sdk); Assert.Equal("custom-sdk-path", view.Vm.Java);
   }

   [WpfFact]
   public async Task ChangingPlatformLoadsCompatibleHostAndItsApplicationId() {
      var project = Path.Combine(_root, "Mobile.csproj");
      File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-android</TargetFramework><ApplicationTitle>Inventory</ApplicationTitle><Company>Contoso</Company><EmAppId>Contoso.Inventory.Mobile</EmAppId><ApplicationId>com.contoso.inventory</ApplicationId><Version>1.2.0</Version></PropertyGroup></Project>");
      File.WriteAllText(Path.Combine(_root, "App.slnx"), "<Solution><Project Path=\"Mobile.csproj\"/></Solution>");
      var profile = Profile(); profile.Platform = ReleasePlatform.Windows; profile.Product.AppId = "";
      var vm = new ReleaseSettingsDialogVm(); vm.Configure(App(), new ReleaseProfileStore(_root), profile, default, [], Preferences(), false);
      vm.Draft.ChangePlatform(ReleasePlatform.Android); vm.Android.SelectedPlatform = ReleasePlatform.Android;
      await vm.Android.PlatformChanged!();
      Assert.Equal("Mobile.csproj", vm.SelectedHost); Assert.Single(vm.Hosts);
      Assert.Equal("com.contoso.inventory", vm.Draft.Android.ApplicationId);
      Assert.Equal("Contoso.Inventory.Mobile", vm.AppId); Assert.True(vm.IsAndroid);
      Assert.Equal("Mobile.csproj", vm.Draft.HostProject);
      vm.Draft.Android.ApplicationId = "com.contoso.permanent";
      await vm.CheckSolutionCommand();
      Assert.Equal("com.contoso.permanent", vm.Draft.Android.ApplicationId);
   }

   [WpfFact]
   public async Task SavingKeepsDraftIsolationPreferencesAndHostSelection() {
      var profile = Profile(); var store = new ReleaseProfileStore(Path.Combine(_root, "profiles"));
      var saved = store.Save(profile); var prefs = Preferences();
      var vm = new ReleaseSettingsDialogVm(); vm.Configure(App(), store, profile, saved.LastWriteUtc, [], prefs, false);
      vm.Android.Sdk = "sdk-choice"; vm.Android.Java = "java-choice";
      vm.AppName = "Updated inventory"; vm.Draft.Android.ApplicationId = "com.contoso.inventory";
      Assert.Equal("Inventory", profile.Product.AppName); Assert.Empty(profile.Android.ApplicationId);
      var closed = false; vm.RequestClose += result => closed = result;
      await vm.SaveCommand();
      Assert.True(closed); Assert.Empty(vm.Error);
      var reloaded = store.Load(profile.Id);
      Assert.Equal("Updated inventory", reloaded.Product.AppName); Assert.Equal("Mobile.csproj", reloaded.HostProject);
      Assert.Equal("com.contoso.inventory", reloaded.Android.ApplicationId);
      Assert.Equal("sdk-choice", prefs.AndroidSdkDirectory); Assert.Equal("java-choice", prefs.JavaSdkDirectory);
   }

   [WpfFact]
   public async Task InvalidSourceReportsAnErrorAndRestoresEditing() {
      var vm = new ReleaseSettingsDialogVm(); var profile = Profile();
      vm.Configure(App(), new ReleaseProfileStore(_root), profile, default, [], Preferences(), false);
      await vm.CheckSolutionCommand();
      Assert.True(vm.HasError); Assert.NotEmpty(vm.Error); Assert.False(vm.IsBusy); Assert.True(vm.CanEdit);
      Assert.Equal("Mobile.csproj", vm.SelectedHost);
      Assert.Equal(ReleasePlatform.Android, profile.Platform);
   }
}
