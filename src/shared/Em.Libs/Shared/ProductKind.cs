namespace Em.Shared;

/// <summary>
/// What a product type is used for. The kind belongs to the product type; every product follows the kind of its
/// type. The engine attaches no stock or accounting behaviour to it.
/// </summary>
public enum ProductKind
{
   /// <summary>Goods that are bought and sold as they are.</summary>
   Goods = 1,
   /// <summary>The base of something that is manufactured, such as a model.</summary>
   ManufactureBase = 2,
   /// <summary>A component of a manufactured product.</summary>
   ProductComponent = 3,
   /// <summary>An item that is not kept in stock.</summary>
   NonStock = 4,
   /// <summary>A service.</summary>
   Service = 5,
   /// <summary>A financial item, such as a fee or a charge.</summary>
   Financial = 6,
   /// <summary>The generic transaction item every tenant has. Created by the system only.</summary>
   GenericTransaction = 7
}
