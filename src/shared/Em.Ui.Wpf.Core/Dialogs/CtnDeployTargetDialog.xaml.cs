using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Deploy target of one registry container: SSH or Portainer, Stack or Container, the registry login the Docker
   /// server pulls with, and automatic deploy after push. Test connection pins the server's fingerprint after the user
   /// confirms it. <see cref="CtnDeployTargetDialogVm.Changed"/> tells the caller to read the card again.
   /// </summary>
   public partial class CtnDeployTargetDialog : EmWindow
   {
      /// <summary>Creates a new instance of <see cref="CtnDeployTargetDialog"/>.</summary>
      public CtnDeployTargetDialog(ICtnServices service, CtnImageInfo image, CtnDeployTargetInfo? target, string registryHost) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(service, image, target, registryHost);
         Vm.CloseRequested += () => DialogResult = true;
      }

      /// <summary>ViewModel of this dialog.</summary>
      public CtnDeployTargetDialogVm Vm => (CtnDeployTargetDialogVm)DataContext;

      // PasswordBox has no bindable password; each box writes its value into the VM as it changes.
      private void PasswordChanged(object sender, RoutedEventArgs e) => Vm.SetPassword(SshPasswordBox.Password);
      private void PassphraseChanged(object sender, RoutedEventArgs e) => Vm.SetPassphrase(PassphraseBox.Password);
      private void TokenChanged(object sender, RoutedEventArgs e) => Vm.SetToken(TokenBox.Password);
      private void RegistryPasswordChanged(object sender, RoutedEventArgs e) => Vm.SetRegistryPassword(RegistryPasswordBox.Password);

      private void PickChanged(object sender, SelectionChangedEventArgs e) {
         if (sender is ComboBox { SelectedItem: string value } box) {
            Vm.Pick(box.Name, value);
            box.SelectedItem = null;
         }
      }
   }

   /// <summary>ViewModel for <see cref="CtnDeployTargetDialog"/>.</summary>
   public partial class CtnDeployTargetDialogVm : MvvmModelBase
   {
      private const string SavedHint = "Saved - leave empty to keep.";
      private ICtnServices? _service;
      private CtnImageInfo? _image;
      private CtnDeployTargetInfo? _stored;
      private string _password = "", _passphrase = "", _token = "", _registryPassword = "";

      [GeneratedRegex(@"^\s*(\d+)\s*(?:·\s*(.*))?$")]
      private static partial Regex IdAndName();

      /// <summary>Creates a new instance of <see cref="CtnDeployTargetDialogVm"/>.</summary>
      public CtnDeployTargetDialogVm() {
         RegisterCommand(nameof(TestCommand), TestCommand, () => IsNotBusy);
         RegisterCommand(nameof(SaveCommand), SaveCommand, () => IsNotBusy);
         RegisterCommand(nameof(DeleteCommand), DeleteCommand, () => IsNotBusy && _stored is not null);
         RegisterCommand(nameof(CreateStackCommand), CreateStackCommand, () => IsNotBusy);
         RegisterCommand(nameof(RegisterPortainerCommand), RegisterPortainerCommand, () => IsNotBusy && IsPortainer);
         RegisterCommand(nameof(LoadKeyCommand), LoadKeyCommand, () => IsNotBusy);
         RegisterCommand(nameof(ClearFingerprintCommand), ClearFingerprintCommand, () => IsNotBusy && Fingerprint.Length > 0);
      }

      /// <summary>Raised after Save or Delete; the dialog closes.</summary>
      public event Action? CloseRequested;

      /// <summary><c>true</c> when the target was saved, created, or deleted, so the caller reads it again.</summary>
      public bool Changed { get; private set; }

      internal void Initialize(ICtnServices service, CtnImageInfo image, CtnDeployTargetInfo? target, string registryHost) {
         _service = service;
         _image = image;
         Load(target, registryHost);
      }

      private ICtnServices Api => _service ?? throw new InvalidOperationException("The dialog is not initialized.");

      private void Load(CtnDeployTargetInfo? t, string registryHost) {
         _stored = t;
         IsActive = t?.IsActive ?? true;
         Kind = t?.Kind ?? CtnDeployKind.Ssh;
         Mode = t?.Mode ?? CtnDeployMode.Stack;
         SshAuth = t?.Auth is CtnDeployAuth.SshPassword ? CtnDeployAuth.SshPassword : CtnDeployAuth.SshKey;
         Host = t?.Host ?? "";
         PortText = (t?.Port ?? 22).ToString();
         User = t?.User ?? "";
         PrivateKey = "";
         Fingerprint = t?.Fingerprint ?? "";
         EndpointText = t?.EndpointId?.ToString() ?? "";
         SshFolder = t?.Kind == CtnDeployKind.Ssh ? t.Stack ?? "" : "";
         StackText = t?.Kind == CtnDeployKind.Portainer ? (t.StackId is { } id ? $"{id} · {t.Stack}" : t.Stack ?? "") : "";
         Service = t?.Service ?? "";
         Container = t?.Container ?? "";
         ImageVariable = t?.ImageVariable ?? "";
         TagFilter = t?.TagFilter ?? "";
         RegistryHost = t?.RegistryHost ?? registryHost;
         RegistryUser = t?.RegistryUser ?? "";
         NotifyAll();
      }

      #region Fields

      /// <summary>The title.</summary>
      public string Title => _image is null ? "Deploy" : $"Deploy {_image.FullName}";

      /// <summary>The kinds.</summary>
      public CtnDeployChoice<CtnDeployKind>[] Kinds { get; } = [new(CtnDeployKind.Ssh, "SSH"), new(CtnDeployKind.Portainer, "Portainer")];

      /// <summary>The modes.</summary>
      public CtnDeployChoice<CtnDeployMode>[] Modes { get; } = [
         new(CtnDeployMode.Stack, "Stack (compose service)"), new(CtnDeployMode.Container, "Container (standalone)")
      ];

      /// <summary>The ssh auths.</summary>
      public CtnDeployChoice<CtnDeployAuth>[] SshAuths { get; } = [new(CtnDeployAuth.SshKey, "Private key"), new(CtnDeployAuth.SshPassword, "Password")];

      // The combo boxes bind to these; the enum properties stay the source of truth.
      /// <summary>The kind choice.</summary>
      public CtnDeployChoice<CtnDeployKind> KindChoice { get => Kinds.First(k => k.Value == Kind); set => Kind = value.Value; }
      /// <summary>The mode choice.</summary>
      public CtnDeployChoice<CtnDeployMode> ModeChoice { get => Modes.First(m => m.Value == Mode); set => Mode = value.Value; }
      /// <summary>The ssh auth choice.</summary>
      public CtnDeployChoice<CtnDeployAuth> SshAuthChoice { get => SshAuths.First(a => a.Value == SshAuth); set => SshAuth = value.Value; }

      /// <summary>Indicates active.</summary>
      public bool IsActive { get => Get<bool>(); set => Set(value); }

      /// <summary>The kind.</summary>
      public CtnDeployKind Kind {
         get => Get(CtnDeployKind.Ssh);
         set => Set(value, _ => NotifyAll());
      }

      /// <summary>The mode.</summary>
      public CtnDeployMode Mode {
         get => Get(CtnDeployMode.Stack);
         set => Set(value, _ => NotifyAll());
      }

      /// <summary>The ssh auth.</summary>
      public CtnDeployAuth SshAuth {
         get => Get(CtnDeployAuth.SshKey);
         set => Set(value, _ => NotifyAll());
      }

      /// <summary>Indicates ssh.</summary>
      public bool IsSsh => Kind == CtnDeployKind.Ssh;
      /// <summary>Indicates portainer.</summary>
      public bool IsPortainer => Kind == CtnDeployKind.Portainer;
      /// <summary>Indicates stack.</summary>
      public bool IsStack => Mode == CtnDeployMode.Stack;
      /// <summary>Indicates container.</summary>
      public bool IsContainer => Mode == CtnDeployMode.Container;
      /// <summary>Indicates key auth.</summary>
      public bool IsKeyAuth => IsSsh && SshAuth == CtnDeployAuth.SshKey;
      /// <summary>Indicates password auth.</summary>
      public bool IsPasswordAuth => IsSsh && SshAuth == CtnDeployAuth.SshPassword;
      /// <summary>Indicates ssh stack.</summary>
      public bool IsSshStack => IsSsh && IsStack;
      /// <summary>Indicates portainer stack.</summary>
      public bool IsPortainerStack => IsPortainer && IsStack;
      /// <summary>Indicates the endpoint is shown.</summary>
      public bool ShowEndpoint => IsPortainer;

      /// <summary>The host.</summary>
      public string Host { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The port text.</summary>
      public string PortText { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The user.</summary>
      public string User { get => Get<string>() ?? ""; set => Set(value); }

      /// <summary>Private key pasted or loaded from a file; empty keeps the saved one.</summary>
      public string PrivateKey { get => Get<string>() ?? ""; set => Set(value); }

      /// <summary>Pinned fingerprint; filled by Test connection after the user accepts it.</summary>
      public string Fingerprint {
         get => Get<string>() ?? "";
         set => Set(value, _ => {
            NotifyChanged(nameof(FingerprintCaption));
            RaiseCommandsChanged();
         });
      }

      /// <summary>The fingerprint caption.</summary>
      public string FingerprintCaption => Fingerprint.Length > 0 ? Fingerprint : "Not pinned - Test connection asks you to accept the server's fingerprint.";

      /// <summary>The endpoint text.</summary>
      public string EndpointText { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The ssh folder.</summary>
      public string SshFolder { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The stack text.</summary>
      public string StackText { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The service.</summary>
      public string Service { get => Get<string>() ?? ""; set => Set(value, _ => NotifyChanged(nameof(VariableHint))); }
      /// <summary>The container.</summary>
      public string Container { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The image variable.</summary>
      public string ImageVariable { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The tag filter.</summary>
      public string TagFilter { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The registry host.</summary>
      public string RegistryHost { get => Get<string>() ?? ""; set => Set(value); }
      /// <summary>The registry user.</summary>
      public string RegistryUser { get => Get<string>() ?? ""; set => Set(value); }

      /// <summary>The variable hint.</summary>
      public string VariableHint => "Default: " + DefaultVariable(Service.Length > 0 ? Service : "service");

      /// <summary>The key hint.</summary>
      public string KeyHint => _stored is { HasSecret: true, Auth: CtnDeployAuth.SshKey } ? SavedHint : "Paste the private key (OpenSSH or PEM), or load it from a file.";
      /// <summary>Indicates there is saved password.</summary>
      public bool HasSavedPassword => _stored is { HasSecret: true, Auth: CtnDeployAuth.SshPassword };
      /// <summary>Indicates there is saved passphrase.</summary>
      public bool HasSavedPassphrase => _stored is { HasPassphrase: true };
      /// <summary>Indicates there is saved token.</summary>
      public bool HasSavedToken => _stored is { HasSecret: true, Auth: CtnDeployAuth.PortainerToken };
      /// <summary>Indicates there is saved registry password.</summary>
      public bool HasSavedRegistryPassword => _stored is { HasRegistrySecret: true };
      /// <summary>The saved caption.</summary>
      public string SavedCaption => SavedHint;

      // Choices filled by Test connection; picking one writes the text field next to it.
      /// <summary>The endpoint choices.</summary>
      public string[] EndpointChoices { get => Get<string[]>() ?? []; private set => Set(value); }
      /// <summary>The stack choices.</summary>
      public string[] StackChoices { get => Get<string[]>() ?? []; private set => Set(value); }
      /// <summary>The service choices.</summary>
      public string[] ServiceChoices { get => Get<string[]>() ?? []; private set => Set(value); }
      /// <summary>The container choices.</summary>
      public string[] ContainerChoices { get => Get<string[]>() ?? []; private set => Set(value); }

      /// <summary>The test text.</summary>
      public string TestText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasTestText))); }
      /// <summary>Indicates there is test text.</summary>
      public bool HasTestText => TestText.Length > 0;

      /// <summary>Indicates test failed.</summary>
      public bool TestFailed { get => Get<bool>(); private set => Set(value); }

      /// <summary>Indicates the register portainer is shown.</summary>
      public bool ShowRegisterPortainer { get => Get<bool>(); private set => Set(value); }

      /// <summary>The error text.</summary>
      public string ErrorText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasError))); }
      /// <summary>Indicates there is error.</summary>
      public bool HasError => ErrorText.Length > 0;

      internal void SetPassword(string value) => _password = value;
      internal void SetPassphrase(string value) => _passphrase = value;
      internal void SetToken(string value) => _token = value;
      internal void SetRegistryPassword(string value) => _registryPassword = value;

      internal void Pick(string field, string value) {
         switch (field) {
            case "pickEndpoint": EndpointText = value; break;
            case "pickStack": StackText = value.EndsWith(" (Git)", StringComparison.Ordinal) ? value[..^6] : value; break;
            case "pickService": Service = value; break;
            case "pickContainer": Container = value; break;
         }
      }

      private void NotifyAll() {
         foreach (var name in new[] {
                     nameof(Title), nameof(IsActive), nameof(Kind), nameof(Mode), nameof(SshAuth), nameof(KindChoice), nameof(ModeChoice),
                     nameof(SshAuthChoice), nameof(IsSsh), nameof(IsPortainer),
                     nameof(IsStack), nameof(IsContainer), nameof(IsKeyAuth), nameof(IsPasswordAuth), nameof(IsSshStack),
                     nameof(IsPortainerStack), nameof(ShowEndpoint), nameof(KeyHint), nameof(HasSavedPassword), nameof(HasSavedPassphrase),
                     nameof(HasSavedToken), nameof(HasSavedRegistryPassword), nameof(VariableHint), nameof(FingerprintCaption)
                  }) {
            NotifyChanged(name);
         }

         RaiseCommandsChanged();
      }

      private static string DefaultVariable(string service) =>
         "EM_IMAGE_" + Regex.Replace(service.ToUpperInvariant(), "[^A-Z0-9]", "_");

      #endregion

      #region Request

      // Builds the request; empty secret boxes keep the saved values (null). A changed sign-in method needs a new secret.
      private CtnDeployTargetSave Build() {
         var auth = IsSsh ? SshAuth : CtnDeployAuth.PortainerToken;
         var secret = IsKeyAuth ? PrivateKey.Trim() : IsPasswordAuth ? _password : _token;
         if (secret.Length == 0 && _stored is { HasSecret: true } stored && stored.Auth != auth) {
            throw new InvalidDataException(IsKeyAuth ? "Paste the private key for the new sign-in method."
               : IsPasswordAuth ? "Enter the SSH password for the new sign-in method." : "Enter the Portainer access token.");
         }

         int? port = null;
         if (IsSsh && PortText.Trim().Length > 0) {
            port = int.TryParse(PortText.Trim(), out var value) ? value : throw new InvalidDataException("SSH port must be a number.");
         }

         int? endpoint = null;
         if (IsPortainer && EndpointText.Trim().Length > 0) {
            endpoint = IdAndName().Match(EndpointText) is { Success: true } m ? int.Parse(m.Groups[1].Value)
               : throw new InvalidDataException("Environment must be the numeric Portainer environment id.");
         }

         string? stackName = null;
         int? stackId = null;
         if (IsPortainer && StackText.Trim().Length > 0) {
            if (IdAndName().Match(StackText) is { Success: true } m) {
               stackId = int.Parse(m.Groups[1].Value);
               stackName = m.Groups[2].Value.Trim() is { Length: > 0 } name ? name : null;
            }
            else {
               stackName = StackText.Trim();
            }
         }

         return new CtnDeployTargetSave {
            ImageId = _image!.Id, IsActive = IsActive, Kind = Kind, Mode = Mode, TagFilter = Empty(TagFilter),
            RegistryHost = RegistryHost.Trim(), RegistryUser = Empty(RegistryUser), RegistrySecret = Secret(_registryPassword),
            Host = Host.Trim(), Port = port, User = IsSsh ? Empty(User) : null, Auth = auth, Secret = Secret(secret),
            Passphrase = IsKeyAuth ? Secret(_passphrase) : null, Fingerprint = Empty(Fingerprint),
            EndpointId = endpoint, Stack = IsSsh ? Empty(SshFolder) : stackName, StackId = stackId, Service = Empty(Service),
            Container = Empty(Container), ImageVariable = Empty(ImageVariable)
         };
      }

      private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

      private static string? Secret(string value) => value.Length == 0 ? null : value;

      #endregion

      #region Commands

      /// <summary>Tests the connection with what the dialog holds now, confirming and pinning a new fingerprint.</summary>
      public async Task TestCommand() {
         await RunAsync("Testing connection...", async () => {
            var result = await Api.PostGetMeta_CtnDeployTest(Build());
            if (result.OfferedFingerprint is { } offered && MainWindow is { } owner) {
               var answer = owner.ShowMboxDecideWarning(
                  $"{Host} presents this fingerprint:\n\n{offered}\n\nCompare it with the server (ssh-keygen -lf on the host key, or the " +
                  "certificate in a browser). Trust it? Only this fingerprint is accepted from then on.", "Confirm Server Fingerprint");
               if (answer != MessageBoxResult.Yes) {
                  ShowTest(result);
                  return;
               }

               Fingerprint = offered;
               result = await Api.PostGetMeta_CtnDeployTest(Build());
            }

            ShowTest(result);
         });
      }

      private void ShowTest(CtnDeployTestResult result) {
         TestFailed = !result.Success;
         TestText = string.Join("\n", result.Messages.Prepend(result.Success ? "Connection test passed." : "Connection test found problems:"));
         ShowRegisterPortainer = result.PortainerRegistryMissing;
         if (result.Endpoints.Length > 0) EndpointChoices = [.. result.Endpoints.Select(e => $"{e.Id} · {e.Name}")];
         if (result.Stacks.Length > 0) StackChoices = [.. result.Stacks.Select(s => $"{s.Id} · {s.Name}{(s.IsGit ? " (Git)" : "")}")];
         if (result.Services.Length > 0) ServiceChoices = result.Services;
         if (result.Containers.Length > 0) ContainerChoices = result.Containers;
      }

      /// <summary>Saves the target and closes.</summary>
      public async Task SaveCommand() {
         var saved = false;
         await RunAsync("Saving...", async () => {
            await SaveAsync();
            saved = true;
         });
         if (saved) CloseRequested?.Invoke();
      }

      private async Task SaveAsync() {
         var target = await Api.PostGetMeta_CtnDeployTargetSave(Build());
         Changed = true;
         _stored = target;
         NotifyAll();
      }

      /// <summary>Deletes the target and its history after confirmation, and closes.</summary>
      public async Task DeleteCommand() {
         if (MainWindow is not { } owner || _image is null) return;
         if (owner.ShowMboxDecideWarning($"Delete the deploy target of {_image.FullName} and its deploy history?\n\n" +
                                          "Nothing changes on the Docker server.", "Delete Deploy Target") != MessageBoxResult.Yes) {
            return;
         }

         var deleted = false;
         await RunAsync("Deleting...", async () => {
            await Api.PostMeta_CtnDeployTargetDelete(_image.Id);
            Changed = deleted = true;
         });
         if (deleted) CloseRequested?.Invoke();
      }

      /// <summary>Saves, then opens the Create stack editor; on success the fields show the new stack.</summary>
      public async Task CreateStackCommand() {
         if (MainWindow is not { } owner || _image is null) return;

         var ready = false;
         await RunAsync("Saving...", async () => {
            await SaveAsync();
            ready = true;
         });
         if (!ready) return;

         var dialog = new CtnDeployStackDialog(Api, _image, Kind) { Owner = owner };
         dialog.ShowDialog();
         if (!dialog.Vm.Created) return;

         await RunAsync("Loading...", async () => Load(await Api.GetMeta_CtnDeployTarget(_image.Id), RegistryHost));
      }

      /// <summary>Saves, then adds the registry to Portainer with the registry login and tests again.</summary>
      public async Task RegisterPortainerCommand() {
         if (_image is null) return;
         await RunAsync("Registering the registry in Portainer...", async () => {
            await SaveAsync();
            ShowTest(await Api.PostGetMeta_CtnDeployRegisterPortainerRegistry(_image.Id));
         });
      }

      /// <summary>Loads a private key file into the key box.</summary>
      public void LoadKeyCommand() {
         var picker = new OpenFileDialog {
            Title = "Private key", Filter = "All files (*.*)|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh")
         };
         if (picker.ShowDialog() != true) return;
         try {
            PrivateKey = File.ReadAllText(picker.FileName);
         }
         catch (Exception x) {
            ErrorText = x.Message;
         }
      }

      /// <summary>Forgets the pinned fingerprint; the next test asks again.</summary>
      public void ClearFingerprintCommand() => Fingerprint = "";

      private async Task RunAsync(string waiterText, Func<Task> work) {
         if (IsBusy) return;
         ErrorText = "";
         WaiterText = waiterText;
         IsBusy = InWaiting = true;
         RaiseCommandsChanged();
         try {
            await work();
         }
         catch (ActionException x) {
            ErrorText = ContainerManagerVm.ServerMessage(x);
         }
         catch (Exception x) when (x is InvalidDataException or TimeoutException) {
            ErrorText = x.Message;
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseCommandsChanged();
         }
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }

   /// <summary>An option of a combo box: the value and the text shown for it.</summary>
   public sealed class CtnDeployChoice<T>(T value, string name)
   {
      /// <summary>The value.</summary>
      public T Value { get; } = value;
      /// <summary>The name.</summary>
      public string Name { get; } = name;
      /// <inheritdoc />
      public override string ToString() => Name;
   }
}
