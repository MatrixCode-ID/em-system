/// <summary>
/// General extension methods usable across the whole solution (frontend and backend).
/// </summary>
// ReSharper disable once CheckNamespace
public static class Extensions
{
   /// <summary>
   /// Runs <paramref name="action"/> for every element of <paramref name="source"/>, equivalent to
   /// <c>foreach</c> but as a method chain.
   /// </summary>
   /// <typeparam name="T">Element type of the collection.</typeparam>
   /// <param name="source">Collection to iterate.</param>
   /// <param name="action">Action run for each element. When <c>null</c>, the elements are skipped.</param>
   public static void EachOf<T>(this IEnumerable<T> source, Action<T> action) {
      foreach (var item in source) {
         action?.Invoke(item);
      }
   }
}
