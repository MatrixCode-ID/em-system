namespace Em.Shared
{
   /// <summary>
   /// Jumlah anggota dan jumlah hak milik satu role. Bukan baris tabel - angkanya dihitung server
   /// untuk seluruh daftar role sekaligus, supaya layar yang memuat belasan role tidak perlu
   /// bertanya sebanyak itu pula.
   /// </summary>
   public class RoleCounter
   {
      /// <summary>Role yang angkanya dihitung di sini.</summary>
      public required string cRoleId { get; init; }

      /// <summary>Banyaknya user yang sedang memegang role ini, tanpa memandang masa berlakunya.</summary>
      public required int MemberCount { get; init; }

      /// <summary>Banyaknya hak yang dibawa role ini.</summary>
      public required int ClaimCount { get; init; }
   }
}
