using System.Text.Json.Serialization;

namespace Em.Shared
{
   /// <summary>
   /// Pure declaration of one claim: its owning module and its name. It holds no answer ("does this user
   /// have the right") - that belongs to the grants in the database, not to this declaration.
   /// </summary>
   public record ClaimAction
   {
      /// <summary>
      /// Separator between module name and claim name inside <see cref="Key"/>. Chosen because it is not
      /// valid in a C# identifier, so it never appears by accident in either part - unlike a dot (already
      /// used by module names such as <c>core.contact</c>) or a hyphen (natural in names such as
      /// <c>Read-Only</c>).
      /// </summary>
      public const char Separator = ':';

      // Width of column cUserClaimName in ta_UserClaim.
      private const int MaxKeyLength = 255;

      /// <summary>Declares claim <paramref name="name"/> in the module of service <typeparamref name="T"/>.</summary>
      /// <typeparam name="T">Service whose <see cref="ModuleAttribute"/> names the module.</typeparam>
      /// <param name="name">Claim name, without <see cref="Separator"/>.</param>
      /// <exception cref="ArgumentException">The name is empty, contains the separator, or the key is too long.</exception>
      public static ClaimAction Create<T>(string name) where T : IServices {
         var moduleName = ModuleAttribute.ResolveName(typeof(T));

         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Claim name must not be empty.", nameof(name));
         }

         if (name.Contains(Separator)) {
            throw new ArgumentException(
               $"Claim name '{name}' must not contain '{Separator}'.", nameof(name));
         }

         if (moduleName.Contains(Separator)) {
            throw new ArgumentException(
               $"Module name '{moduleName}' must not contain '{Separator}'.", nameof(name));
         }

         var result = new ClaimAction {
            Name = name,
            ModuleName = moduleName,
         };

         if (result.Key.Length > MaxKeyLength) {
            throw new ArgumentException(
               $"Claim key '{result.Key}' is {result.Key.Length} characters, which exceeds the maximum of {MaxKeyLength}.",
               nameof(name));
         }

         return result;
      }

      /// <summary>
      /// Counterpart of <see cref="Key"/>: splits one key string back into <see cref="ModuleName"/> and
      /// <see cref="Name"/>. Used by the server when reading <c>ta_UserClaim</c> rows - the client never
      /// builds or splits keys itself.
      /// </summary>
      /// <param name="key">
      /// Key of the form <c>module{Separator}name</c>. A key without any <see cref="Separator"/> means an
      /// old or broken row, and is returned as is with an empty module instead of throwing - one odd row
      /// must not kill the whole screen reading it.
      /// </param>
      public static ClaimAction FromKey(string key) {
         var separatorIndex = key.IndexOf(Separator);
         if (separatorIndex < 0) {
            return new ClaimAction { ModuleName = string.Empty, Name = key };
         }

         return new ClaimAction {
            ModuleName = key[..separatorIndex],
            Name = key[(separatorIndex + 1)..]
         };
      }

      /// <summary>Module that owns the claim.</summary>
      public required string ModuleName { get; init; }

      /// <summary>Claim name within its module.</summary>
      public required string Name { get; init; }

      /// <summary>
      /// Combined key stored in the database, <c>module:claimname</c>. Ignored during serialization: it is
      /// derived from the other two properties, so sending it over the wire would send the same data twice
      /// and open the door to a key inconsistent with its parts.
      /// </summary>
      [JsonIgnore]
      public string Key => $"{ModuleName}{Separator}{Name}";
   }
}