using System.Collections.ObjectModel;
using System.Globalization;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations;

/// <summary>One row of the template list.</summary>
public sealed class SchedTemplateItem
{
   private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

   /// <summary>Wraps a template; a row without an id is a draft.</summary>
   public SchedTemplateItem(vi_TenantSchedTemplate row) { Row = row; }

   /// <summary>The template as read.</summary>
   public vi_TenantSchedTemplate Row { get; }

   /// <summary>Whether this row has not been saved.</summary>
   public bool IsDraft => Row.cTenantSchedTemplateId.Length == 0;

   /// <summary>Name shown in the list.</summary>
   public string DisplayName => IsDraft ? "(new template)" : Row.cTenantSchedTemplateName;

   /// <summary>Start, weekly hours and whether a shift ends on the next day.</summary>
   public string Caption => IsDraft ? "" : $"{Row.cTenantSchedTemplateStart.ToString(@"hh\:mm", Culture)} · {Row.ccTenantSchedTemplateWeekHours.ToString("0.##", Culture)} h/week" +
      (Row.ccTenantSchedTemplateEndsNextDay ? " · ends next day" : "");

   /// <summary>Whether the template is deactivated.</summary>
   public bool IsInactive => !IsDraft && Row.cTenantSchedTemplateState < ta_TenantSchedTemplate.StateActive;
}

/// <summary>The hours of one weekday in the template editor.</summary>
public sealed class SchedTemplateDayItem : MvvmModelBase
{
   private readonly Action _changed;

   internal SchedTemplateDayItem(int weekday, decimal hours, int? minutes, Action changed) {
      Weekday = weekday; _changed = changed;
      Set<decimal?>(hours, propertyName: nameof(Hours));
      Set<decimal?>(minutes, propertyName: nameof(BreakMinutes));
   }

   /// <summary>1 Monday to 7 Sunday.</summary>
   public int Weekday { get; }

   /// <summary>Short weekday name.</summary>
   public string Caption => CultureInfo.GetCultureInfo("en-US").DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(Weekday % 7));

   /// <summary>Full weekday name.</summary>
   public string Name => CultureInfo.GetCultureInfo("en-US").DateTimeFormat.GetDayName((DayOfWeek)(Weekday % 7));

   /// <summary>Working hours; 0 is a day off.</summary>
   public decimal? Hours { get => Get<decimal?>(); set => Set(value, _ => _changed()); }

   /// <summary>Break in minutes for this day; empty uses the template break.</summary>
   public decimal? BreakMinutes { get => Get<decimal?>(); set => Set(value, _ => _changed()); }
}

/// <summary>State and commands of the Schedule templates page: the templates of one tenant.</summary>
public sealed class BusinessModuleManagerSchedTemplateVm : MvvmModelBase
{
   private readonly ITenantSchedTemplateServices? _service;
   private TenantSchedTemplateSet? _saved;
   private bool _refreshingRows;
   private bool _opening;

   /// <summary>Creates a view model for navigation; the service is taken from the application.</summary>
   public BusinessModuleManagerSchedTemplateVm() { RegisterCommands(); }

   /// <summary>Creates a view model with an explicit service, for tests and other hosts.</summary>
   public BusinessModuleManagerSchedTemplateVm(ITenantSchedTemplateServices service) { _service = service; RegisterCommands(); }

   private ITenantSchedTemplateServices? Service => _service ?? EmApp?.ServiceProvider.GetRequiredService<ITenantSchedTemplateServices>();

