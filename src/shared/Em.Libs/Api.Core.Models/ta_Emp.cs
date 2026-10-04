using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Data kepegawaian seorang karyawan: nomor karyawannya menurut bagian personalia, dan kontak yang
   /// mewakilinya di aplikasi.
   /// </summary>
   /// <remarks>
   /// Jembatan antara nomor karyawan - yang dipakai dokumen dan aplikasi lama - dengan identitas orang di
   /// aplikasi ini. Lewat jembatan itu sebuah dokumen bisa menyebut siapa yang harus menandatanganinya
   /// dengan nomor karyawan, dan engine tetap bisa menemukan user yang bersangkutan.
   /// </remarks>
   [Table("ta_Emp")]
   public class ta_Emp
   {
      [Key]
      public string cEmpId { get; set; } = string.Empty;

      /// <summary>Kontak yang mewakili karyawan ini.</summary>
      public string cContactId { get; set; } = string.Empty;

      /// <summary>Jabatan karyawan ini saat ini.</summary>
      public string cEmpPositionCurent { get; set; } = string.Empty;
   }
}
