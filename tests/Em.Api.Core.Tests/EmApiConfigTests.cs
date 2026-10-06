using Em.Api.Shared;

namespace Em.Api.Core.Tests
{
   public class EmApiConfigTests
   {
      [Fact]
      public void IsModuleEnabled_UnlistedModule_UsesDefault() {
         var config = new EmApiConfig();

         Assert.False(config.IsModuleEnabled("test"));
         Assert.True(config.IsModuleEnabled("test", defaultValue: true));
      }

      // Loading replaces the dictionary (and its comparer), so the name must still match case-insensitively.
      [Theory]
      [InlineData("""{ "modules": { "Test": true } }""", true)]
      [InlineData("""{ "modules": { "test": false } }""", false)]
      [InlineData("""{ "modules": { "shop": true } }""", false)]
      public void IsModuleEnabled_ReadsLoadedFile(string json, bool expected) {
         var path = Path.Combine(Path.GetTempPath(), $"emapi-config-{Guid.NewGuid():N}.json");
         try {
            File.WriteAllText(path, json);

            Assert.Equal(expected, EmApiConfig.Load(path).IsModuleEnabled("test"));
         } finally {
            File.Delete(path);
         }
      }
   }
}
