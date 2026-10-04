using System.Text;
using Em.Shared;

namespace Em.Libs.Tests
{
   public class Crc32Tests
   {
      // "123456789" is the standard check input for CRC-32/ISO-HDLC; every conforming implementation
      // returns 0xCBF43926 for it, so a match proves the polynomial and the final XOR are both right.
      [Fact]
      public void Compute_StandardCheckValue() {
         Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"));
      }

      [Theory]
      [InlineData("")]
      [InlineData(null)]
      public void Compute_EmptyText_IsZero(string? value) {
         Assert.Equal(0u, Crc32.Compute(value!));
      }

      [Fact]
      public void Compute_TextAndItsUtf8Bytes_Agree() {
         const string value = "Approval · Ünïcode";
         Assert.Equal(Crc32.Compute(Encoding.UTF8.GetBytes(value)), Crc32.Compute(value));
      }
   }
}
