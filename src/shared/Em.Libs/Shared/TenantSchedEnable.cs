namespace Em.Shared;

/// <summary>Whether scheduling applies at one level. The tenant level only takes enabled or disabled.</summary>
public enum TenantSchedEnable
{
   /// <summary>Scheduling does not apply.</summary>
   Disabled = -1,
   /// <summary>Follows the level above.</summary>
   Inherit = 0,
   /// <summary>Scheduling applies.</summary>
   Enabled = 1
}
