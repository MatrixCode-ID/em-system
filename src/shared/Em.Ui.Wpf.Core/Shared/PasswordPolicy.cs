namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// How far a password rule applies. Follows the enum convention of this repo: a negative value means
   /// not used, zero and above means active.
   /// </summary>
   public enum PasswordRuleLevel
   {
      /// <summary>
      /// The rule is not used at all: it does not appear in the rule list and is not counted by the password
      /// strength meter.
      /// </summary>
      Off = -1,

      /// <summary>
      /// The rule is shown and counted by the password strength meter, but does not hold back the save
      /// button - a password that does not meet it may still be used. This is what makes the rule list work as
      /// a suggestion, not an obstacle.
      /// </summary>
      Advisory = 0,

      /// <summary>
      /// The rule is required: as long as it is not met, the password cannot be saved.
      /// </summary>
      Required = 1
   }

   /// <summary>
   /// The password rules in force in the application - the minimum length and the character kind rules,
   /// each with its own level of application. Set through <see cref="EmAppBuilder.UsePasswordPolicy"/>; an
   /// application that does not call it uses the default value of every property below, which are the same
   /// rules as before this object existed.
   /// <para>
   /// One thing cannot be turned off through this object: both password boxes must be the same and must
   /// not be empty. That is not a password strength rule but a condition so that what is stored is really
   /// what was typed, so it still applies even if <see cref="Disable"/> is called.
   /// </para>
   /// </summary>
   public sealed class PasswordPolicy
   {
      /// <summary>
      /// The minimum password length. Only means anything when <see cref="MinLengthRule"/> is not
      /// <see cref="PasswordRuleLevel.Off"/>.
      /// </summary>
      public int MinLength { get; set; } = 12;

      /// <summary>The level of the minimum length rule.</summary>
      public PasswordRuleLevel MinLengthRule { get; set; } = PasswordRuleLevel.Required;

      /// <summary>The level of the rule "contains both uppercase and lowercase letters".</summary>
      public PasswordRuleLevel MixedCaseRule { get; set; } = PasswordRuleLevel.Advisory;

      /// <summary>The level of the rule "contains at least one digit".</summary>
      public PasswordRuleLevel DigitRule { get; set; } = PasswordRuleLevel.Advisory;

      /// <summary>The level of the rule "contains at least one symbol".</summary>
      public PasswordRuleLevel SymbolRule { get; set; } = PasswordRuleLevel.Advisory;

      /// <summary>
      /// Turns off all the password strength rules at once: no rule list, no meter, and nothing holds back the
      /// save button except that both password boxes must be the same and filled. Provided so turning
      /// everything off need not name each rule, and still includes any new rule added later.
      /// </summary>
      /// <example>
      /// <code>
      /// builder.UsePasswordPolicy(opt => opt.Disable());
      /// </code>
      /// </example>
      public void Disable() {
         MinLength = 0;
         MinLengthRule = PasswordRuleLevel.Off;
         MixedCaseRule = PasswordRuleLevel.Off;
         DigitRule = PasswordRuleLevel.Off;
         SymbolRule = PasswordRuleLevel.Off;
      }

      /// <summary>
      /// Whether the minimum length rule is shown. A minimum length of zero filters nothing, so it counts as
      /// off even if its level is not <see cref="PasswordRuleLevel.Off"/>.
      /// </summary>
      public bool IsMinLengthShown => MinLengthRule != PasswordRuleLevel.Off && MinLength > 0;

      /// <summary>Whether the upper/lowercase rule is shown.</summary>
      public bool IsMixedCaseShown => MixedCaseRule != PasswordRuleLevel.Off;

      /// <summary>Whether the digit rule is shown.</summary>
      public bool IsDigitShown => DigitRule != PasswordRuleLevel.Off;

      /// <summary>Whether the symbol rule is shown.</summary>
      public bool IsSymbolShown => SymbolRule != PasswordRuleLevel.Off;

      /// <summary>
      /// The number of rules shown, 0 to 4. Zero means the rule list and the password strength meter have
      /// nothing to draw, so both are hidden.
      /// </summary>
      public int ShownRuleCount =>
         (IsMinLengthShown ? 1 : 0) + (IsMixedCaseShown ? 1 : 0)
         + (IsDigitShown ? 1 : 0) + (IsSymbolShown ? 1 : 0);

      /// <summary>Whether <paramref name="password"/> meets the minimum length.</summary>
      public bool HasMinLength(string password) => password.Length >= MinLength;

      /// <summary>Whether <paramref name="password"/> contains both uppercase and lowercase letters.</summary>
      public static bool HasMixedCase(string password) =>
         password.Any(char.IsUpper) && password.Any(char.IsLower);

      /// <summary>Whether <paramref name="password"/> contains at least one digit.</summary>
      public static bool HasDigit(string password) => password.Any(char.IsDigit);

      /// <summary>Whether <paramref name="password"/> contains at least one punctuation mark or symbol.</summary>
      public static bool HasSymbol(string password) => password.Any(c => !char.IsLetterOrDigit(c));

      /// <summary>
      /// The number of rules that are shown and already met by <paramref name="password"/>. Used as the
      /// numerator of the password strength meter, with <see cref="ShownRuleCount"/> as its denominator.
      /// </summary>
      /// <param name="password">The password being typed.</param>
      public int CountMetShownRules(string password) {
         var met = 0;
         if (IsMinLengthShown && HasMinLength(password)) met++;
         if (IsMixedCaseShown && HasMixedCase(password)) met++;
         if (IsDigitShown && HasDigit(password)) met++;
         if (IsSymbolShown && HasSymbol(password)) met++;
         return met;
      }

      /// <summary>
      /// Whether <paramref name="password"/> meets every rule whose level is
      /// <see cref="PasswordRuleLevel.Required"/>. A rule that is only a suggestion is not checked here -
      /// that is the difference from the rule list shown on screen.
      /// </summary>
      /// <param name="password">The password being typed.</param>
      public bool IsSatisfiedBy(string password) {
         if (IsMinLengthShown && MinLengthRule == PasswordRuleLevel.Required && !HasMinLength(password)) return false;
         if (MixedCaseRule == PasswordRuleLevel.Required && !HasMixedCase(password)) return false;
         if (DigitRule == PasswordRuleLevel.Required && !HasDigit(password)) return false;
         if (SymbolRule == PasswordRuleLevel.Required && !HasSymbol(password)) return false;
         return true;
      }
   }
}
