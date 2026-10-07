namespace Em.Shared
{
   /// <summary>
   /// How many roles carry one claim key. Used by the claim catalog sheet to answer "how many roles hold
   /// this right" without reading every role one by one.
   /// </summary>
   public class ClaimUsage
   {
      /// <summary>Claim key counted, of the form <c>module:name</c> like <see cref="ClaimAction.Key"/>.</summary>
      public required string ClaimKey { get; init; }

      /// <summary>Number of roles carrying this key.</summary>
      public required int RoleCount { get; init; }
   }
}
