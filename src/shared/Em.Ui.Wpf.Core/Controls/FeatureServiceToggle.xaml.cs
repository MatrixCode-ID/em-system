using System.Windows;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Controls;

/// <summary>Shared runtime service switch and status display used by system managers.</summary>
public partial class FeatureServiceToggle : UserControl {
   private readonly FeatureServiceToggleVm _vm = new();
   /// <summary>Creates the status control.</summary>
   public FeatureServiceToggle() {
      InitializeComponent(); DataContext = _vm;
      _vm.Saved += () => StateChanged?.Invoke(this, EventArgs.Empty);
      Loaded += async (_, _) => { if (_vm.EmApp is not null) await _vm.RefreshCommand(); };
   }
   /// <summary>Raised after a service switch is saved.</summary>
   public event EventHandler? StateChanged;
   /// <summary>Connects this control to its host feature.</summary>
   public void Attach(EmApp app, string feature) { _vm.EmApp = app; _vm.Feature = feature; }
}

/// <summary>View model of the common administrator-only switch.</summary>
public sealed class FeatureServiceToggleVm : MvvmModelBase {
   /// <summary>Creates and registers the switch commands.</summary>
   public FeatureServiceToggleVm() {
      RegisterCommand(nameof(ToggleCommand), ToggleCommand, ToggleCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
   }
   internal event Action? Saved;
   internal string Feature { get; set; } = "";
   private IFeatureServiceServices Service => EmApp!.ServiceProvider.GetRequiredService<IFeatureServiceServices>();
   /// <summary>Cached server status.</summary>
   public bool Enabled { get => Get<bool>(); set { Set(value); NotifyChanged(nameof(StatusCaption)); } }
   /// <summary>Whether the server permits the active caller to switch the service.</summary>
   public bool CanManage { get => Get<bool>(); set { Set(value); RaiseCommandsChanged(); } }
   /// <summary>Whether status has been read.</summary>
   public bool IsLoaded { get => Get<bool>(); set { Set(value); NotifyChanged(nameof(StatusCaption)); RaiseCommandsChanged(); } }
   /// <summary>Whether a server request is running.</summary>
   public bool IsUpdating { get => Get<bool>(); set { Set(value); RaiseCommandsChanged(); } }
   /// <summary>Explicit service status, independent of the toggle position.</summary>
   public string StatusCaption => !IsLoaded ? "Service: unavailable" : Enabled ? "Service enabled" : "Service disabled";
   /// <summary>Whether the toggle can execute.</summary>
   public bool ToggleCommandAllowed() => IsLoaded && CanManage && !IsUpdating;
   /// <summary>Whether status can be refreshed.</summary>
   public bool RefreshCommandAllowed() => EmApp is not null && !IsUpdating;
   /// <summary>Toggles the persisted status.</summary>
   public Task ToggleCommand() => UpdateAsync(!Enabled);
   /// <summary>Reads current status.</summary>
   public Task RefreshCommand() => UpdateAsync(null);
   private Task<FeatureServiceState> ReadAsync() => Feature switch {
      "Cdn" => Service.GetMeta_CdnServiceState(), "Ctn" => Service.GetMeta_CtnServiceState(),
      "NuPak" => Service.GetMeta_NuPakServiceState(), "ObjectStorage" => Service.GetMeta_ObjectStorageServiceState(),
      _ => throw new InvalidOperationException("Unknown service feature.")
   };
   private Task<FeatureServiceState> SaveAsync(bool enabled) => Feature switch {
      "Cdn" => Service.PostMeta_CdnServiceState(enabled), "Ctn" => Service.PostMeta_CtnServiceState(enabled),
      "NuPak" => Service.PostMeta_NuPakServiceState(enabled), "ObjectStorage" => Service.PostMeta_ObjectStorageServiceState(enabled),
      _ => throw new InvalidOperationException("Unknown service feature.")
   };
   private async Task UpdateAsync(bool? enabled) {
      if (IsUpdating) return;
      IsUpdating = true;
      try {
         var state = enabled is { } value ? await SaveAsync(value) : await ReadAsync();
         await Application.Current.Dispatcher.InvokeAsync(() => { Enabled = state.Enabled; CanManage = state.CanManage; IsLoaded = true; if (enabled is not null) Saved?.Invoke(); });
      }
      catch (Exception ex) {
         await Application.Current.Dispatcher.InvokeAsync(() => { IsLoaded = false; CanManage = false; AlertError(ex); });
      }
      finally { await Application.Current.Dispatcher.InvokeAsync(() => { IsUpdating = false; NotifyChanged(nameof(Enabled)); }); }
   }
   private void RaiseCommandsChanged() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
}
