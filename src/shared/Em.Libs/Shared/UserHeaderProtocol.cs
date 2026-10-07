using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Shared
{
   /// <summary>
   /// Rules for composing the content of the <see cref="Defaults.UserHeader"/> header - the client's note
   /// of which account is active on its screen. Both sides use this class: the client that composes the
   /// header and the server that reads it must say exactly the same thing.
   /// </summary>
   /// <remarks>
   /// The content is a JSON object carrying both the id and the account name, e.g.
   /// <c>{"cUserId":"01K5Z0X9P7QW3M8V2ND6TJHFAB","cUserAccount":"budi"}</c>. The id is carried because an
   /// account name can change while the id does not, so the server relies on the permanent identity and
   /// the name merely serves as a human-readable note in logs.
   /// <para>
   /// <see cref="TryRead"/> also still accepts a plain account name (text that does not start with
   /// <c>{</c>), so developers can keep typing <c>X-Em-User: budi</c> in curl/Postman without composing
   /// JSON.
   /// </para>
   /// <para>
   /// One rule never changes: the header content is never a source of identity by itself. It is trusted
   /// only when the request also carries a debug token that passes verification - if it were ever trusted
   /// alone, anyone could become any owner just by naming an id.
   /// </para>
   /// </remarks>
   public static class UserHeaderProtocol
   {
      /// <summary>
      /// Maximum header length that will be processed. Generous for an id plus an account name; it exists
      /// so a caller that has proven nothing cannot force the server to parse a large blob.
      /// </summary>
      public const int MaxHeaderLength = 1024;

      /// <summary>
      /// Composes the header content from the active id and/or account name. Empty members are skipped,
      /// so the header never contains <c>"cUserId":null</c>.
      /// </summary>
      /// <param name="cUserId">Id of the active user, or <c>null</c> when not known.</param>
      /// <param name="cUserAccount">Account name of the active user, or <c>null</c> when not known.</param>
      /// <returns>
      /// The header content ready to send, or an empty string when neither can be named - a header that
      /// names nobody is better not sent at all.
      /// </returns>
      /// <remarks>
      /// Serialization uses the default <see cref="System.Text.Json"/> encoder, which escapes non-ASCII
      /// characters as <c>\uXXXX</c>. The result is therefore always safe to send as-is as a header value,
      /// whatever the account name contains.
      /// </remarks>
      public static string Create(string? cUserId, string? cUserAccount) {
         var hasId = !string.IsNullOrWhiteSpace(cUserId);
         var hasAccount = !string.IsNullOrWhiteSpace(cUserAccount);
         if (!hasId && !hasAccount) {
            return string.Empty;
         }

         return JsonSerializer.Serialize(new UserHeaderPayload(
            hasId ? cUserId : null,
            hasAccount ? cUserAccount : null));
      }

      /// <summary>
      /// Reads the header content into the id and account name it names.
      /// </summary>
      /// <param name="header">The header content as received.</param>
      /// <param name="cUserId">The id named by the header, or <c>null</c> when not named.</param>
      /// <param name="cUserAccount">The account name named by the header, or <c>null</c> when not named.</param>
      /// <returns>
      /// <c>true</c> when the header names at least one of the two. A header that is present but cannot be
      /// read at all counts as naming nobody, not as an error: identity is still decided by the token the
      /// request carries, not by this header.
      /// </returns>
      public static bool TryRead(string? header, out string? cUserId, out string? cUserAccount) {
         cUserId = null;
         cUserAccount = null;

         if (string.IsNullOrWhiteSpace(header) || header.Length > MaxHeaderLength) {
            return false;
         }

         var text = header.Trim();

         // Legacy form - a plain account name. Kept so developers can still type it directly in
         // curl/Postman, and so the tripwire at the gate still catches headers written in that form.
         if (text[0] != '{') {
            cUserAccount = text;
            return true;
         }

         UserHeaderPayload? payload;
         try {
            payload = JsonSerializer.Deserialize<UserHeaderPayload>(text);
         }
         catch (JsonException) {
            return false;
         }

         if (payload is null) {
            return false;
         }

         cUserId = string.IsNullOrWhiteSpace(payload.cUserId) ? null : payload.cUserId!.Trim();
         cUserAccount = string.IsNullOrWhiteSpace(payload.cUserAccount) ? null : payload.cUserAccount!.Trim();
         return cUserId is not null || cUserAccount is not null;
      }

      private sealed record UserHeaderPayload(
         [property: JsonPropertyName("cUserId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
         string? cUserId,
         [property: JsonPropertyName("cUserAccount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
         string? cUserAccount);
   }
}
