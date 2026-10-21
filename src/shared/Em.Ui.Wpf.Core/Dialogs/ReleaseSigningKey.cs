namespace Em.Ui.Wpf.Dialogs;
/// <summary>Public display identity of the signing PEM in the product profile.</summary>
public sealed class ReleaseSigningKey {
   /// <summary>First sixteen lowercase hex characters of SHA-256 of public SPKI DER.</summary>
   public required string KeyId { get; init; }
}
