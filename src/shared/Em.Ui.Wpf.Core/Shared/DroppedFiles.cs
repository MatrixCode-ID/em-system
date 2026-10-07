namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// The payload received by <see cref="DropTarget"/> when files or folders are dropped from outside the
   /// application, for example from Windows Explorer. Its content is local paths as-is - files and folders
   /// alike - and the receiving command decides what to do with them.
   /// </summary>
   public sealed class DroppedFiles
   {
      /// <summary>
      /// Wraps the list of dropped paths.
      /// </summary>
      /// <param name="paths">The local paths of the files or folders that were dropped.</param>
      public DroppedFiles(IReadOnlyList<string> paths) {
         Paths = paths;
      }

      /// <summary>The local paths of the files or folders that were dropped, in the order from the source.</summary>
      public IReadOnlyList<string> Paths { get; }
   }
}
