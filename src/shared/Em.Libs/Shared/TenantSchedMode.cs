namespace Em.Shared;

/// <summary>Where the default schedule of one level comes from.</summary>
public enum TenantSchedMode
{
   /// <summary>Follows the level above. On the tenant level the same value means <see cref="BuiltIn"/>.</summary>
   Inherit = 0,
   /// <summary>The built-in office schedule; only meaningful on the tenant level or in an effective result.</summary>
   BuiltIn = 0,
   /// <summary>One schedule template.</summary>
   Template = 1,
   /// <summary>A rotation of schedule templates.</summary>
   Rotation = 2
}
