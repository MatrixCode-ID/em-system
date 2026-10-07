using System.Text;

namespace Em.Shared
{
   /// <summary>
   /// CRC32 computation (standard IEEE polynomial) over a text or a byte sequence. The result is always the
   /// same for the same input - in any process, on any computer, and in any programming language using the
   /// same polynomial.
   /// <para>
   /// That property is why this class exists. <c>string.GetHashCode()</c> is deliberately randomized every
   /// time the application runs as a safeguard, so it cannot be used for anything whose result must stay
   /// the same later - for example picking a color from a name. This CRC32 serves that need, and is not a
   /// security tool: do not use it for passwords or signatures (use <see cref="Argon2Hashing"/> and
   /// <see cref="RsaKeyPair"/> for those).
   /// </para>
   /// </summary>
   public static class Crc32
   {
      private const uint Polynomial = 0xEDB88320u;

      // Built once when this class is first used: 256 entries that let the CRC be computed a byte at a
      // time instead of a bit at a time.
      private static readonly uint[] Table = BuildTable();

      private static uint[] BuildTable() {
         var table = new uint[256];
         for (uint i = 0; i < 256; i++) {
            var entry = i;
            for (var bit = 0; bit < 8; bit++) {
               entry = (entry & 1) != 0 ? (entry >> 1) ^ Polynomial : entry >> 1;
            }

            table[i] = entry;
         }

         return table;
      }

      /// <summary>
      /// Computes the CRC32 of a text, read as UTF-8 bytes. Empty text yields 0.
      /// </summary>
      /// <param name="value">Text to compute; <c>null</c> is treated like empty text.</param>
      /// <returns>CRC32 value of the text.</returns>
      public static uint Compute(string value) {
         if (string.IsNullOrEmpty(value)) return 0u;

         var bytes = Encoding.UTF8.GetBytes(value);
         return Compute(bytes);
      }

      /// <summary>
      /// Computes the CRC32 of a byte sequence as is, without interpreting any encoding.
      /// </summary>
      /// <param name="bytes">Bytes to compute; an empty sequence yields 0.</param>
      /// <returns>CRC32 value of the bytes.</returns>
      public static uint Compute(ReadOnlySpan<byte> bytes) {
         if (bytes.IsEmpty) return 0u;

         var crc = 0xFFFFFFFFu;
         foreach (var b in bytes) {
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
         }

         return crc ^ 0xFFFFFFFFu;
      }
   }
}
