namespace Em.Ui.Core.Shared;

/// <summary>
/// Receiver of the initial info of an info card from the module's declaration. May be implemented by
/// the card's control or view model that also uses <see cref="IApprovalPanel"/>.
/// </summary>
public interface IApprovalPanelInput
{
   /// <summary>The info produced by the card's input function for the request being opened.</summary>
   object? Input { get; set; }
}
