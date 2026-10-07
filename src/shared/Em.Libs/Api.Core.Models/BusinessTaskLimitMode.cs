namespace Em.Api.Core.Models
{
   /// <summary>How <see cref="BusinessTaskLimit.Limit"/> is counted.</summary>
   public enum BusinessTaskLimitMode
   {
      /// <summary>At most N tasks run at the same time across the whole server.</summary>
      Global = 0,

      /// <summary>
      /// At most N tasks run at the same time per user. Global tasks count towards the user who started them.
      /// </summary>
      PerUser = 1
   }
}
