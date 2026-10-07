namespace Em.Shared
{
   /// <summary>
   /// Derives the color of a data row from its name. The color is never stored: the entity name is
   /// hashed with <see cref="Crc32"/> and the remainder selects one color slot in the palette. The
   /// same name therefore always gets the same color - today, tomorrow, and on any other client, as
   /// long as the normalization and the algorithm match. That is why both live here rather than in
   /// each client's presentation layer.
   /// <para>
   /// Known consequence: renaming a row moves its color.
   /// </para>
   /// </summary>
   public static class UiTint
   {
      /// <summary>
      /// Picks a color slot for a name. Case and leading/trailing whitespace do not matter -
      /// <c>" Admin "</c> and <c>"admin"</c> get the same slot.
      /// </summary>
      /// <param name="name">Name of the entity being colored; empty or <c>null</c> always yields slot 0.</param>
      /// <param name="slotCount">
      /// Number of colors available in the palette. Supplied by the caller because the color count is a
      /// presentation decision that may grow at any time - since colors are not stored, adding colors only
      /// changes the display and never makes any data wrong.
      /// </param>
      /// <returns>A slot number from 0 to <paramref name="slotCount"/> - 1.</returns>
      /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="slotCount"/> is less than 1.</exception>
      public static int SlotOf(string? name, int slotCount) {
         ArgumentOutOfRangeException.ThrowIfLessThan(slotCount, 1);

         if (string.IsNullOrWhiteSpace(name)) return 0;

         var normalized = name.Trim().ToLowerInvariant();
         return (int)(Crc32.Compute(normalized) % (uint)slotCount);
      }
   }
}
