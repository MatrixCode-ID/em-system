namespace Em.Api.Core.Models
{
   /// <summary>
   /// Stage a business task is in, from queued to finished. This is a process stage, not an
   /// active/inactive marker, so there are no negative values here.
   /// </summary>
   public enum BusinessTaskStatus
   {
      /// <summary>Waiting for its turn because the limit of concurrently running tasks is reached.</summary>
      Queued = 0,

      /// <summary>Being processed by the server.</summary>
      Running = 1,

      /// <summary>Finished without errors.</summary>
      Succeeded = 2,

      /// <summary>
      /// Stopped because of an error. A failed task stays visible until cleared, so its cause can be
      /// read.
      /// </summary>
      Failed = 3,

      /// <summary>Stopped at the user's request, or because the server shut down.</summary>
      Canceled = 4
   }
}
