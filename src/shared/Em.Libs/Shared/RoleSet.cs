using Em.Api.Core.Models;

namespace Em.Shared
{
   /// <summary>
   /// Perubahan isi satu role yang dikirim sekali jalan: hak yang ditambah dan dicabut, anggota yang
   /// masuk, keluar, dan yang masa berlakunya diubah. Bukan baris tabel dan bukan pula daftar niat -
   /// isinya adalah selisih antara keadaan role saat dibuka dan keadaan yang diinginkan saat tombol
   /// simpan ditekan, jadi mencentang lalu membatalkan centang yang sama tidak meninggalkan apa pun
   /// di sini.
   /// <para>
   /// Sengaja bukan <see cref="DtoPayload{T1,T2,T3,T4,T5}"/>: payload itu mencocokkan slot
   /// berdasarkan posisi, sedangkan tiga dari lima daftar di sini bertipe sama
   /// (<see cref="ta_UserRole"/>). Urutan yang tertukar akan lolos compiler, lolos runtime, lalu
   /// menambahkan anggota yang mestinya justru dicabut.
   /// </para>
   /// <para>
   /// Angka yang ditampilkan layar - "2 hak dan 1 penugasan berubah" - dihitung dari objek ini juga,
   /// lewat <see cref="ClaimChangeCount"/> dan <see cref="AssignmentChangeCount"/>, supaya tidak ada
   /// penghitung terpisah yang bisa melenceng dari apa yang benar-benar dikirim.
   /// </para>
   /// </summary>
   public class RoleSet
   {
      /// <summary>Role yang isinya diubah. Seluruh baris di bawah menunjuk id ini.</summary>
      public required string cRoleId { get; init; }

      /// <summary>Hak yang diberikan ke role ini.</summary>
      public ta_RoleClaim[] ClaimsGranted { get; init; } = [];

      /// <summary>Hak yang dicabut dari role ini.</summary>
      public ta_RoleClaim[] ClaimsRevoked { get; init; } = [];

      /// <summary>Penugasan baru - user yang mulai memegang role ini.</summary>
      public ta_UserRole[] MembersAdded { get; init; } = [];

      /// <summary>Penugasan yang dicabut - user yang berhenti memegang role ini.</summary>
      public ta_UserRole[] MembersRemoved { get; init; } = [];

      /// <summary>
      /// Penugasan yang tetap berjalan tapi masa berlakunya berubah. Terpisah dari
      /// <see cref="MembersRemoved"/> + <see cref="MembersAdded"/> karena mencabut lalu memberikan
      /// ulang akan menghapus catatan kapan penugasan itu pertama kali dibuat.
      /// </summary>
      public ta_UserRole[] MembersRescheduled { get; init; } = [];

      /// <summary>Banyaknya perubahan hak, yaitu yang diberikan ditambah yang dicabut.</summary>
      public int ClaimChangeCount => ClaimsGranted.Length + ClaimsRevoked.Length;

      /// <summary>Banyaknya perubahan penugasan, yaitu yang masuk, keluar, dan yang diubah masa berlakunya.</summary>
      public int AssignmentChangeCount =>
         MembersAdded.Length + MembersRemoved.Length + MembersRescheduled.Length;

      /// <summary>
      /// <c>true</c> kalau tidak ada satu pun perubahan di sini. Pemanggil memeriksanya sebelum
      /// mengirim, supaya tombol simpan yang ditekan tanpa ada yang berubah tidak berubah menjadi
      /// perjalanan ke server yang tidak menghasilkan apa-apa.
      /// </summary>
      public bool IsEmpty => ClaimChangeCount == 0 && AssignmentChangeCount == 0;
   }
}
