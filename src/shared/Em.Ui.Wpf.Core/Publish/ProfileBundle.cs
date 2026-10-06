using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Em.Ui.Wpf.Publish;

/// <summary>
/// Berkas profil yang dienkripsi dengan passphrase, dipakai untuk memindahkan profil beserta secret-nya ke PC
/// lain. Secret yang disimpan DPAPI tidak bisa dibawa karena terkunci ke akun Windows asal; bundle ini tidak
/// bergantung pada akun mana pun, hanya pada passphrase. Isinya JSON profil yang sama dengan export biasa.
/// </summary>
public static class ProfileBundle {
 /// <summary>Ekstensi bundle profil Container Manager.</summary>
 public const string ContainerExtension=".ctnconfig";
 /// <summary>Ekstensi bundle profil NuGet Manager.</summary>
 public const string NuGetExtension=".nugetconfig";
 /// <summary>Panjang minimum passphrase.</summary>
 public const int MinPassphraseLength=8;

 private const string Format="em-publish-profile";
 private const int Version=1;
 private const int Iterations=600_000;
 private const int MaxIterations=5_000_000;
 private static readonly byte[] Aad=Encoding.ASCII.GetBytes(Format+":"+Version);

 /// <summary>Ekstensi bundle untuk jenis profil <paramref name="kind"/>.</summary>
 public static string Extension(PublishKind kind)=>kind==PublishKind.Container?ContainerExtension:NuGetExtension;

 /// <summary>Apakah <paramref name="file"/> bundle terenkripsi, dilihat dari ekstensinya.</summary>
 public static bool IsBundleFile(string file) {
  var extension=Path.GetExtension(file);
  return extension.Equals(ContainerExtension,StringComparison.OrdinalIgnoreCase)||extension.Equals(NuGetExtension,StringComparison.OrdinalIgnoreCase);
 }

 /// <summary>Mengenkripsi <paramref name="json"/> dengan AES-256-GCM; kuncinya diturunkan dari passphrase (PBKDF2-SHA256).</summary>
 public static string Encrypt(string json,string passphrase) {
  if(string.IsNullOrEmpty(passphrase)||passphrase.Length<MinPassphraseLength)throw new InvalidDataException($"Passphrase must be at least {MinPassphraseLength} characters.");
  var salt=RandomNumberGenerator.GetBytes(16);var nonce=RandomNumberGenerator.GetBytes(12);
  var key=Rfc2898DeriveBytes.Pbkdf2(passphrase,salt,Iterations,HashAlgorithmName.SHA256,32);
  var plain=Encoding.UTF8.GetBytes(json);var cipher=new byte[plain.Length];var tag=new byte[16];
  try { using var aes=new AesGcm(key,tag.Length);aes.Encrypt(nonce,plain,cipher,tag,Aad); }
  finally { CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(plain); }
  return ProfileJson.Write(new Envelope(Format,Version,"PBKDF2-SHA256",Iterations,Convert.ToBase64String(salt),Convert.ToBase64String(nonce),Convert.ToBase64String(tag),Convert.ToBase64String(cipher)));
 }

 /// <summary>Membuka bundle. Passphrase salah dan berkas rusak sengaja memberi pesan yang sama.</summary>
 /// <exception cref="InvalidDataException">Bukan bundle yang dikenali, passphrase salah, atau isinya rusak.</exception>
 public static string Decrypt(string envelope,string passphrase) {
  Envelope e;
  try { e=JsonSerializer.Deserialize<Envelope>(envelope,ProfileJson.Options)??throw new InvalidDataException("Not an encrypted profile file."); }
  catch(JsonException) { throw new InvalidDataException("Not an encrypted profile file."); }
  if(e.Format!=Format||e.Version!=Version||e.Kdf!="PBKDF2-SHA256"||e.Iterations<1||e.Iterations>MaxIterations)throw new InvalidDataException("Unsupported encrypted profile file.");
  if(string.IsNullOrEmpty(passphrase))throw new InvalidDataException("This file is encrypted. A passphrase is required.");
  byte[] key=[];byte[] plain=[];
  try {
   var salt=Convert.FromBase64String(e.Salt);var nonce=Convert.FromBase64String(e.Nonce);var tag=Convert.FromBase64String(e.Tag);var cipher=Convert.FromBase64String(e.Data);
   key=Rfc2898DeriveBytes.Pbkdf2(passphrase,salt,e.Iterations,HashAlgorithmName.SHA256,32);plain=new byte[cipher.Length];
   using var aes=new AesGcm(key,tag.Length);aes.Decrypt(nonce,cipher,tag,plain,Aad);
   return Encoding.UTF8.GetString(plain);
  }
  catch(Exception ex) when(ex is CryptographicException or FormatException or ArgumentException) { throw new InvalidDataException("Wrong passphrase, or the file is damaged."); }
  finally { CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(plain); }
 }

 private sealed record Envelope(string Format,int Version,string Kdf,int Iterations,string Salt,string Nonce,string Tag,string Data);
}
