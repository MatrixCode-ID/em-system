namespace Em.Shared;

/// <summary>
/// The product type and the product every tenant has for document lines that name no specific product. The system
/// creates them; an administrator may rename them but cannot change their code or kind, or deactivate them.
/// </summary>
public static class TenantProductSystem
{
   /// <summary>Code of the system product type and of the system product.</summary>
   public const string Code = "GENERIC";
   /// <summary>Name the system product type is created with.</summary>
   public const string TypeName = "Generic Transaction";
   /// <summary>Name the system product is created with.</summary>
   public const string ProductName = "Generic Transaction Item";
   /// <summary>Unit the system product is created with; the unit is created when it does not exist yet.</summary>
   public const string UnitId = "UNIT";
   /// <summary>Symbol the system unit is created with.</summary>
   public const string UnitSymbol = "unit";
   /// <summary>Description the system unit is created with.</summary>
   public const string UnitDesc = "Unit";
}
