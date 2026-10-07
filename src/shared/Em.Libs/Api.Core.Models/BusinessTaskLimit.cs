namespace Em.Api.Core.Models
{
   /// <summary>
   /// Limit on the number of business tasks running at the same time. Tasks above it are not rejected
   /// but wait with status <see cref="BusinessTaskStatus.Queued"/> until one finishes. Stored in the
   /// server metadata and changed from the Business Task Manager screen.
   /// </summary>
   public class BusinessTaskLimit
   {
      /// <summary>Whether <see cref="Limit"/> applies to the whole server or to each user.</summary>
      public BusinessTaskLimitMode Mode { get; set; }

      /// <summary>Number of tasks allowed to run at the same time; at least <c>1</c>.</summary>
      public int Limit { get; set; } = 1;
   }
}
