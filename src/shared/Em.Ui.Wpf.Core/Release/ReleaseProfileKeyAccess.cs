using System.Security.Cryptography;
using System.Windows;
using Em.Ui.Wpf.Dialogs;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Permintaan password pada thread UI, digunakan Sync dan ekspor key Settings.</summary>
   internal static class ReleaseProfileKeyAccess
   {
      public static string? AcquirePassword(ReleaseProfile profile, ReleaseProfileStore store, ReleaseSigningSecrets secrets, Window? owner, string okCaption) {
         for (var attempt = 0; attempt < 3; attempt++) {
            var plaintext = profile.Signing.PasswordStorage == ReleasePasswordStorage.Plaintext;
            var password = plaintext ? profile.Signing.Password : secrets.Get(profile.Id, profile.Signing.Thumbprint);
            PasswordInputDialog? dialog = null;
            if (password is null && !plaintext) {
               dialog = new PasswordInputDialog("Signing Key Password", $"Password of the key file in profile '{profile.Name}'.", okCaption: okCaption, showRemember: true) { Owner = owner };
               if (dialog.ShowDialog() != true) return null;
               password = dialog.Vm.Password;
            }
            try {
               using var certificate = SigningCertificates.ValidatePfx(store.KeyFilePath(profile.Id), password ?? "");
               if (!string.Equals(certificate.Thumbprint, profile.Signing.Thumbprint, StringComparison.OrdinalIgnoreCase))
                  throw new CryptographicException("The key file does not match signing.cer. Import the key again in Settings.");
               if (dialog is not null) secrets.Put(profile.Id, certificate.Thumbprint, password!, dialog.Vm.Remember);
               return password ?? "";
            }
            catch (CryptographicException) {
               if (plaintext) {
                  owner?.ShowMboxError("The password saved in profile.json is wrong, or the key file does not match its certificate. Fix it in Settings.");
                  return null;
               }
               secrets.Forget(profile.Id);
               owner?.ShowMboxError("The key file could not be opened with this password, or does not match signing.cer. Try again or import it in Settings.");
            }
            catch (Exception x) {
               owner?.ShowMboxError(x.Message);
               return null;
            }
         }
         owner?.ShowMboxError("The signing key could not be opened after three attempts.");
         return null;
      }
   }
}
