namespace Em.Shared
{
   /// <summary>
   /// Sample placeholder model for testing or demonstrating POST binding. Not used by the engine itself
   /// and not a business model - do not use it as a reference for production data structures.
   /// </summary>
   public class DummyModel
   {
      /// <summary>
      /// Sample name.
      /// </summary>
      public string Name { get; set; } = "";

      /// <summary>
      /// Sample numeric value.
      /// </summary>
      public int Value { get; set; }

      /// <summary>
      /// Optional sample identifier of type <see cref="System.Guid"/>.
      /// </summary>
      public Guid? Guid { get; set; }
   }
}
