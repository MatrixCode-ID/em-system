using Microsoft.Maui.Storage;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Where the application's settings are stored on the device - the MAUI counterpart of the Registry
   /// subkey used by the desktop client. Its content is ordinary settings (the last profile name, the theme
   /// choice, the list of connection profiles); secrets are not kept here, but in
   /// <see cref="SecureStorageSessionStorage"/>.
   /// </summary>
   /// <remarks>
   /// Every key is prefixed with the application name so two applications built from the same engine do not
   /// overwrite each other's settings - exactly the reason the desktop client keeps its own under one
   /// subkey named after the application.
   /// </remarks>
   public sealed class AppSettings(string applicationName)
   {
      private readonly IPreferences _preferences = Preferences.Default;

      private string KeyOf(string name) => $"{applicationName}:{name}";

      /// <summary>Reads a text value, or <c>null</c> when it was never stored.</summary>
      /// <param name="name">The name of the setting.</param>
      public string? GetString(string name) => _preferences.Get<string?>(KeyOf(name), null);

      /// <summary>
      /// Stores a text value. An empty or <c>null</c> value removes the setting, rather than storing empty
      /// text - so "never filled in" and "deliberately emptied" need not be told apart by the reader.
      /// </summary>
      /// <param name="name">The name of the setting.</param>
      /// <param name="value">The value to store, or <c>null</c> to remove it.</param>
      public void SetString(string name, string? value) {
         if (string.IsNullOrWhiteSpace(value)) {
            _preferences.Remove(KeyOf(name));
            return;
         }

         _preferences.Set(KeyOf(name), value);
      }

      /// <summary>Reads a true/false value, or <paramref name="defaultValue"/> when it was never stored.</summary>
      /// <param name="name">The name of the setting.</param>
      /// <param name="defaultValue">The value used when the setting does not exist.</param>
      public bool GetBool(string name, bool defaultValue = false) => _preferences.Get(KeyOf(name), defaultValue);

      /// <summary>Stores a true/false value.</summary>
      /// <param name="name">The name of the setting.</param>
      /// <param name="value">The value to store.</param>
      public void SetBool(string name, bool value) => _preferences.Set(KeyOf(name), value);
   }
}
