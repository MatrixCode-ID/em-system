namespace Em.Api.Core.Approval
{
   /// <summary>
   /// Database functions that may only be used inside a query, mapped to the database's built-in
   /// functions.
   /// </summary>
   /// <remarks>
   /// Used by the request list to sort by the module's summary columns, which are stored as JSON. Only
   /// SQL Server is mapped here - like the approval's cross-database transaction, which also only runs
   /// there.
   /// </remarks>
   internal static class ApprovalJsonFunctions
   {
      /// <summary>A scalar value inside JSON text, by its path. Cannot be called outside a query.</summary>
      /// <param name="json">The JSON text.</param>
      /// <param name="path">Path to the value, e.g. <c>$."Customer"</c>.</param>
      public static string? JsonValue(string? json, string path) =>
         throw new NotSupportedException("This function can only be used inside a query.");
   }
}
