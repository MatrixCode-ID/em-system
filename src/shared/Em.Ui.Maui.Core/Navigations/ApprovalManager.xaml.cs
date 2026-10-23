using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The mobile approval inbox: the requests the server lets the user see, as one column of cards. A card
   /// opens the request in <see cref="ApprovalViewer"/>.
   /// </summary>
   public partial class ApprovalManager : ContentView, INavigationBody
   {
      public ApprovalManager() {
         InitializeComponent();
      }

      /// <summary>The view model of this screen, read back from the BindingContext set in XAML.</summary>
      public ApprovalManagerVm Vm => (ApprovalManagerVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         var payload = args.Data as ApprovalManagerNavigationPayload;
         if (args.Data is string text && ApprovalManagerFilter.TryParse(text, out var kind, out var name))
            payload = new ApprovalManagerNavigationPayload { Kind = kind, Name = name, WaitingForMeOnly = true };
         Vm.Configure(payload);
         return Vm.RefreshAsync();
      }

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.RefreshAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.Release();
         return Task.CompletedTask;
      }
   }

   /// <summary>One request card in the approval inbox.</summary>
   public sealed class ApprovalRowVm : MvvmModelBase
   {
      private readonly ApprovalManagerVm _owner;

      public ApprovalRowVm(ApprovalManagerVm owner, ta_ApprovalRequest row) {
         _owner = owner;
         Row = row;
         RegisterCommand(nameof(OpenCommand), OpenCommand);
      }

      /// <summary>The request this card shows.</summary>
      public ta_ApprovalRequest Row { get; }

      /// <summary>The approval name, shown as the card caption.</summary>
      public string Title => Row.cApprovalName.ToUpperInvariant();

      /// <summary>The first summary line of the request.</summary>
      public string Summary => Row.cSummary;

      /// <summary>The optional second summary line.</summary>
      public string SubSummary => Row.cSubSummary ?? string.Empty;

      /// <summary><c>true</c> when there is a second summary line to draw.</summary>
      public bool HasSubSummary => !string.IsNullOrWhiteSpace(Row.cSubSummary);

      /// <summary>The readable stage of the request.</summary>
      public string Status => ApprovalTexts.Stage(Row.cApprovalRequestStage);

      /// <summary>A pending request is drawn with the accent chip.</summary>
      public bool IsHighlighted => Row.cApprovalRequestStage == ApprovalStage.Pending;

      /// <summary>Inverse of <see cref="IsHighlighted"/>, for binding without a converter.</summary>
      public bool IsNotHighlighted => !IsHighlighted;

      /// <summary>The kind, level and creation time of the request.</summary>
      public string Meta =>
         $"{Row.cApprovalRequestKind} · Level {Row.cApprovalRequestLevel} · {ApprovalTexts.Time(Row.datestamp)}";

      public Task OpenCommand() => _owner.OpenAsync(Row);
   }

   /// <summary>View model of <see cref="ApprovalManager"/>.</summary>
   public sealed class ApprovalManagerVm : MvvmModelBase
   {
      private const int PageSize = 30;

      private ApprovalManagerNavigationPayload _payload = new();
      private bool _configuring;
      private bool _released;
      private int _version;

      public ApprovalManagerVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterCommand(nameof(SearchCommand), SearchCommand, SearchCommandAllowed);
         RegisterCommand(nameof(NextCommand), NextCommand, NextCommandAllowed);
         RegisterCommand(nameof(PreviousCommand), PreviousCommand, PreviousCommandAllowed);
      }

      private IApprovalServices Service => EmApp!.ServiceProvider.GetRequiredService<IApprovalServices>();

      /// <summary>The cards of the current page.</summary>
      public ObservableCollection<ApprovalRowVm> Rows { get; } = [];

      /// <summary>The free-text search sent to the server.</summary>
      public string Search {
         get => Get(string.Empty);
         set => Set(value);
      }

      /// <summary>Limits the list to requests that wait for the user's own decision.</summary>
      public bool WaitingForMe {
         get => Get(true);
         set => Set(value, waiting => {
            if (_configuring) return;
            Page = 1;
            _ = RefreshAsync();
         });
      }

      /// <summary>Bound to the pull-to-refresh state of the list.</summary>
      public bool IsRefreshing {
         get => Get(false);
         set => Set(value);
      }

      /// <summary>The page shown, starting at 1.</summary>
      public int Page {
         get => Get(1);
         set => Set(value);
      }

      /// <summary>The number of requests matching the filter.</summary>
      public int Total {
         get => Get(0);
         set => Set(value, _ => {
            NotifyChanged(nameof(PageLabel));
            NotifyChanged(nameof(HasPages));
         });
      }

      /// <summary>"1-30 of 75".</summary>
      public string PageLabel {
         get {
            if (Total == 0) return "0 requests";
            var first = (Page - 1) * PageSize + 1;
            var last = Math.Min(Page * PageSize, Total);
            return $"{first}-{last} of {Total}";
         }
      }

      /// <summary>Paging is only drawn when the result does not fit one page.</summary>
      public bool HasPages => Total > PageSize;

      /// <summary>The last failure, shown above the list.</summary>
      public string Error {
         get => Get(string.Empty);
         set => Set(value, _ => NotifyChanged(nameof(HasError)));
      }

      /// <summary><c>true</c> when <see cref="Error"/> holds a message.</summary>
      public bool HasError => !string.IsNullOrEmpty(Error);

      /// <summary>
      /// Applies the navigation parameter. Without one, debugger and administrator accounts start from every
      /// request they may see, other users from the requests waiting for them.
      /// </summary>
      public void Configure(ApprovalManagerNavigationPayload? payload) {
         _configuring = true;
         try {
            _payload = payload ?? new ApprovalManagerNavigationPayload {
               WaitingForMeOnly = !(EmApp?.IsDebugMode == true || EmApp?.ActiveUser?.cUserIsAdmin == true)
            };
            WaitingForMe = _payload.WaitingForMeOnly;
            Page = 1;
         }
         finally {
            _configuring = false;
         }
      }

      /// <summary>Stops pending loads from touching a released screen.</summary>
      public void Release() {
         _released = true;
         _version++;
      }

      public Task RefreshCommand() => RefreshAsync();

      public bool RefreshCommandAllowed() => !IsBusy;

      public Task SearchCommand() {
         Page = 1;
         return RefreshAsync();
      }

      public bool SearchCommandAllowed() => !IsBusy;

      public Task NextCommand() {
         Page++;
         return RefreshAsync();
      }

      public bool NextCommandAllowed() => !IsBusy && Page * PageSize < Total;

      public Task PreviousCommand() {
         Page--;
         return RefreshAsync();
      }

      public bool PreviousCommandAllowed() => !IsBusy && Page > 1;

      /// <summary>Reloads the current page from the server.</summary>
      // Never throws: it runs from commands, whose async void execution has nothing above it to catch.
      public async Task RefreshAsync() {
         if (EmApp is null || _released) return;
         if (IsBusy) {
            IsRefreshing = false;
            return;
         }

         var version = ++_version;
         IsBusy = true;
         Error = string.Empty;
         RaiseCommands();
         try {
            var result = await Service.PostGetMeta_ApprovalRequests(new ApprovalQuery(_payload.Kind, _payload.Name,
               _payload.SubjectKey, null, WaitingForMe, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), Page, PageSize));
            if (_released || version != _version) return;

            Rows.Clear();
            foreach (var row in result.Items) Rows.Add(new ApprovalRowVm(this, row));
            Total = result.Total;
            NotifyChanged(nameof(PageLabel));
         }
         catch (Exception x) {
            Error = x.SerializedMessagesDefault();
         }
         finally {
            IsBusy = false;
            IsRefreshing = false;
            RaiseCommands();
         }
      }

      /// <summary>Opens one request in the viewer.</summary>
      public Task OpenAsync(ta_ApprovalRequest row) {
         var payload = new ApprovalManagerNavigationPayload {
            Kind = row.cApprovalRequestKind,
            Name = row.cApprovalName,
            ApprovalRequestId = row.cApprovalRequestId,
            WaitingForMeOnly = false,
            SystemViewer = true
         };
         return NavigationEntry?.NavigateTo(EmApp.ApprovalViewerNavigationName, payload)
                ?? EmApp!.NavigateTo(EmApp.ApprovalViewerNavigationName, payload);
      }

      private void RaiseCommands() {
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }
   }

   /// <summary>Readable texts shared by the approval screens.</summary>
   internal static class ApprovalTexts
   {
      public static string Stage(ApprovalStage stage) => stage switch {
         ApprovalStage.Pending => "Pending",
         ApprovalStage.Approved => "Approved",
         ApprovalStage.Rejected => "Rejected",
         ApprovalStage.Cancelled => "Void",
         ApprovalStage.ReinstatedAfterFinish => "Void",
         _ => stage.ToString()
      };

      public static string Time(DateTime value) => value.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
   }
}
