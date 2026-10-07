namespace Em.Api.Core.Models
{
   /// <summary>
   /// Who owns a business task, and therefore where it is shown. Chosen by the action author when
   /// starting the task.
   /// </summary>
   public enum BusinessTaskScope
   {
      /// <summary>
      /// Owned by the user who started it, for example loading data for themselves. Shown in that user's
      /// personal task list in the main window. Only the owner and administrators may cancel it, clear it
      /// and fetch its result.
      /// </summary>
      Personal = 0,

      /// <summary>
      /// Owned by the module screen that requested it, for example creating an archive on the CDN. Shown
      /// on that screen to anyone allowed to open it, and anyone who passes that module's action rights
      /// may cancel or clear it.
      /// </summary>
      Global = 1
   }
}
