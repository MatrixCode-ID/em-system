namespace Em.Shared
{
   /// <summary>Result of signing in or refreshing: the tokens and their lifetime.</summary>
   public class TokenResult
   {
      /// <summary>ID of the signed-in user.</summary>
      public string cUserId { get; set; } = string.Empty;

      /// <summary>Access token sent with every request.</summary>
      public string AccessToken { get; set; } = string.Empty;

      /// <summary>Refresh token exchanged for a new token pair.</summary>
      public string RefreshToken { get; set; } = string.Empty;

      /// <summary>Lifetime of the access token, in seconds.</summary>
      public int ExpiresIn { get; set; }
}
