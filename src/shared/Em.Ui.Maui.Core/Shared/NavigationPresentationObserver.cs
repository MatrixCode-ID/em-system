using System.ComponentModel;

namespace Em.Ui.Maui.Shared;

internal sealed class NavigationPresentationObserver(Action<Action> dispatch)
{
   private long _generation;
   public NavigationPresentation? Source { get; private set; }
   public event Action? Changed;

   // Observe is called on the UI thread, including Observe(null) when the host detaches.
   public void Observe(NavigationPresentation? source) {
      Interlocked.Increment(ref _generation);
      if (Source is not null) Source.PropertyChanged -= OnPropertyChanged;
      Source = source;
      if (Source is not null) Source.PropertyChanged += OnPropertyChanged;
      Changed?.Invoke();
   }

   private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
      var generation = Interlocked.Read(ref _generation);
      dispatch(() => {
         if (generation == Interlocked.Read(ref _generation) && ReferenceEquals(sender, Source)) Changed?.Invoke();
      });
   }
}
