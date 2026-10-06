using System.Windows;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Create stack: a compose template with the container's image variable and name, edited by the user, then created
   /// on the target (compose file over SSH, or a Portainer stack). <see cref="CtnDeployStackDialogVm.Created"/> tells
   /// the caller the target now points at the new stack.
   /// </summary>
   public partial class CtnDeployStackDialog : EmWindow
   {
      public CtnDeployStackDialog(ICtnServices service, CtnImageInfo image, CtnDeployKind kind) {
         InitializeComponent();
         Vm.MainWindow = this;
         Vm.Initialize(service, image, kind);
         Loaded += async (_, _) => await Vm.LoadTemplateAsync();
      }

      /// <summary>ViewModel of this dialog.</summary>
      public CtnDeployStackDialogVm Vm => (CtnDeployStackDialogVm)DataContext;
   }

   /// <summary>ViewModel for <see cref="CtnDeployStackDialog"/>.</summary>
   public class CtnDeployStackDialogVm : MvvmModelBase
   {
      private ICtnServices? _service;
      private CtnImageInfo? _image;

      public CtnDeployStackDialogVm() {
         RegisterCommand(nameof(CreateCommand), CreateCommand, () => IsNotBusy && !Created && Content.Trim().Length > 0);
      }

      internal void Initialize(ICtnServices service, CtnImageInfo image, CtnDeployKind kind) {
         _service = service;
         _image = image;
         Caption = kind == CtnDeployKind.Ssh
            ? "Creates compose.yml and .env in the compose folder of the target. Nothing starts until you press Deploy. An existing compose file is never overwritten."
            : "Creates and starts a Portainer stack with this file in the environment of the target, using the stack name of the target.";
      }

      private ICtnServices Api => _service ?? throw new InvalidOperationException("The dialog is not initialized.");

      public string Title => _image is null ? "Create stack" : $"Create stack for {_image.FullName}";

      public string Caption { get => Get<string>() ?? ""; private set => Set(value); }

      /// <summary>Compose file to create; only the image and the container name are filled in.</summary>
      public string Content {
         get => Get<string>() ?? "";
         set => Set(value, _ => RaiseCommandsChanged());
      }

      public string OutputText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasOutput))); }
      public bool HasOutput => OutputText.Length > 0;

      public string ErrorText { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasError))); }
      public bool HasError => ErrorText.Length > 0;

      /// <summary><c>true</c> once the stack was created.</summary>
      public bool Created {
         get => Get<bool>();
         private set => Set(value, _ => RaiseCommandsChanged());
      }

      internal async Task LoadTemplateAsync() {
         if (_image is null) return;
         await RunAsync("Loading template...", async () => Content = await Api.GetMeta_CtnDeployStackTemplate(_image.Id));
      }

      /// <summary>Creates the stack after confirmation and shows what the server did.</summary>
      public async Task CreateCommand() {
         if (_image is null || MainWindow is not { } owner) return;
         if (owner.ShowMboxDecideWarning("Create the stack on the Docker server now?", "Create Stack") != MessageBoxResult.Yes) return;

         await RunAsync("Creating stack...", async () => {
            var run = await Api.PostGetMeta_CtnDeployCreateStack(_image.Id, Content);
            OutputText = run.Output ?? "";
            if (run.Result == CtnDeployResult.Success) Created = true;
            else ErrorText = "Creating the stack failed; see the output.";
         });
      }

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
         catch (TimeoutException x) {
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
   }
}
