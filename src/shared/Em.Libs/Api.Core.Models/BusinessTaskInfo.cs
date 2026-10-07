using System.Text.Json.Serialization;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Snapshot of one business task as reported by the server: what the work is, who owns it, how far
   /// it has got, and what the caller may do with it. The values change while the task runs, so screens
   /// that monitor it reload this snapshot periodically.
   /// </summary>
   public class BusinessTaskInfo
   {
      /// <summary>Unique ID of this task (ULID), used to cancel, clear or fetch its result.</summary>
      public string Id { get; set; } = "";

      /// <summary>
      /// Work key chosen by the action author. While a task is alive, no other task with the same key can
      /// start: for global tasks the key is unique across the server, for personal tasks unique per
      /// owner.
      /// </summary>
      public string Key { get; set; } = "";

      /// <summary>Who owns this task, and therefore where it is shown.</summary>
      public BusinessTaskScope Scope { get; set; }

      /// <summary>Title shown to the user, e.g. "Archive photos.zip".</summary>
      public string Title { get; set; } = "";

      /// <summary>Module that started this task.</summary>
      public string ModuleName { get; set; } = "";

      /// <summary>ID of the user who started this task.</summary>
      public string OwnerUserId { get; set; } = "";

      /// <summary>Display name of the user who started this task.</summary>
      public string OwnerName { get; set; } = "";

      /// <summary>Kind of result this task leaves behind after success.</summary>
      public BusinessTaskOutputKind OutputKind { get; set; }

      /// <summary>Stage this task is currently in.</summary>
      public BusinessTaskStatus Status { get; set; }

      /// <summary>
      /// Progress in percent (0–100), or <c>null</c> when progress cannot be measured. Show an
      /// indeterminate progress bar for <c>null</c>.
      /// </summary>
      public double? Percent { get; set; }

      /// <summary>Short description of the current step, e.g. the name of the file being processed.</summary>
      public string Caption { get; set; } = "";

      /// <summary>When this task was started and queued.</summary>
      public DateTimeOffset QueuedAt { get; set; }

      /// <summary>When this task actually started running; <c>null</c> while still queued.</summary>
      public DateTimeOffset? StartedAt { get; set; }

      /// <summary>When this task finished, whatever the outcome; <c>null</c> while still alive.</summary>
      public DateTimeOffset? FinishedAt { get; set; }

      /// <summary>Error message when this task failed.</summary>
      public string? ErrorMessage { get; set; }

      /// <summary>
      /// Navigation name suggested by the action author for opening this task's JSON result, if any.
      /// </summary>
      public string? NavigationName { get; set; }

      /// <summary>Result file name, for tasks whose result is a file.</summary>
      public string? ResultFileName { get; set; }

      /// <summary>Content type (MIME type) of the result file, when the action author specifies it.</summary>
      public string? ResultContentType { get; set; }

      /// <summary>Result size in bytes, filled after a task with a result succeeds.</summary>
      public long? ResultSize { get; set; }

      /// <summary><c>true</c> when the caller may cancel this task (and the task is still alive).</summary>
      public bool CanCancel { get; set; }

      /// <summary><c>true</c> when the caller may clear this task (and the task has finished).</summary>
      public bool CanClear { get; set; }

      /// <summary><c>true</c> when the caller may fetch this task's result.</summary>
      public bool CanReadResult { get; set; }

      /// <summary><c>true</c> while this task is queued or running.</summary>
      [JsonIgnore]
      public bool IsAlive => Status is BusinessTaskStatus.Queued or BusinessTaskStatus.Running;
   }
}
