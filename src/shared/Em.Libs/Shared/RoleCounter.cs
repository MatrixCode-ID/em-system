namespace Em.Shared
{
   /// <summary>
   /// Member count and claim count of one role. Not a table row - the numbers are computed by the server
   /// for the whole role list at once, so a screen loading a dozen roles need not ask that many times.
   /// </summary>
   public class RoleCounter
   {
      /// <summary>Role whose numbers are counted here.</summary>
      public required string cRoleId { get; init; }

      /// <summary>Number of users currently holding this role, regardless of validity period.</summary>
      public required int MemberCount { get; init; }

      /// <summary>Number of claims this role carries.</summary>
      public required int ClaimCount { get; init; }
   }
}
