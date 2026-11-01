namespace Em.Shared
{
   /// <summary>
   /// How much of a QR code can be damaged or covered and still be read. A higher level survives more
   /// damage, but holds less data and makes the code denser for the same text.
   /// </summary>
   public enum QrErrorCorrection
   {
      /// <summary>About 7% of the code can be restored. Densest data, least tolerant.</summary>
      L,

      /// <summary>About 15% of the code can be restored. The engine default.</summary>
      M,

      /// <summary>About 25% of the code can be restored.</summary>
      Q,

      /// <summary>About 30% of the code can be restored. Use when a logo or wear may cover part of it.</summary>
      H,
   }
}
