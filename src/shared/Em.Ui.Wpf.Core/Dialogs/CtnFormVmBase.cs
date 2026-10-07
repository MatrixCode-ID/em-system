using FontAwesome6;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Base of the view models of Container Manager form-shaped dialogs (root, container, robot): the
   /// title, a name that can only be filled in when creating, a description, and the active status. The
   /// name check is light and only exists so the confirm button does not turn on for a name that is clearly
   /// wrong; the server remains the authority.
   /// </summary>
   public abstract class CtnFormVmBase : MvvmModelBase
   {
      /// <summary>Creates the view model and registers the confirm command.</summary>
      protected CtnFormVmBase() {
         RegisterCommand(nameof(OkCommand), OkCommand, OkCommandAllowed);
      }

      /// <summary>Raised when the dialog is about to close; <c>true</c> when the user confirmed.</summary>
      public event Action<bool>? RequestClose;

      /// <summary>The dialog title.</summary>
      public string Title {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>Explanatory sentence below the title.</summary>
      public string Caption {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>Text of the confirm button.</summary>
      public string OkCaption {
         get => Get<string>() ?? "OK";
         set => Set(value);
      }

      /// <summary>Ikon di banner dialog.</summary>
      public EFontAwesomeIcon Icon {
         get => Get<EFontAwesomeIcon>();
         set => Set(value);
      }

      /// <summary>Label of the name field.</summary>
      public string NameLabel {
         get => Get<string>() ?? "Name";
         set => Set(value);
      }

      /// <summary>Faint hint text in the name field.</summary>
      public string NamePlaceholder {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>The name rule, shown below the field.</summary>
      public string NameHelp {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>
      /// <c>true</c> when the name may be typed (creating new); <c>false</c> shows it as read-only text,
      /// because the name cannot be changed after it is created.
      /// </summary>
      public bool IsNameEditable {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(IsNameReadOnly)));
      }

      /// <summary>The opposite of <see cref="IsNameEditable"/>, for binding.</summary>
      public bool IsNameReadOnly => !IsNameEditable;

      /// <summary>Whether the name section is shown at all.</summary>
      public bool ShowName {
         get => Get<bool>(true);
         set => Set(value);
      }

      /// <summary>Whether the description field is shown.</summary>
      public bool ShowDescription {
         get => Get<bool>(true);
         set => Set(value);
      }

      /// <summary>Whether the Active choice is shown (only when editing).</summary>
      public bool ShowActive {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>The name as typed, as-is. Use <see cref="NameResult"/> for its final result.</summary>
      public string Name {
         get => Get<string>() ?? "";
         set => Set(value, _ => OnInputChanged());
      }

      /// <summary>The name without spaces at both ends.</summary>
      public string NameResult => Name.Trim();

      /// <summary>The description as typed, as-is.</summary>
      public string Description {
         get => Get<string>() ?? "";
         set => Set(value, _ => OnInputChanged());
      }

      /// <summary>The description without spaces at both ends; <c>null</c> when empty.</summary>
      public string? DescriptionResult => string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

      /// <summary>The active status.</summary>
      public bool IsActive {
         get => Get<bool>(true);
         set => Set(value);
      }

      /// <summary>Error message of the name; empty while the name has not been typed or is valid.</summary>
      public string NameError =>
         ShowName && IsNameEditable && NameResult.Length > 0 ? ValidateName(NameResult) ?? "" : "";

      /// <summary>Error message of the description; empty when its length is reasonable.</summary>
      public string DescriptionError =>
         ShowDescription && Description.Trim().Length > CtnInput.MaxDescription
            ? $"The description may be at most {CtnInput.MaxDescription} characters."
            : "";

      /// <summary>Called by the dialog after filling in the initial values, so the confirm button is evaluated again.</summary>
      internal void Refresh() => OnInputChanged();

      /// <summary>Closes the dialog with the result <c>true</c>.</summary>
      public void OkCommand() => RequestClose?.Invoke(true);

      /// <summary>Only when the name (if typed) and the description are valid, and the other fields of the derived class are valid.</summary>
      public virtual bool OkCommandAllowed() {
         if (ShowName && IsNameEditable && (NameResult.Length == 0 || ValidateName(NameResult) is not null)) return false;

         return DescriptionError.Length == 0;
      }

      /// <summary>An answer of <c>null</c> means the name is valid; otherwise a sentence explaining why.</summary>
      protected abstract string? ValidateName(string name);

      /// <summary>Called whenever an input changes: updates the error messages and the confirm button.</summary>
      protected virtual void OnInputChanged() {
         NotifyChanged(nameof(NameError));
         NotifyChanged(nameof(DescriptionError));
         Commands[nameof(OkCommand)]?.RaiseCanExecuteChanged();
      }
   }
}
