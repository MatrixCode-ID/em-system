namespace Em.Ui.Core.Shared
{
   /// <summary>Base class of navigation parameters.</summary>
   public abstract class NavigationPayloadBase(object? data)
   {
      /// <summary>The raw data carried by the parameter.</summary>
      protected object? _data = data;
      /// <summary>The data carried by the parameter.</summary>
      public object? Data => _data;
      /// <summary>Whether the parameter describes new or existing data.</summary>
      public DataState DataState { get; protected set; }

      /// <summary>
      /// Title of the entry opened with this parameter, or <c>null</c> to use the navigation's default title.
      /// This title is also the entry's unique key: opening a screen with a title that is already open only
      /// moves the display to that entry.
      /// <para>
      /// Therefore, a document's title <b>must contain its unique identifier</b> - e.g. a reference number or
      /// an account name - not text that can repeat like a customer name. If two different documents produce
      /// the same title, the second document can never be opened: the one shown is always the first.
      /// </para>
      /// </summary>
      public virtual string? Title => null;
   }

   /// <summary>Whether a screen is opened for new data or for editing existing data.</summary>
   public enum DataState
   {
      /// <summary>New data is being created.</summary>
      NewData,
      /// <summary>Existing data is being edited.</summary>
      EditData,
   }
}
