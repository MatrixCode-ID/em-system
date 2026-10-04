using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Satu permintaan persetujuan: dokumen apa yang diajukan, siapa yang mengajukan, sampai di level
   /// mana ia berjalan, dan bagaimana akhirnya.
   /// </summary>
   /// <remarks>
   /// Request yang sudah disetujui sekaligus menjadi jejak audit perubahannya, jadi baris di sini tidak
   /// pernah dibuang - termasuk yang ditolak atau ditarik kembali.
   /// </remarks>
   [Table("ta_ApprovalRequest")]
   public class ta_ApprovalRequest
   {
      [Key]
      public string cApprovalRequestId { get; set; } = string.Empty;

      /// <summary>Apakah data usulannya menumpang di request ini, atau sudah ada di dokumennya.</summary>
      public ApprovalKind cApprovalRequestKind { get; set; }

      /// <summary>Jenis dokumen yang diajukan, merujuk daftar jenis dokumen aplikasi.</summary>
      public string cApprovalRequestDocType { get; set; } = string.Empty;

      /// <summary>
      /// Kunci dokumennya dalam bentuk kanonik: array JSON berisi bagian-bagian kunci sesuai urutan yang
      /// dideklarasikan modul. Satu string supaya bisa di-index, dicocokkan, dan dirunut riwayatnya,
      /// walaupun kunci aslinya terdiri dari beberapa bagian.
      /// </summary>
      public string cApprovalRequestDocKey { get; set; } = string.Empty;

      /// <summary>
      /// Versi dokumen yang diajukan. Disimpan sebagai teks apa adanya, karena penomoran versi dokumen
      /// warisan tidak selalu berupa angka.
      /// </summary>
      public string cApprovalRequestDocVersion { get; set; } = string.Empty;

      /// <summary>Pengaju request ini. Selalu user nyata - akun sistem tidak bisa mengajukan.</summary>
      public string cApprovalRequestRequesterId { get; set; } = string.Empty;

      /// <summary>
      /// Level yang sedang menunggu keputusan. Langkah-langkah dikelompokkan per level, dan level
      /// berikutnya baru dimulai setelah semua langkah di level ini disetujui.
      /// </summary>
      public int cApprovalRequestLevel { get; set; }

      /// <summary>Kapan seluruh langkahnya selesai, atau kosong kalau belum.</summary>
      public DateTime? cApprovalRequestCompletedDate { get; set; }

      /// <summary>
      /// Penunjuk ke berkas PDF dasar dokumen ini di penyimpanan biner, dibekukan saat diajukan. Kosong
      /// untuk jenis dokumen yang tidak punya PDF.
      /// </summary>
      public string? cApprovalRequestPdfKey { get; set; }

      /// <summary>
      /// Request sebelumnya yang ditarik kembali lalu diajukan ulang untuk dokumen dan versi yang sama.
      /// Lewat tautan ini riwayat pengajuan ulang tetap tersambung.
      /// </summary>
      public string? cApprovalRequestReinstateOf { get; set; }

      /// <summary>Tahap hidup request ini.</summary>
      public ApprovalStage cApprovalRequestStage { get; set; }

      /// <summary>Catatan pengaju saat mengajukan.</summary>
      public string? cApprovalRequestNote { get; set; }

      /// <summary>Kapan request ini diajukan.</summary>
      public DateTime cApprovalRequestDate { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      /// <summary>
      /// Kolom ringkasan milik modul, dipotret saat diajukan. Modul menentukan sendiri isinya; engine
      /// hanya menyimpannya dan memakainya untuk menyaring serta mengurutkan daftar request. Berupa satu
      /// objek JSON datar. Nama berawalan tanda dolar dicadangkan untuk catatan engine sendiri, misalnya
      /// siapa yang menarik kembali request ini dan kenapa, dan tidak pernah tampil sebagai kolom.
      /// </summary>
      public string? json_object { get; set; }
   }
}
