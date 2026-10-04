namespace Em.Ui.Core.Shared;

/// <summary>
/// Penerima keterangan awal kartu informasi dari deklarasi modul. Dapat diimplementasikan oleh
/// control atau view model kartu yang juga memakai <see cref="IApprovalPanel"/>.
/// </summary>
public interface IApprovalPanelInput
{
   /// <summary>Keterangan yang dihasilkan fungsi input kartu untuk request yang sedang dibuka.</summary>
   object? Input { get; set; }
}
