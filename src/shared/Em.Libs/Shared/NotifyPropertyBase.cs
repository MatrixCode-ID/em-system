using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Em.Shared
{
   /// <summary>
   /// Base class for view models/models that need property change notification
   /// (<see cref="INotifyPropertyChanged"/>), storing property values in a dictionary (no manual backing
   /// field per property). The MVVM foundation on the WPF side, but placed in <c>Em.Libs</c> (not a WPF
   /// project) so non-UI models that still need change notification can reuse it.
   /// </summary>
   public abstract class NotifyPropertyBase : INotifyPropertyChanged
   {
      private readonly Dictionary<string, object?> _propertyValues = [];
      private readonly Dictionary<string, Action<object?>> _propertyChangedActions = [];

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;

      /// <summary>
      /// Raises <see cref="PropertyChanged"/> for one property name.
      /// </summary>
      /// <param name="propertyName">
      /// Name of the changed property. Filled automatically with the caller's member name when not given.
      /// </param>
      protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "") {
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      }

      /// <summary>
      /// Changes a regular backing field (not the internal dictionary), then raises
      /// <see cref="PropertyChanged"/> and the <paramref name="onChanged"/> callback when the value differs.
      /// Suitable for properties that already have an explicit backing field.
      /// </summary>
      /// <typeparam name="T">Property value type.</typeparam>
      /// <param name="field">Reference to the backing field to change.</param>
      /// <param name="value">New value.</param>
      /// <param name="onChanged">Optional callback run after the value changed.</param>
      /// <param name="propertyName">Property name, filled automatically through <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns><c>true</c> when the value actually changed and was stored; <c>false</c> when it equals the previous value.</returns>
      protected bool SetField<T>(
         ref T field,
         T value,
         Action<T>? onChanged = null,
         [CallerMemberName] string propertyName = "") {
         if (EqualityComparer<T>.Default.Equals(field, value)) return false;

         field = value;
         OnPropertyChanged(propertyName);
         onChanged?.Invoke(value);
         return true;
      }

      /// <summary>
      /// Gets a property value from the internal storage (dictionary), without a backing field. When the
      /// property has never been set, it is initialized with <paramref name="defaultValue"/>.
      /// </summary>
      /// <typeparam name="T">Property value type.</typeparam>
      /// <param name="defaultValue">Default value when the property has never been set.</param>
      /// <param name="propertyName">Property name, filled automatically through <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns>The current property value.</returns>
      protected T Get<T>(T defaultValue = default!, [CallerMemberName] string propertyName = "") {
         EnsurePropertyExists(propertyName);

         if (_propertyValues.TryGetValue(propertyName, out var value))
            return (T)value!;

         _propertyValues[propertyName] = defaultValue;
         return defaultValue;
      }

      /// <summary>
      /// Stores a property value in the internal storage (dictionary), then raises
      /// <see cref="PropertyChanged"/>, the <paramref name="onChanged"/> callback, and the callback registered
      /// through <see cref="RegisterValueChanged{T}"/> for this property - when the value changed.
      /// </summary>
      /// <typeparam name="T">Property value type.</typeparam>
      /// <param name="value">New value to store.</param>
      /// <param name="onChanged">Optional callback run after the value changed.</param>
      /// <param name="propertyName">Property name, filled automatically through <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns><c>true</c> when the value actually changed and was stored; <c>false</c> when it equals the previous value.</returns>
      protected bool Set<T>(
         T value,
         Action<T>? onChanged = null,
         [CallerMemberName] string propertyName = "") {
         EnsurePropertyExists(propertyName);

         if (_propertyValues.TryGetValue(propertyName, out var currentValue) &&
             EqualityComparer<T>.Default.Equals((T)currentValue!, value)) {
            return false;
         }

         _propertyValues[propertyName] = value;
         OnPropertyChanged(propertyName);
         onChanged?.Invoke(value);

         if (_propertyChangedActions.TryGetValue(propertyName, out var registeredAction))
            registeredAction.Invoke(value);

         return true;
      }

      /// <summary>
      /// Registers an additional callback invoked every time property <paramref name="propertyName"/>
      /// changes through <see cref="Set{T}"/>. Useful for cross-property reactions without overriding
      /// <see cref="OnPropertyChanged"/>. Registering again for the same property replaces the callback.
      /// </summary>
      /// <typeparam name="T">Value type of the observed property.</typeparam>
      /// <param name="propertyName">Name of the observed property.</param>
      /// <param name="action">Action run every time the property value changes.</param>
      protected void RegisterValueChanged<T>(string propertyName, Action<T> action) {
         ArgumentNullException.ThrowIfNull(action);
         EnsurePropertyExists(propertyName);

         _propertyChangedActions[propertyName] = value => action((T)value!);
      }

      /// <summary>
      /// Ensures <paramref name="propertyName"/> really is a property of this type, to catch property name
      /// typos (e.g. a wrong <c>nameof</c> or caller member name).
      /// </summary>
      /// <param name="propertyName">Property name to validate.</param>
      /// <returns>The <see cref="PropertyInfo"/> of that property.</returns>
      /// <exception cref="ArgumentException">When <paramref name="propertyName"/> is empty.</exception>
      /// <exception cref="NotSupportedException">When this type has no property with that name.</exception>
      private PropertyInfo EnsurePropertyExists(string propertyName) {
         if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));

         return GetType().GetProperty(propertyName) ??
                throw new NotSupportedException($"Member \"{propertyName}\" is not supported or is not a property.");
      }

      /// <summary>
      /// Raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/> and returns the event
      /// arguments used, for cases that need the <see cref="PropertyChangedEventArgs"/>.
      /// </summary>
      /// <param name="propertyName">Property name, filled automatically through <see cref="CallerMemberNameAttribute"/>.</param>
      /// <returns>The event arguments used to raise <see cref="PropertyChanged"/>.</returns>
      protected PropertyChangedEventArgs NotifyChanged([CallerMemberName] string propertyName = "") {
         var arg = new PropertyChangedEventArgs(propertyName);
         PropertyChanged?.Invoke(this, arg);
         return arg;
      }

      /// <summary>
      /// Indicates whether the object is busy (e.g. an async operation is running), usually used to enable
      /// or disable UI (loading indicator, disabled buttons, etc.).
      /// </summary>
      public bool IsBusy {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(IsNotBusy)));
      }

      /// <summary>
      /// Inverse of <see cref="IsBusy"/>, provided for direct binding (e.g. <c>IsEnabled</c>) without an
      /// extra converter in XAML.
      /// </summary>
      public bool IsNotBusy => !IsBusy;

      /// <summary>
      /// Indicates whether the object is waiting (e.g. for a server response).
      /// </summary>
      public bool InWaiting {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(IsNotWaiting)));
      }

      /// <summary>
      /// Inverse of <see cref="InWaiting"/>, provided for direct binding without an extra converter in
      /// XAML.
      /// </summary>
      public bool IsNotWaiting => !InWaiting;

      /// <summary>
      /// Short description of what is being waited for, e.g. "Saving user...". Shown by the wait overlay
      /// while <see cref="InWaiting"/> is <c>true</c>; when left empty, the overlay only shows its standard
      /// title.
      /// </summary>
      public string WaiterText {
         get => Get<string>();
         set => Set(value);
      }
   }
}
