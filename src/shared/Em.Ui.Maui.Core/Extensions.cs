using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;

// ReSharper disable once CheckNamespace

/// <summary>
/// A collection of extension methods for MAUI UI needs: debug mode configuration, a module's way into
/// its claims, name initials for avatars, and exception message serialization.
/// </summary>
public static class Extensions
{
   /// <summary>
   /// Turns on the application's debug mode: the debug connection that is used and the debug key that
   /// signs its token. Call it once during the application's initial configuration, usually inside
   /// <c>#if DEBUG</c>.
   /// </summary>
   /// <param name="appBuilder">The application builder being configured.</param>
   /// <param name="builder">The callback that fills in the debug connections and key.</param>
   /// <returns>The same builder, so calls can be chained.</returns>
   public static EmAppBuilder AddDebug(this EmAppBuilder appBuilder, Action<DebugBuilder> builder) {
      var obj = new DebugBuilder();
      builder(obj);
      appBuilder.DebugBuilder = obj;
      return appBuilder;
   }

   /// <summary>
   /// A module's way into the claims of its own service: <c>Services.Claims()["CreateNewItem"]</c>. It
   /// lives here, not in <c>Em.Ui.Core</c>, because this is the only piece that needs to know
   /// <see cref="EmApp"/> - <c>ClaimCollection</c> itself does not touch MAUI at all. The object is formed
   /// again on every call; do not keep it in a field.
   /// </summary>
   /// <param name="services">The module service whose claims are asked.</param>
   /// <exception cref="InvalidOperationException">
   /// Thrown when <paramref name="services"/> does not derive from <see cref="ServiceMauiBase"/> - a wrong
   /// setup, not "has no right", so it is not answered with an empty collection.
   /// </exception>
   public static ClaimCollection Claims(this IServices services) =>
      services is ServiceMauiBase svc
         ? new ClaimCollection(svc.ModuleName, svc.App.AllClaims, svc.App.ActiveUser)
         : throw new InvalidOperationException(
            $"Service '{services.GetType().FullName}' is not a MAUI client service, so its claims cannot be resolved.");

   /// <summary>
   /// Composes the two-letter initials of a name, to be used as the content of an avatar circle -
   /// "SYSTEM DEBUGGER" becomes "SD". It always produces something: <c>?</c> for an empty name, so the
   /// circle never appears empty.
   /// </summary>
   /// <param name="name">The name whose initials are taken.</param>
   /// <returns>The initials in uppercase, at most two letters.</returns>
   /// <remarks>
   /// Two letters, not one: one letter is too easily shared by half the people in one company. A name of a
   /// single word has no last name to take, so it hands over its own second letter - "debugger" reads "DE",
   /// not a lone "D".
   /// </remarks>
   public static string ToInitials(this string? name) {
      var words = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      return words.Length switch {
         0 => "?",
         1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
         _ => $"{words[0][0]}{words[^1][0]}".ToUpperInvariant()
      };
   }

   /// <summary>
   /// Composes a summary of the messages of an exception together with all its inner exceptions
   /// (including a flattened <see cref="AggregateException"/>), as tiered text (indented per level) to be
   /// shown to the user/log concisely.
   /// </summary>
   /// <param name="x">The exception to serialize.</param>
   /// <returns>The tiered summary text of the exception messages.</returns>
   public static string SerializedMessagesDefault(this Exception x) {
      var sb = new System.Text.StringBuilder();
      var exceptions = new Stack<(Exception Exception, int Level)>();

      exceptions.Push((x, 0));

      while (exceptions.Count > 0) {
         var (exception, level) = exceptions.Pop();

         if (exception is AggregateException aggregateException) {
            exception = aggregateException.Flatten();
         }

         if (sb.Length > 0) {
            sb.AppendLine();
         }

         if (level > 0) {
            sb.Append(new string(' ', level * 2));
            sb.Append("-> ");
         }

         sb.Append(exception.GetType().Name);

         if (!string.IsNullOrWhiteSpace(exception.Message)) {
            sb.Append(": ");
            sb.Append(exception.Message);
         }

         if (exception is AggregateException flattenedAggregateException) {
            for (var i = flattenedAggregateException.InnerExceptions.Count - 1; i >= 0; i--) {
               exceptions.Push((flattenedAggregateException.InnerExceptions[i], level + 1));
            }
         }
         else if (exception.InnerException is not null) {
            exceptions.Push((exception.InnerException, level + 1));
         }
      }

      return sb.ToString();
   }
}
