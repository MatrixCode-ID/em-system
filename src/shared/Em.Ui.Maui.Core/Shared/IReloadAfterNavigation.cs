namespace Em.Ui.Maui.Shared;

/// <summary>
/// Marks a navigation body that validates and configures in OnNavigatingIn, then loads
/// through OnReloadRequested after mounting, including when returning to an existing entry.
/// </summary>
public interface IReloadAfterNavigation;
