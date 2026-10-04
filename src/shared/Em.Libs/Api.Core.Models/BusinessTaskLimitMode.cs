namespace Em.Api.Core.Models
{
   /// <summary>
   /// Cara angka <see cref="BusinessTaskLimit.Limit"/> dihitung.
   /// </summary>
   public enum BusinessTaskLimitMode
   {
      /// <summary>Paling banyak N task berjalan bersamaan di seluruh server.</summary>
      Global = 0,

      /// <summary>
      /// Paling banyak N task berjalan bersamaan per user. Task global dihitung ke user yang memulainya.
      /// </summary>
      PerUser = 1
   }
}
