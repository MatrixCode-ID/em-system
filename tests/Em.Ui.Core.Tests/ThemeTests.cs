using Em.Ui.Core.Shared;

namespace Em.Ui.Core.Tests
{
   public class ThemeTests
   {
      public static TheoryData<ThemeBase, ThemeVariant> Themes => new() {
         { new LightTheme(), ThemeVariant.Light },
         { new DarkTheme(), ThemeVariant.Dark }
      };

      [Theory]
      [MemberData(nameof(Themes))]
      public void Theme_ReportsItsVariant(ThemeBase theme, ThemeVariant expected) {
         Assert.Equal(expected, theme.Variant);
      }

      // Text drawn in an On* colour sits on its paired colour; the two being equal would make it invisible.
      [Theory]
      [MemberData(nameof(Themes))]
      public void Theme_OnColoursDifferFromTheirSurface(ThemeBase theme, ThemeVariant _) {
         Assert.NotEqual(theme.Primary, theme.OnPrimary);
         Assert.NotEqual(theme.PrimaryContainer, theme.OnPrimaryContainer);
         Assert.NotEqual(theme.Secondary, theme.OnSecondary);
         Assert.NotEqual(theme.SecondaryContainer, theme.OnSecondaryContainer);
      }
   }
}
