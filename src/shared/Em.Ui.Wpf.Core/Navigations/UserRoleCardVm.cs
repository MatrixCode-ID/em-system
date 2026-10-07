using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// One role card on the Roles tab of the user editor: whether the open user holds that role, and for
   /// which period. Nothing here talks to the server - the card only remembers what is stored (the
   /// baseline) next to what is on screen, and the editor sends the difference: at once for a stored
   /// user, with Save for a new one.
   /// </summary>
   public class UserRoleCardVm : NotifyPropertyBase
   {
      // On while the card itself moves its values back to the baseline, so a revert or an accept does
      // not report itself as a change the user made.
      private bool _resetting;

      /// <summary>Creates a card for <paramref name="role"/>.</summary>
      /// <param name="role">The role this card stands for.</param>
      /// <param name="assignment">The stored assignment of this role to the user, or <c>null</c> when the user does not hold it.</param>
      /// <param name="now">The time the period state (scheduled, expired) is measured against.</param>
      public UserRoleCardVm(Role role, ta_UserRole? assignment, DateTime now) {
         ArgumentNullException.ThrowIfNull(role);
         Role = role;
         Now = now;
         OriginalRow = assignment;
         OriginalIsAssigned = assignment is not null;
         OriginalStart = assignment?.cUserRoleStart;
         OriginalExpiry = assignment?.cUserRoleExpiry;
         ResetToBaseline();
      }

      /// <summary>Raised every time a value the user can change on this card has changed.</summary>
      public event EventHandler? Changed;

      #region Role

      /// <summary>The role this card stands for.</summary>
      public Role Role { get; }

      /// <summary>Name of the role.</summary>
      public string RoleName => Role.cRoleName;

      /// <summary>Description of the role, or an empty text when it has none.</summary>
      public string RoleDescription => Role.cRoleDescription ?? string.Empty;

      /// <summary>Whether the role has a description worth showing.</summary>
      public bool HasDescription => !string.IsNullOrWhiteSpace(Role.cRoleDescription);

      /// <summary>
      /// Whether the role is not active. Such a role may still be given, but it grants nothing while it stays
      /// inactive - the server only counts active roles when it reads a user's permissions.
      /// </summary>
      public bool IsRoleInactive => Role.cRoleState != RoleState.Active;

      /// <summary>
      /// Number of permissions the role carries, or <c>null</c> when it is not known. Filled by the editor
      /// from one request for the whole list, never by one request per card.
      /// </summary>
      public int? ClaimCount {
         get => Get<int?>();
         set => Set(value, _ => {
            NotifyChanged(nameof(HasClaimCount));
            NotifyChanged(nameof(ClaimCountCaption));
         });
      }

      /// <summary>Whether <see cref="ClaimCountCaption"/> has anything to say.</summary>
      public bool HasClaimCount => ClaimCount is not null;

      /// <summary>Caption of the permission count, e.g. "12 permissions".</summary>
      public string ClaimCountCaption => ClaimCount switch {
         null => string.Empty,
         1 => "1 permission",
         { } count => $"{count} permissions"
      };

      /// <summary>Whether the card matches the search text of the Roles tab, so it is shown.</summary>
      public bool IsSearchMatch {
         get => Get(true);
         private set => Set(value);
      }

      /// <summary>
      /// Shows or hides the card for <paramref name="search"/>: an empty search shows every card, otherwise
      /// the role name or description must contain it, ignoring case. A hidden card keeps its changes.
      /// </summary>
      /// <param name="search">The text typed in the search box of the Roles tab.</param>
      /// <returns>Whether the card is shown.</returns>
      public bool ApplySearch(string? search) {
         var text = search?.Trim();
         IsSearchMatch = string.IsNullOrEmpty(text)
                         || RoleName.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                         || RoleDescription.Contains(text, StringComparison.CurrentCultureIgnoreCase);
         return IsSearchMatch;
      }

      #endregion

      #region Baseline

      /// <summary>The time the period state is measured against.</summary>
      public DateTime Now { get; private set; }

      /// <summary>Whether the stored data says the user holds this role.</summary>
      public bool OriginalIsAssigned { get; private set; }

      /// <summary>The stored start of the assignment.</summary>
      public DateTime? OriginalStart { get; private set; }

      /// <summary>The stored expiry of the assignment.</summary>
      public DateTime? OriginalExpiry { get; private set; }

      /// <summary>
      /// The stored assignment row, kept so a period change can be sent over it - its creation stamp says
      /// when the role was first given, and rebuilding the row would replace that with now.
      /// </summary>
      public ta_UserRole? OriginalRow { get; private set; }

      #endregion

      #region Working values

      /// <summary>Whether the user holds this role, as switched on screen.</summary>
      public bool IsAssigned {
         get => Get<bool>();
         set => Set(value, _ => OnWorkingValueChanged());
      }

      /// <summary>The start date picked on screen; <c>null</c> means valid from the moment it is given.</summary>
      public DateTime? StartDate {
         get => Get<DateTime?>();
         set => Set(value, _ => OnWorkingValueChanged());
      }

      /// <summary>The expiry date picked on screen; <c>null</c> means no end.</summary>
      public DateTime? ExpiryDate {
         get => Get<DateTime?>();
         set => Set(value, _ => OnWorkingValueChanged());
      }

      /// <summary>
      /// Whether the card may be changed at all. Off for a system account, while the editor or this card is
      /// saving, and while the role list is loading.
      /// </summary>
      public bool IsEditable {
         get => Get(true);
         set => Set(value, _ => NotifyChanged(nameof(IsPeriodEnabled)));
      }

      /// <summary>
      /// Whether the change on this card is being sent to the server. The card is locked meanwhile, so what
      /// is accepted as stored afterwards is exactly what was sent.
      /// </summary>
      public bool IsSaving {
         get => Get<bool>();
         internal set => Set(value);
      }

      /// <summary>Whether the period pickers may be changed: only for a role that is switched on.</summary>
      public bool IsPeriodEnabled => IsAssigned && IsEditable;

      /// <summary>Whether both dates are filled in and the expiry falls before the start.</summary>
      public bool HasPeriodError =>
         IsAssigned && StartDate is { } start && ExpiryDate is { } expiry && expiry.Date < start.Date;

      /// <summary>
      /// Whether the card differs from what is stored: the role was switched, or it stays on and one of its
      /// dates moved to another day. Compared per day, not per second, so opening and saving a card whose
      /// stored times are not midnight changes nothing.
      /// </summary>
      public bool IsChanged =>
         IsAssigned != OriginalIsAssigned
         || (IsAssigned && (!SameDate(StartDate, OriginalStart) || !SameDate(ExpiryDate, OriginalExpiry)));

      /// <summary>
      /// The start that is sent to the server: the stored value while its day is unchanged, otherwise the
      /// start of the picked day.
      /// </summary>
      public DateTime? EffectiveStart => SameDate(StartDate, OriginalStart) ? OriginalStart : ToStart(StartDate);

      /// <summary>
      /// The expiry that is sent to the server: the stored value while its day is unchanged, otherwise the
      /// last second of the picked day.
      /// </summary>
      public DateTime? EffectiveExpiry => SameDate(ExpiryDate, OriginalExpiry) ? OriginalExpiry : ToExpiry(ExpiryDate);

      /// <summary>Whether the assignment on screen only starts after <see cref="Now"/>.</summary>
      public bool IsScheduled => IsAssigned && EffectiveStart is { } start && start > Now;

      /// <summary>Whether the assignment on screen has already ended at <see cref="Now"/>.</summary>
      public bool IsExpired => IsAssigned && !IsScheduled && EffectiveExpiry is { } expiry && expiry < Now;

      /// <summary>Caption of the period chip: <c>SCHEDULED</c>, <c>EXPIRED</c>, or empty.</summary>
      public string PeriodStateCaption => IsScheduled ? "SCHEDULED" : IsExpired ? "EXPIRED" : string.Empty;

      #endregion

      #region Methods

      /// <summary>
      /// Makes what is on screen the new baseline, after the editor has stored it.
      /// </summary>
      /// <param name="sentRow">
      /// The row that was just sent for this role (a new or rescheduled assignment), or <c>null</c> when
      /// nothing was sent for it.
      /// </param>
      /// <param name="now">The new time the period state is measured against.</param>
      public void Accept(ta_UserRole? sentRow, DateTime now) {
         var start = EffectiveStart;
         var expiry = EffectiveExpiry;

         Now = now;
         OriginalIsAssigned = IsAssigned;
         OriginalStart = IsAssigned ? start : null;
         OriginalExpiry = IsAssigned ? expiry : null;
         if (!IsAssigned) OriginalRow = null;
         else if (sentRow is not null) OriginalRow = sentRow;

         ResetToBaseline();
      }

      /// <summary>Puts the card back to what is stored.</summary>
      public void Revert() => ResetToBaseline();

      /// <summary>Builds the assignment row this card would send for <paramref name="cUserId"/>.</summary>
      internal ta_UserRole ToRow(string cUserId, DateTime stamp) {
         var original = OriginalRow;
         return new ta_UserRole {
            cUserId = cUserId,
            cRoleId = Role.cRoleId,
            cUserRoleStart = EffectiveStart,
            cUserRoleExpiry = EffectiveExpiry,
            ustamp = stamp,
            // A rescheduled assignment keeps the moment it was first given.
            datestamp = original is not null && OriginalIsAssigned ? original.datestamp : stamp
         };
      }

      private void ResetToBaseline() {
         _resetting = true;
         try {
            IsAssigned = OriginalIsAssigned;
            StartDate = OriginalStart;
            ExpiryDate = OriginalExpiry;
         }
         finally {
            _resetting = false;
         }

         RaiseDerived();
      }

      private void OnWorkingValueChanged() {
         if (_resetting) return;

         RaiseDerived();
         Changed?.Invoke(this, EventArgs.Empty);
      }

      private void RaiseDerived() {
         NotifyChanged(nameof(IsPeriodEnabled));
         NotifyChanged(nameof(HasPeriodError));
         NotifyChanged(nameof(IsChanged));
         NotifyChanged(nameof(EffectiveStart));
         NotifyChanged(nameof(EffectiveExpiry));
         NotifyChanged(nameof(IsScheduled));
         NotifyChanged(nameof(IsExpired));
         NotifyChanged(nameof(PeriodStateCaption));
      }

      #endregion

      #region Statics

      /// <summary>The stored form of a picked start date: the start of that day.</summary>
      public static DateTime? ToStart(DateTime? date) => date?.Date;

      /// <summary>The stored form of a picked expiry date: the last second of that day.</summary>
      public static DateTime? ToExpiry(DateTime? date) => date?.Date.AddDays(1).AddSeconds(-1);

      /// <summary>Whether two optional dates fall on the same day, both empty included.</summary>
      public static bool SameDate(DateTime? a, DateTime? b) => a?.Date == b?.Date;

      /// <summary>
      /// Works out what has to be sent to the server so the user's roles match the cards: assignments to
      /// remove, assignments to add, and assignments whose period moved.
      /// </summary>
      /// <param name="cUserId">The user the assignments belong to. For a new user, its id after it was saved.</param>
      /// <param name="cards">Every card on the tab.</param>
      /// <param name="stamp">The server time used to stamp new and changed rows.</param>
      public static UserRoleChanges BuildChanges(string cUserId, IEnumerable<UserRoleCardVm> cards, DateTime stamp) {
         ArgumentException.ThrowIfNullOrWhiteSpace(cUserId);
         ArgumentNullException.ThrowIfNull(cards);

         var removed = new List<ta_UserRole>();
         var added = new List<ta_UserRole>();
         var rescheduled = new List<ta_UserRole>();

         foreach (var card in cards) {
            if (!card.IsChanged) continue;

            if (card.OriginalIsAssigned && !card.IsAssigned)
               removed.Add(new ta_UserRole { cUserId = cUserId, cRoleId = card.Role.cRoleId });
            else if (!card.OriginalIsAssigned && card.IsAssigned)
               added.Add(card.ToRow(cUserId, stamp));
            else
               rescheduled.Add(card.ToRow(cUserId, stamp));
         }

         return new UserRoleChanges([.. removed], [.. added], [.. rescheduled]);
      }

      #endregion
   }

   /// <summary>
   /// The difference between the roles a user holds and the roles switched on in the editor, ready to be
   /// sent in the same order the role manager sends its own: removals, additions, then period changes.
   /// </summary>
   /// <param name="Removed">Assignments to remove; only the user and role keys are filled.</param>
   /// <param name="Added">New assignments.</param>
   /// <param name="Rescheduled">Existing assignments whose period changed.</param>
   public sealed record UserRoleChanges(ta_UserRole[] Removed, ta_UserRole[] Added, ta_UserRole[] Rescheduled)
   {
      /// <summary>Whether there is nothing to send.</summary>
      public bool IsEmpty => Removed.Length == 0 && Added.Length == 0 && Rescheduled.Length == 0;

      /// <summary>The row sent for <paramref name="cRoleId"/> as an addition or a period change, if any.</summary>
      public ta_UserRole? SentRowFor(string cRoleId) =>
         Added.FirstOrDefault(r => r.cRoleId == cRoleId) ?? Rescheduled.FirstOrDefault(r => r.cRoleId == cRoleId);
   }
}
