using System.Collections.ObjectModel;
using System.Globalization;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>A choice of the Kind field.</summary>
/// <param name="Value">The kind.</param>
/// <param name="Caption">Text shown in the list.</param>
public sealed record HolidayKindOption(PublicHolidayKind Value, string Caption);

/// <summary>One row of the holiday list.</summary>
public sealed class HolidayItem
{
   private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

   /// <summary>Wraps a holiday; a row without an id is a draft.</summary>
   public HolidayItem(vi_PublicHoliday row) { Row = row; }

   /// <summary>The holiday as read.</summary>
   public vi_PublicHoliday Row { get; }

   /// <summary>Whether this row has not been saved.</summary>
   public bool IsDraft => Row.cPublicHolidayId.Length == 0;

   /// <summary>Name shown in the list.</summary>
   public string DisplayName => IsDraft ? "(new holiday)" : Row.cPublicHolidayName;

   /// <summary>Whether the holiday is fixed.</summary>
   public bool IsFixed => Row.cPublicHolidayKind == PublicHolidayKind.Fixed;

   /// <summary>Day and month shown in the list.</summary>
   public string DateCaption => IsDraft ? "" : (Row.cPublicHolidayDate ?? new DateTime(2024, Row.cPublicHolidayMonth ?? 1, Row.cPublicHolidayDay ?? 1)).ToString("dd MMM", Culture);

   /// <summary>Kind shown in the list.</summary>
   public string KindCaption => IsDraft ? "" : IsFixed ? "Fixed" : "Dated";

   /// <summary>Whether the marker chip is shown.</summary>
   public bool IsCollective => Row.cPublicHolidayCollective;
}

/// <summary>State and commands of the Public holidays page: the calendar shared by every tenant.</summary>
public sealed class BusinessModuleManagerHolidayVm : MvvmModelBase
{
   private readonly IPublicHolidayServices? _service;
   private bool _refreshingRows;
   private bool _opening;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerHolidayVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerHolidayVm(IPublicHolidayServices service) { _service = service; RegisterCommands(); }

   private IPublicHolidayServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<IPublicHolidayServices>();

