using Em.Api.Core.Approval;

namespace Em.Test.Api
{
   /// <summary>Kunci sebuah item uji pada data approval.</summary>
   public record TestItemKey([property: KeyPart(1)] string ItemId);

   /// <summary>Kunci sebuah dokumen uji pada document approval.</summary>
   public record TestDocKey([property: KeyPart(1)] string DocId);

   /// <summary>
   /// Letak kotak tanda tangan dan isian pada PDF dokumen uji, dalam milimeter dari kiri atas halaman.
   /// Dipakai dua kali: oleh deklarasi alur approval, dan oleh pembuat PDF yang menggambar kotaknya.
   /// </summary>
   internal static class TestSlots
   {
      public static readonly ApprovalSlot PreparedBy = ApprovalSlot.At(15, 235, 55, 20);

      public static readonly ApprovalSlot QaCheck = ApprovalSlot.At(75, 235, 55, 20);

      public static readonly ApprovalSlot ApprovedByA = ApprovalSlot.At(135, 235, 55, 20);

      public static readonly ApprovalSlot ApprovedByB = ApprovalSlot.At(135, 262, 55, 20);

      public static readonly ApprovalSlot QaPassedBox = ApprovalSlot.At(15, 205, 6, 6);

      public static readonly ApprovalSlot QaRemarks = ApprovalSlot.At(25, 204, 165, 8);
   }
}
