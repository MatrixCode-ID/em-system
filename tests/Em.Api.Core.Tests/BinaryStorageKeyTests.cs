using Em.Api.Core.Storage;

namespace Em.Api.Core.Tests
{
   public class BinaryStorageKeyTests
   {
      [Theory]
      [InlineData("approval/doc.pdf", "approval/doc.pdf")]
      [InlineData("/approval//2026/doc.pdf/", "approval/2026/doc.pdf")]
      public void Normalize_CollapsesSeparators(string key, string expected) {
         Assert.Equal(expected, BinaryStorageKey.Normalize(key));
      }

      [Theory]
      [InlineData("")]
      [InlineData("   ")]
      [InlineData("///")]
      [InlineData("approval\\doc.pdf")]
      [InlineData("approval/../secret")]
      [InlineData("approval/./doc.pdf")]
      [InlineData("approval/doc file.pdf")]
      public void Normalize_RejectsKeysThatCouldPointElsewhere(string key) {
         Assert.Throws<ArgumentException>(() => BinaryStorageKey.Normalize(key));
      }

      [Fact]
      public void Normalize_RejectsOverlongSegment() {
         var key = new string('a', BinaryStorageKey.MaxSegmentLength + 1);
         Assert.Throws<ArgumentException>(() => BinaryStorageKey.Normalize(key));
      }

      [Fact]
      public void Combine_SkipsEmptyParts() {
         Assert.Equal("approval/doc.pdf", BinaryStorageKey.Combine("approval", null, " ", "doc.pdf"));
      }

      [Theory]
      [InlineData("approval/doc.pdf", true)]
      [InlineData("approval/..", false)]
      [InlineData(null, false)]
      public void IsValid_MatchesNormalize(string? key, bool expected) {
         Assert.Equal(expected, BinaryStorageKey.IsValid(key));
      }
   }
}
