namespace Em.Shared
{
   /// <summary>
   /// Berapa banyak role yang membawa satu kunci hak tertentu. Dipakai lembar katalog hak untuk
   /// menjawab "hak ini dipegang berapa role" tanpa harus membaca isi setiap role satu per satu.
   /// </summary>
   public class ClaimUsage
   {
      /// <summary>Kunci hak yang dihitung, berbentuk <c>module:name</c> seperti <see cref="ClaimAction.Key"/>.</summary>
      public required string ClaimKey { get; init; }

      /// <summary>Banyaknya role yang membawa kunci ini.</summary>
      public required int RoleCount { get; init; }
   }
}
