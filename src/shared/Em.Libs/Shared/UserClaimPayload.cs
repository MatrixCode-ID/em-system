namespace Em.Shared
{
   /// <summary>Identifies one claim of one user, as sent to the add/remove user claim actions.</summary>
   public class UserClaimPayload {
      /// <summary>Key of the user the claim belongs to.</summary>
      public required string cUserId { get; init; }
      /// <summary>Name of the claim.</summary>
      public required string cUserClaimName { get; init; }
   }
}
