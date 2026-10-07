using System.Reflection;

namespace Em.Shared
{
   /// <summary>
   /// Marks a service class with its module name, which groups actions and claims. Server and client use
   /// <see cref="ResolveName"/> to derive the name.
   /// </summary>
   /// <param name="name">Module name. When <c>null</c> or empty, the type name is used.</param>
   [AttributeUsage(AttributeTargets.Class)]
   public class ModuleAttribute(string? name = null) : Attribute
   {
      /// <summary>
      /// Module name set through this attribute, or <c>null</c> when not set.
      /// </summary>
      public string? Name { get; } = name;

      /// <summary>
      /// Derives the module name from the <see cref="ModuleAttribute"/> on <paramref name="serviceType"/>:
      /// the attribute's explicit name when present, or the type name when the attribute is empty. The only
      /// place this rule is written, so server and client never derive different module names for the same
      /// type.
      /// </summary>
      /// <param name="serviceType">Service implementation type marked with <see cref="ModuleAttribute"/>.</param>
      /// <param name="required">
      /// <c>true</c> when the type must carry <see cref="ModuleAttribute"/> - throws when it does not.
      /// <c>false</c> lets a type without the attribute fall back to its own type name, used for UI services
      /// that have long run without this attribute.
      /// </param>
      /// <exception cref="InvalidOperationException">
      /// Thrown when <paramref name="required"/> is <c>true</c> and <paramref name="serviceType"/> does not
      /// carry <see cref="ModuleAttribute"/>.
      /// </exception>
      public static string ResolveName(Type serviceType, bool required = true) {
         var attribute = serviceType.GetCustomAttribute<ModuleAttribute>();
         if (attribute is null) {
            if (required) {
               throw new InvalidOperationException(
                  $"Type '{serviceType.FullName}' must be decorated with [Module] attribute to take part in claims or action routing.");
            }

            return serviceType.Name;
         }

         return string.IsNullOrWhiteSpace(attribute.Name) ? serviceType.Name : attribute.Name;
      }
   }
}