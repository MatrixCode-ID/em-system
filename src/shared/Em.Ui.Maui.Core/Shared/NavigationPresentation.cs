using Em.Shared;

namespace Em.Ui.Maui.Shared;

/// <summary>Opt-in display metadata for one MAUI navigation body; defaults preserve the shell's normal chrome.</summary>
public sealed class NavigationPresentation : NotifyPropertyBase
{
   /// <summary>Optional display title; null uses the entry title and never changes its unique key.</summary>
   public string? Title { get => Get<string?>(); set => Set(value); }

   /// <summary>Optional context line; null uses the registered subtitle and an empty string hides it.</summary>
   public string? Subtitle { get => Get<string?>(); set => Set(value); }

   /// <summary>Requests a compact app bar with a minimum 48-unit height and scalable text.</summary>
   public bool UseCompactAppBar { get => Get(false); set => Set(value); }

   /// <summary>Temporarily hides workspace tabs, for example while the body's IME or modal sheet is open.</summary>
   public bool HideBottomNavigation { get => Get(false); set => Set(value); }
}
