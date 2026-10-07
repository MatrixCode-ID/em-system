using Em.Api.Core.Models;

namespace Em.Api.Core.Hub
{
   /// <summary>
   /// One source of the active user's task list. The task list in the application title bar is the union
   /// of all registered sources, so a new kind of task is added by registering a new source - not by
   /// changing that list.
   /// </summary>
   /// <remarks>
   /// The list is always <b>computed</b> from the source's data, never stored as a separate task. That is
   /// what makes a task disappear by itself from other people's lists as soon as one person completes it.
   /// <para>
   /// A source is called inside the relevant user's request, so the caller's identity is read from that
   /// request as in an ordinary action. A source that fails does not fail the other sources: its failure
   /// is logged and its part is empty.
   /// </para>
   /// </remarks>
   public interface IHubTaskSource
   {
      /// <summary>
      /// The tasks the active user needs to know about according to this source, or an empty list when there
      /// are none.
      /// </summary>
      /// <param name="cancellationToken">Cancellation token.</param>
      Task<IReadOnlyList<HubTaskInfo>> GetTasksAsync(CancellationToken cancellationToken = default);
   }
}