   private void RegisterCommands() {
      RegisterCommand(nameof(NewTemplateCommand), NewTemplateCommand, NewTemplateCommandAllowed);
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(DuplicateCommand), DuplicateCommand, DuplicateCommandAllowed);
      RegisterCommand(nameof(ToggleStateCommand), ToggleStateCommand, ToggleStateCommandAllowed);
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
   }

   /// <summary>Tenants available in the toolbar.</summary>
   public ObservableCollection<vi_Tenant> TenantChoices { get; } = [];

   /// <summary>The tenant being managed.</summary>
   public vi_Tenant? SelectedTenant {
      get => Get<vi_Tenant>();
      set {
         if (ReferenceEquals(value, SelectedTenant)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         Set(value); Rows.Clear(); Show(null, null); IsLoaded = false;
         Loading = ReloadAsync(); UpdateState();
      }
   }

   /// <summary>Most recent load, also awaitable by the owning screen.</summary>
   public Task Loading { get; private set; } = Task.CompletedTask;

   /// <summary>Host-supplied question asked before unsaved changes are discarded; <c>true</c> = discard.</summary>
   public Func<bool>? ConfirmDiscard { get; set; }

   /// <summary>Saved templates of the tenant, and a draft when one is open.</summary>
   public ObservableCollection<SchedTemplateItem> Rows { get; } = [];

   /// <summary>The template open in the editor. A saved one is read with its days when selected.</summary>
   public SchedTemplateItem? SelectedRow {
      get => Get<SchedTemplateItem>();
      set {
         if (_refreshingRows) return;
         if (_opening) { Set(value); return; }
         if (ReferenceEquals(value, SelectedRow)) return;
         if (IsBusy || !CanLeave()) { NotifyChanged(); return; }
         DropDraft(value);
         if (value is null or { IsDraft: true }) Show(value, null);
         else Loading = RunAsync("Loading template...", async () => { if (Service is { } service) Show(value, await service.GetMeta_TenantSchedTemplate(value.Row.cTenantSchedTemplateId)); });
      }
   }

   /// <summary>The seven weekdays, Monday first.</summary>
   public ObservableCollection<SchedTemplateDayItem> Days { get; } = [];

   /// <summary>Code of the edited template.</summary>
   public string EditCode { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Name of the edited template.</summary>
   public string EditName { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Start time as text, in 24-hour HH:mm.</summary>
   public string EditStart { get => Get<string>() ?? ""; set => Set(value, _ => UpdateState()); }

   /// <summary>Break of the template, in minutes.</summary>
   public decimal? EditBreak { get => Get<decimal?>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Display order of the edited template.</summary>
   public int EditOrder { get => Get<int>(); set => Set(value, _ => UpdateState()); }

   /// <summary>Note of the edited template.</summary>
   public string? EditNote { get => Get<string>(); set => Set(value, _ => UpdateState()); }

   /// <summary>The start time, when <see cref="EditStart"/> is valid.</summary>
   public TimeSpan? ParsedStart => TimeSpan.TryParseExact(EditStart, @"hh\:mm", CultureInfo.InvariantCulture, out var start) ? start : null;

   /// <summary>Days off, derived from the weekdays with 0 hours.</summary>
   public string DaysOffCaption => Days.Where(x => x.Hours is null or 0).Select(x => x.Name) is var names && names.Any() ? "Days off: " + string.Join(", ", names) : "No days off";

   /// <summary>Working hours of the whole week.</summary>
   public string WeekHoursCaption => Days.Sum(x => x.Hours ?? 0).ToString("0.##", CultureInfo.GetCultureInfo("en-US")) + " hours a week";

   /// <summary>Whether a template is open in the editor.</summary>
   public bool HasSelection => SelectedRow is not null;

   /// <summary>Whether nothing is open in the editor, to show the hint instead.</summary>
   public bool HasNoSelection => SelectedRow is null;

   /// <summary>Whether the editor shows a draft that has not been saved.</summary>
   public bool IsDraftSelected => SelectedRow?.IsDraft == true;

   /// <summary>Whether the editor differs from the saved template; a draft always counts as changed.</summary>
   public bool IsDirty => SelectedRow is { } item && (item.IsDraft || (_saved is { } saved && (EditCode != saved.Template.cTenantSchedTemplateCode || EditName != saved.Template.cTenantSchedTemplateName ||
      ParsedStart != saved.Template.cTenantSchedTemplateStart || EditBreak != saved.Template.cTenantSchedTemplateBreak || EditOrder != saved.Template.cTenantSchedTemplateOrder ||
      (EditNote ?? "") != (saved.Template.cTenantSchedTemplateNote ?? "") || Days.Any(x => saved.Days.FirstOrDefault(d => d.cTenantSchedTemplateDayWeekday == x.Weekday) is not { } day ||
         x.Hours != day.cTenantSchedTemplateDayHours || x.BreakMinutes != day.cTenantSchedTemplateDayBreak))));

   /// <summary>Caption of the state button: Deactivate for an active template, Activate for an inactive one.</summary>
   public string ToggleStateCaption => SelectedRow?.IsInactive == true ? "Activate" : "Deactivate";

   /// <summary>Whether the toolbar and list accept input.</summary>
   public bool CanBrowse => IsLoaded && SelectedTenant is not null && IsNotBusy;

   /// <summary>Whether the editor fields accept input.</summary>
   public bool CanEdit => CanBrowse && HasSelection;

   /// <summary>True after the latest tenant load succeeded.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }

   /// <summary>Result or server error shown below the toolbar.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Checks whether unsaved changes may be discarded.</summary>
   public bool CanLeave() => !IsDirty || ConfirmDiscard?.Invoke() != false;

   /// <summary>Restores the last saved values after the owning screen confirmed discard.</summary>
   public void DiscardChanges() {
      if (!IsDirty) return;
      if (IsDraftSelected) { DropDraft(null); Show(null, null); }
      else Show(SelectedRow, _saved);
   }

   /// <summary>Whether a new template can be started.</summary>
   public bool NewTemplateCommandAllowed() => CanBrowse;

   /// <summary>Starts a new template as a draft row, with the hours of the built-in office schedule.</summary>
   public void NewTemplateCommand() {
      if (!NewTemplateCommandAllowed() || !CanLeave()) return;
      DropDraft(null);
      var draft = new SchedTemplateItem(new vi_TenantSchedTemplate {
         cTenantId = SelectedTenant!.cTenantId, cTenantSchedTemplateStart = TenantSchedBuiltIn.Start, cTenantSchedTemplateBreak = TenantSchedBuiltIn.BreakMinutes,
         cTenantSchedTemplateState = ta_TenantSchedTemplate.StateActive
      });
      Rows.Add(draft);
      Show(draft, null);
      Status = "";
   }

   /// <summary>Whether the editor holds valid changes.</summary>
   public bool SaveCommandAllowed() => CanEdit && IsDirty && !string.IsNullOrWhiteSpace(EditCode) && !string.IsNullOrWhiteSpace(EditName) && ParsedStart is not null &&
      EditBreak is not null && Days.All(x => x.Hours is not null);

   /// <summary>Creates the draft or saves the changes of the selected template, then reloads the list.</summary>
   public Task SaveCommand() => RunAsync("Saving template...", async () => {
      if (SelectedRow is not { } item || Service is not { } service) return;
      var draft = item.IsDraft;
      var saved = await service.PostGetMeta_TenantSchedTemplate_Save(Build(item.Row, item.Row.cTenantSchedTemplateState));
      await Load(saved);
      Status = draft ? $"Template '{saved.Template.cTenantSchedTemplateCode}' created." : $"Template '{saved.Template.cTenantSchedTemplateCode}' saved.";
   });

   /// <summary>Whether the selected saved template, without unsaved changes, can be duplicated.</summary>
   public bool DuplicateCommandAllowed() => CanEdit && !IsDraftSelected && !IsDirty;

   /// <summary>Copies the selected template and opens the copy.</summary>
   public Task DuplicateCommand() => RunAsync("Duplicating template...", async () => {
      if (SelectedRow is not { IsDraft: false } item || Service is not { } service) return;
      var copy = await service.PostGetMeta_TenantSchedTemplate_Duplicate(item.Row.cTenantSchedTemplateId);
      await Load(copy);
      Status = $"Template '{copy.Template.cTenantSchedTemplateCode}' created.";
   });

   /// <summary>Whether the selected saved template, without unsaved changes, can be deactivated or activated.</summary>
   public bool ToggleStateCommandAllowed() => CanEdit && !IsDraftSelected && !IsDirty;

   /// <summary>Stops or resumes offering the selected template; settings that use it keep it.</summary>
   public Task ToggleStateCommand() => RunAsync("Saving template...", async () => {
      if (SelectedRow is not { IsDraft: false } item || Service is not { } service) return;
      var activate = item.IsInactive;
      var saved = await service.PostGetMeta_TenantSchedTemplate_Save(Build(item.Row, activate ? ta_TenantSchedTemplate.StateActive : ta_TenantSchedTemplate.StateInactive));
      await Load(saved);
      Status = activate ? $"Template '{saved.Template.cTenantSchedTemplateCode}' activated."
         : $"Template '{saved.Template.cTenantSchedTemplateCode}' deactivated. Settings that use it keep it.";
   });

   /// <summary>Whether the list can be read again.</summary>
   public bool RefreshCommandAllowed() => IsNotBusy && SelectedTenant is not null;

   /// <summary>Reads the templates again, asking first when the editor holds unsaved changes.</summary>
   public Task RefreshCommand() => CanLeave() ? ReloadAsync() : Task.CompletedTask;

   /// <summary>Reads the selected tenant again, keeping the selected template when it still exists.</summary>
   public Task ReloadAsync() => RunAsync("Loading templates...", async () => {
      if (Service is not { } service) return;
      var selected = SelectedRow is { IsDraft: false } item ? await service.GetMeta_TenantSchedTemplate(item.Row.cTenantSchedTemplateId) : null;
      await Load(selected);
      Status = "";
   });

   private TenantSchedTemplateSet Build(vi_TenantSchedTemplate row, int state) => new() {
      Template = new vi_TenantSchedTemplate {
         cTenantSchedTemplateId = row.cTenantSchedTemplateId, cTenantId = row.cTenantId, cTenantSchedTemplateCode = EditCode, cTenantSchedTemplateName = EditName,
         cTenantSchedTemplateStart = ParsedStart ?? row.cTenantSchedTemplateStart, cTenantSchedTemplateBreak = (int)(EditBreak ?? 0), cTenantSchedTemplateOrder = EditOrder,
         cTenantSchedTemplateNote = string.IsNullOrEmpty(EditNote) ? null : EditNote, cTenantSchedTemplateState = state, json_object = row.json_object, ustamp = row.ustamp
      },
      Days = [.. Days.Select(x => new ta_TenantSchedTemplateDay {
         cTenantSchedTemplateDayWeekday = x.Weekday, cTenantSchedTemplateDayHours = x.Hours ?? 0, cTenantSchedTemplateDayBreak = (int?)x.BreakMinutes
      })]
   };

   private async Task Load(TenantSchedTemplateSet? select) {
      if (Service is not { } service || SelectedTenant is null) return;
      var rows = await service.GetVi_TenantSchedTemplates(SelectedTenant.cTenantId);
      _refreshingRows = true;
      try {
         Rows.Clear();
         foreach (var row in rows) Rows.Add(new SchedTemplateItem(row));
      }
      finally { _refreshingRows = false; }
      var item = select is null ? null : Rows.FirstOrDefault(x => string.Equals(x.Row.cTenantSchedTemplateId, select.Template.cTenantSchedTemplateId, StringComparison.OrdinalIgnoreCase));
      Show(item, item is null ? null : select);
      IsLoaded = true;
   }

   // Removes a draft row other than the one being opened.
   private void DropDraft(SchedTemplateItem? keep) {
      if (Rows.FirstOrDefault(x => x.IsDraft) is not { } draft || ReferenceEquals(draft, keep)) return;
      _refreshingRows = true;
      try { Rows.Remove(draft); }
      finally { _refreshingRows = false; }
   }

   private void Show(SchedTemplateItem? item, TenantSchedTemplateSet? set) {
      _opening = true;
      try {
         _saved = set;
         SelectedRow = item;
         var row = set?.Template ?? item?.Row;
         EditCode = row?.cTenantSchedTemplateCode ?? ""; EditName = row?.cTenantSchedTemplateName ?? "";
         EditStart = row?.cTenantSchedTemplateStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "";
         EditBreak = row?.cTenantSchedTemplateBreak; EditOrder = row?.cTenantSchedTemplateOrder ?? -1; EditNote = row?.cTenantSchedTemplateNote;
         Days.Clear();
         if (item is not null)
            for (var weekday = 1; weekday <= 7; weekday++) {
               var day = set?.Days.FirstOrDefault(x => x.cTenantSchedTemplateDayWeekday == weekday);
               Days.Add(new SchedTemplateDayItem(weekday, day?.cTenantSchedTemplateDayHours ?? TenantSchedBuiltIn.HoursOf(weekday), day?.cTenantSchedTemplateDayBreak, UpdateState));
            }
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
      foreach (var name in (string[])[nameof(HasSelection), nameof(HasNoSelection), nameof(IsDraftSelected), nameof(IsDirty), nameof(ToggleStateCaption), nameof(CanBrowse), nameof(CanEdit),
         nameof(DaysOffCaption), nameof(WeekHoursCaption)])
         NotifyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
}
