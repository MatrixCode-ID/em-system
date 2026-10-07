using Em.Api.Core.Models;

namespace Em.Api.Core
{
   /// <summary>
   /// Description of a business task about to be started through <c>ServicesBase.StartBusinessTask</c>.
   /// </summary>
   public sealed class BusinessTaskOptions
   {
      /// <summary>
      /// Key of the work. While a task with this key is still queued or running, starting the same key is
      /// refused with 409. For <see cref="BusinessTaskScope.Global"/> the key is unique across the whole
      /// server, for <see cref="BusinessTaskScope.Personal"/> it is unique per user - two users may run the
      /// same key at the same time. Case-insensitive. Use the module name as a prefix so it does not collide
      /// with another module, e.g. <c>"sales:invoice-load:2025"</c>.
      /// </summary>
      public required string Key { get; init; }

      /// <summary>Title shown to the user, e.g. "Load invoices 2025".</summary>
      public required string Title { get; init; }

      /// <summary>
      /// Whose this task is. <see cref="BusinessTaskScope.Personal"/> appears in its starter's personal task
      /// list, and only they or an administrator may manage it.
      /// <see cref="BusinessTaskScope.Global"/> belongs to the screen of the module that asked for it: its
      /// status is asked through that module's own action (with <c>FindBusinessTask</c>), and anyone who
      /// passes that action's right may cancel or clear it. Deliberately has no default value.
      /// </summary>
      public required BusinessTaskScope Scope { get; init; }

      /// <summary>
      /// Suggested navigation name for opening this task's JSON result in the client, if any.
      /// </summary>
      public string? NavigationName { get; init; }

      /// <summary>Suggested result file name when downloaded. Required for tasks whose result is a file.</summary>
      public string? ResultFileName { get; init; }

      /// <summary>Content type (MIME type) of the result file, e.g. <c>application/vnd.openxmlformats-officedocument.spreadsheetml.sheet</c>.</summary>
      public string? ResultContentType { get; init; }
   }
}
