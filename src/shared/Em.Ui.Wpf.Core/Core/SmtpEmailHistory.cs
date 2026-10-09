using System.Net.Mail;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Core;

/// <summary>SMTP test address history stored under the host application's base Registry key.</summary>
public sealed class SmtpEmailHistory(Func<RegistryKey> baseKey)
{
   /// <summary>Maximum number of addresses retained separately for sender and recipient.</summary>
   public const int Limit = 20;
   private const string SubKey = "SmtpManager";
   /// <summary>Creates history storage using the host application's Registry root.</summary>
   public SmtpEmailHistory(EmApp app) : this(() => app.BaseRegKey) { }

   /// <summary>Reads normalized sender and recipient history, newest first.</summary>
   public (string[] From, string[] To) Read() {
      using var root = baseKey();
      using var key = root.OpenSubKey(SubKey);
      return (Normalize(key?.GetValue("FromHistory") as string[] ?? []),
         Normalize(key?.GetValue("ToHistory") as string[] ?? []));
   }

   /// <summary>Remembers the addresses from a successful test without storing SMTP credentials.</summary>
   public void Remember(string from, string to) {
      using var root = baseKey();
      using var key = root.CreateSubKey(SubKey);
      key.SetValue("FromHistory", Normalize([from, .. key.GetValue("FromHistory") as string[] ?? []]), RegistryValueKind.MultiString);
      key.SetValue("ToHistory", Normalize([to, .. key.GetValue("ToHistory") as string[] ?? []]), RegistryValueKind.MultiString);
   }

   /// <summary>Deletes only this feature's address history values.</summary>
   public void Clear() {
      using var root = baseKey();
      using var key = root.OpenSubKey(SubKey, writable: true);
      key?.DeleteValue("FromHistory", false);
      key?.DeleteValue("ToHistory", false);
   }

   private static string[] Normalize(IEnumerable<string> values) => values
      .Select(value => value?.Trim() ?? "")
      .Where(value => value.Length is > 0 and <= 320 && value.Contains('@') &&
         !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) &&
         MailAddress.TryCreate(value, out var address) && address.Address == value)
      .Distinct(StringComparer.OrdinalIgnoreCase).Take(Limit).ToArray();
}