   private void RegisterCommands() {
      var today = DateTime.Today.Year;
      for (var year = today - 3; year <= today + 3; year++) Years.Add(year);
      Set(today, propertyName: nameof(SelectedYear));
      Set(today - 1, propertyName: nameof(CopyFromYear));
      RegisterCommand(nameof(NewHolidayCommand), NewHolidayCommand, NewHolidayCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(RemoveCommand), RemoveCommand, RemoveCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      RegisterCommand(nameof(CopyDatedCommand), CopyDatedCommand, CopyDatedCommandAllowed);
   }

   /// <summary>The kinds of entry.</summary>
   public IReadOnlyList<HolidayKindOption> KindOptions { get; } = [new(PublicHolidayKind.Fixed, "Fixed (every year)"), new(PublicHolidayKind.Dated, "Dated (this year only)")];

   /// <summary>Years offered in the toolbar.</summary>
   public ObservableCollection<int> Years { get; } = [];

   /// <summary>The year being shown.</summary>
   public int SelectedYear {
      get => Get<int>();
      set {
         if (value == SelectedYear) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(value);
         DropDraft(null); Open(null);
         Loading = ReloadAsync(); UpdateState();
      }
   }

   /// <summary>The year dated entries are copied from.</summary>
   public int CopyFromYear { get => Get<int>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Most recent load, also awaitable by the owning screen.</summary>
   public Task Loading { get; set; } = Task.CompletedTask;

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   /// <summary>Host-supplied question asked before an entry is removed; <c>true</c> = remove.</summary>
   public Func<vi_PublicHoliday, bool>? ConfirmRemove { get; set; }

   /// <summary>Entries of the year, and a draft when one is open.</summary>
   public ObservableCollection<HolidayItem> Rows { get; } = [];

   /// <summary>The entry open in the editor.</summary>
   public HolidayItem? SelectedRow {
      get => Get<HolidayItem>();
      set {
         if (_refreshingRows) return;
         if (_opening) { Set(value); return; }
         if (ReferenceEquals(value, SelectedRow)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         DropDraft(value);
         Open(value);
      }
   }

   /// <summary>Kind of the edited entry.</summary>
   public HolidayKindOption? EditKind { get => Get<HolidayKindOption>(); set => Set(value, _ => { NotifyChanged(nameof(IsFixedKind)); NotifyChanged(nameof(IsDatedKind)); UpdateState(); }); }

   /// <summary>Name of the edited entry.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Month of a fixed entry.</summary>
   public decimal? EditMonth { get => Get<decimal?>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Day of a fixed entry.</summary>
   public decimal? EditDay { get => Get<decimal?>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Date of a dated entry.</summary>
   public DateTime? EditDate { get => Get<DateTime?>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Collective leave marker of the edited entry.</summary>
   public bool EditCollective { get => Get<bool>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited entry.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Whether the editor shows month and day.</summary>
   public bool IsFixedKind => EditKind?.Value == PublicHolidayKind.Fixed;

   /// <summary>Whether the editor shows a date.</summary>
   public bool IsDatedKind => EditKind?.Value == PublicHolidayKind.Dated;

   /// <summary>Whether an entry is open in the editor.</summary>
   public bool HasSelection => SelectedRow is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedRow is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedRow?.IsDraft == true;

   /// <summary>Whether the editor differs from the saved entry; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedRow is { Row: var row } item && (item.IsDraft || EditKind?.Value != row.cPublicHolidayKind || EditName != row.cPublicHolidayName ||
      EditMonth != row.cPublicHolidayMonth || EditDay != row.cPublicHolidayDay || EditDate?.Date != row.cPublicHolidayDate?.Date ||
      EditCollective != row.cPublicHolidayCollective || (EditNote ?? "") != (row.cPublicHolidayNote ?? ""));

   /// <summary>Count of the entries shown.</summary>
   public string Summary {
      get {
         var saved = Rows.Where(x => !x.IsDraft).ToArray();
         return $"{saved.Length} {(saved.Length == 1 ? "entry" : "entries")} · {saved.Count(x => x.IsFixed)} fixed, {saved.Count(x => !x.IsFixed)} dated";
      }
   }

   /// <summary>Warning that the year has no dated entries; empty when it has.</summary>
   public string Warning { get => Get<string>() ?? ""; private set => Set(value, _ => NotifyChanged(nameof(HasWarning))); }

   /// <summary>Whether the warning is shown.</summary>
   public bool HasWarning => Warning.Length > 0;

   /// <summary>Whether the toolbar and list accept input.</summary>
   public bool CanBrowse => IsLoaded && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the latest load succeeded.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }

   /// <summary>Result or server error shown below the toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Checks whether unsaved changes may be discarded.</summary>
   public bool CanLeave() => !IsDirty || ConfirmDiscard?.Invoke() != false;

   /// <summary>Restores the last saved values after the owning screen confirmed discard.</summary>
   public void DiscardChanges() {
      if (!IsDirty) return;
      if (IsDraftSelected) { DropDraft(null); Open(null); }
      else Open(SelectedRow);
   }

   /// <summary>Whether a new entry can be started.</summary>
   public bool NewHolidayCommandAllowed() => CanBrowse;

   /// <summary>Starts a new entry as a draft row, dated in the year being shown.</summary>
   public void NewHolidayCommand() {
      if (!NewHolidayCommandAllowed() || !CanLeave()) return;
      DropDraft(null);
      var draft = new HolidayItem(new vi_PublicHoliday { cPublicHolidayKind = PublicHolidayKind.Dated, cPublicHolidayDate = new DateTime(SelectedYear, 1, 1) });
      Rows.Add(draft);
      Open(draft);
      Status = "";
   }

   /// <summary>Whether the editor holds a complete entry with changes.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && !string.IsNullOrWhiteSpace(EditName) && (IsFixedKind ? EditMonth is not null && EditDay is not null : EditDate is not null);

   /// <summary>Creates the draft or saves the changes of the selected entry, then reloads the list.</summary>
   public Task SaveCommand() => RunAsync("Saving holiday...", async () => {
      if (SelectedRow is not { } item || Service is not { } service) return;
      var draft = item.IsDraft;
      var change = new ta_PublicHoliday {
         cPublicHolidayId = item.Row.cPublicHolidayId, cPublicHolidayName = EditName, cPublicHolidayKind = EditKind!.Value,
         cPublicHolidayMonth = IsFixedKind ? (int?)EditMonth : null, cPublicHolidayDay = IsFixedKind ? (int?)EditDay : null, cPublicHolidayDate = IsDatedKind ? EditDate?.Date : null,
         cPublicHolidayCollective = EditCollective, cPublicHolidayNote = string.IsNullOrEmpty(EditNote) ? null : EditNote,
         cPublicHolidayState = ta_PublicHoliday.StateActive, json_object = item.Row.json_object, ustamp = item.Row.ustamp
      };
      var saved = draft ? await service.PostGetTa_PublicHoliday_New(change) : await service.PostGetTa_PublicHoliday_Update(change);
      await Load(saved.cPublicHolidayId);
      Status = draft ? $"Holiday '{saved.cPublicHolidayName}' created." : $"Holiday '{saved.cPublicHolidayName}' saved.";
   });

   /// <summary>Whether the selected saved entry, without unsaved changes, can be removed.</summary>
   public bool RemoveCommandAllowed() => CanEdit && !IsDraftSelected && !IsDirty;

   /// <summary>Removes the selected entry after the host confirms. The record stays in the database.</summary>
   public async Task RemoveCommand() {
      if (!RemoveCommandAllowed() || SelectedRow is not { } item || ConfirmRemove?.Invoke(item.Row) == false) return;
      await RunAsync("Removing holiday...", async () => {
         if (Service is not { } service) return;
         var row = item.Row;
         await service.PostGetTa_PublicHoliday_Update(new ta_PublicHoliday {
            cPublicHolidayId = row.cPublicHolidayId, cPublicHolidayName = row.cPublicHolidayName, cPublicHolidayKind = row.cPublicHolidayKind, cPublicHolidayMonth = row.cPublicHolidayMonth,
            cPublicHolidayDay = row.cPublicHolidayDay, cPublicHolidayDate = row.cPublicHolidayDate, cPublicHolidayCollective = row.cPublicHolidayCollective,
            cPublicHolidayNote = row.cPublicHolidayNote, json_object = row.json_object, cPublicHolidayState = ta_PublicHoliday.StateRemoved, ustamp = row.ustamp
         });
         await Load(null);
         Status = $"Holiday '{row.cPublicHolidayName}' removed.";
      });
   }

   /// <summary>Whether the calendar can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy;

   /// <summary>Reads the year again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   /// <summary>Whether dated entries can be copied into the year being shown.</summary>
   public bool CopyDatedCommandAllowed() => CanBrowse && CopyFromYear != SelectedYear;

   /// <summary>Copies the dated entries of <see cref="CopyFromYear"/> into the year being shown.</summary>
   public async Task CopyDatedCommand() {
      if (!CopyDatedCommandAllowed() || !CanLeave()) return;
      await RunAsync("Copying holidays...", async () => {
         if (Service is not { } service) return;
         var result = await service.PostGetMeta_PublicHoliday_CopyDated(CopyFromYear, SelectedYear);
         await Load(null);
         Status = $"Copied {result.Created.Length} from {CopyFromYear}; {result.Skipped} skipped.";
      });
   }

   /// <summary>Reads the year again, keeping the selected entry when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading holidays...", async () => {
      await Load(SelectedRow is { IsDraft: false } item ? item.Row.cPublicHolidayId : null);
      Status = $"Loaded {Rows.Count} {(Rows.Count == 1 ? "holiday" : "holidays")} for {SelectedYear}. Holidays apply to all tenants.";
   });

   private async Task Load(string? selectId) {
      if (Service is not { } service) return;
      var rows = await service.GetVi_PublicHolidays(SelectedYear);
      var hasDated = await service.GetMeta_PublicHoliday_HasDated(SelectedYear);
      _refreshingRows = true;
      try {
         Rows.Clear();
         foreach (var row in rows) Rows.Add(new HolidayItem(row));
      }
      finally { _refreshingRows = false; }
      Open(Rows.FirstOrDefault(x => string.Equals(x.Row.cPublicHolidayId, selectId, StringComparison.OrdinalIgnoreCase)));
      Warning = hasDated ? "" : $"{SelectedYear} has no dated holidays yet. Dated holidays are entered manually each year; the engine does not calculate them.";
      IsLoaded = true;
      NotifyChanged(nameof(Summary));
   }

   // Removes a draft row other than the one being opened.
   private void DropDraft(HolidayItem? keep) {
      if (Rows.FirstOrDefault(x => x.IsDraft) is not { } draft || ReferenceEquals(draft, keep)) return;
      _refreshingRows = true;
      try { Rows.Remove(draft); }
      finally { _refreshingRows = false; }
   }

   private void Open(HolidayItem? item) {
      _opening = true;
      try {
         SelectedRow = item;
         var row = item?.Row;
         EditKind = row is null ? null : KindOptions.FirstOrDefault(x => x.Value == row.cPublicHolidayKind);
         EditName = row?.cPublicHolidayName ?? ""; EditMonth = row?.cPublicHolidayMonth; EditDay = row?.cPublicHolidayDay; EditDate = row?.cPublicHolidayDate;
         EditCollective = row?.cPublicHolidayCollective ?? false; EditNote = row?.cPublicHolidayNote;
      }
      finally { _opening = false; }
      NotifyChanged(nameof(SelectedRow));
      UpdateState();
   }

   private async Task RunAsync(string caption, Func<Task> action) {
      if (IsBusy || Service is null) return;
      try {
         IsBusy = InWaiting = true; WaiterText = caption; UpdateState();
         await action();
      }
      catch (ActionException ex) { Status = ex.Message.Replace("Server Error: ", ""); }
      catch (Exception ex) {
         Status = "The operation did not complete. Refresh and try again.";
         if (EmApp is not null) AlertError(ex);
      }
      finally { InWaiting = IsBusy = false; UpdateState(); }
   }

   private void UpdateState() {
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(IsDirty), nameof(CanBrowse), nameof(CanEdit), nameof(Summary)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
