namespace Em.Shared
{
   public class TokenResult
   {
      public string cUserId { get; set; } = string.Empty;
      public string AccessToken { get; set; } = string.Empty;
      public string RefreshToken { get; set; } = string.Empty;
      public int ExpiresIn { get; set; }
   }
}
