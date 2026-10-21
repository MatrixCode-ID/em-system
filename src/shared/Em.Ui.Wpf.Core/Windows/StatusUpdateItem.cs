using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using FontAwesome6;

namespace Em.Ui.Wpf.Windows;

/// <summary>Host-configurable launcher polling schedule.</summary>
public sealed class LauncherUpdateOptions {
   /// <summary>Delay before the first automatic check; default five minutes.</summary>
   public TimeSpan FirstCheckDelay { get; set; } = TimeSpan.FromMinutes(5);
   /// <summary>Period between checks; default sixty minutes.</summary>
   public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(60);
   /// <summary>Random interval variation on either side; default ten minutes.</summary>
   public TimeSpan Jitter { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Checks and prepares updates through the installed launcher only.</summary>
public sealed class StatusUpdateItem : NotifyPropertyBase, IDisposable {
   private readonly DispatcherTimer _timer = new();
   private readonly CancellationTokenSource _cancel = new();
   private LauncherUpdateOptions _options = new();
   private Window? _window;
   private bool _checking;
   private bool _ready;
   private bool _restart;
   private string _failure = "";
   /// <summary>Creates commands without starting a check.</summary>
   public StatusUpdateItem() {
      CheckCommand = new UiCommand("CheckForUpdate", _ => _ = CheckAsync(), _ => !_checking && !_ready);
      RestartCommand = new UiCommand("RestartToUpdate", _ => Restart(), _ => _ready);
      _timer.Tick += async (_, _) => { _timer.Stop(); await CheckAsync(); };
   }
   /// <summary>Manual update check.</summary>
   public ICommand CheckCommand { get; }
   /// <summary>Confirms and requests a normal application shutdown.</summary>
   public ICommand RestartCommand { get; }
   /// <summary>Whether an installed launcher started this process.</summary>
   public bool IsVisible => !string.IsNullOrWhiteSpace(LauncherIntegration.LauncherPath);
   /// <summary>Status text; only ready updates occupy text space.</summary>
   public string Text => _ready ? "Restart to update" : "";
   /// <summary>Status icon.</summary>
   public EFontAwesomeIcon Icon => _checking ? EFontAwesomeIcon.Solid_Spinner : _ready ? EFontAwesomeIcon.Solid_ArrowRotateRight : _failure.Length > 0 ? EFontAwesomeIcon.Solid_TriangleExclamation : EFontAwesomeIcon.Solid_Check;
   /// <summary>Whether the icon spins.</summary>
   public bool Spin => _checking;
   /// <summary>Whether an update is fully verified and ready.</summary>
   public bool IsReady => _ready;
   /// <summary>Current operation or last failure.</summary>
   public string ToolTip => _checking ? "Checking the release source; downloading and verifying update files..." : _ready ? "Update downloaded and verified. Restart to install." : _failure.Length > 0 ? "Check failed: " + _failure : "Up to date. Check for update from the menu.";
   internal void Attach(Window window, LauncherUpdateOptions options) {
      if (_window is not null) return;
      _window = window; _options = options;
      if (!IsVisible) return;
      _timer.Interval = options.FirstCheckDelay > TimeSpan.Zero ? options.FirstCheckDelay : TimeSpan.FromMilliseconds(1); _timer.Start();
      window.Closed += (_, _) => {
         Dispose();
         if (!_restart || LauncherIntegration.LauncherPath is not { } path) return;
         var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
         foreach (var arg in new[] { "--apply", "--pid", Environment.ProcessId.ToString() }) start.ArgumentList.Add(arg);
         Process.Start(start)?.Dispose();
      };
   }
   private async Task CheckAsync() {
      if (_checking || _ready || !IsVisible) return;
      _timer.Stop(); _checking = true; Changed();
      try {
         var start = new ProcessStartInfo(LauncherIntegration.LauncherPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
         start.ArgumentList.Add("--prepare"); start.ArgumentList.Add("--quiet");
         using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the installed launcher.");
         var output = process.StandardOutput.ReadToEndAsync(_cancel.Token); var errors = process.StandardError.ReadToEndAsync(_cancel.Token);
         await process.WaitForExitAsync(_cancel.Token); await errors;
         using var result = JsonDocument.Parse((await output).Trim());
         var state = result.RootElement.GetProperty("state").GetString();
         _ready = state == "prepared"; _failure = state == "failed" ? result.RootElement.GetProperty("message").GetString() ?? "Unknown launcher failure." : "";
         if (state is not ("prepared" or "none" or "failed")) throw new InvalidOperationException("Invalid launcher update response.");
      }
      catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
      catch (Exception ex) { _failure = ex.Message; }
      finally {
         _checking = false; Changed();
         if (!_ready && !_cancel.IsCancellationRequested) {
            var delay = _options.CheckInterval.TotalMilliseconds + (Random.Shared.NextDouble() * 2 - 1) * _options.Jitter.TotalMilliseconds;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, delay)); _timer.Start();
         }
      }
   }
   private void Changed() { foreach (var name in new[] { nameof(Text), nameof(Icon), nameof(Spin), nameof(ToolTip), nameof(IsReady) }) NotifyChanged(name); ((UiCommandBase)CheckCommand).RaiseCanExecuteChanged(); ((UiCommandBase)RestartCommand).RaiseCanExecuteChanged(); }
   private void Restart() {
      if (_window is null || !_ready || _window.ShowMboxDecideWarning("Restart now to install the verified update? Unsaved screens will ask before closing.", "Restart to update") != MessageBoxResult.Yes) return;
      _restart = true; _window.Close();
   }
   /// <summary>Stops future checks when the host closes.</summary>
   public void Dispose() { _timer.Stop(); if (!_cancel.IsCancellationRequested) _cancel.Cancel(); }
}
