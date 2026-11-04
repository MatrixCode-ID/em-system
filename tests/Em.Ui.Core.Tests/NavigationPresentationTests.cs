using System.Collections.Concurrent;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Core.Tests;

public class NavigationPresentationTests
{
   [Fact]
   public void OrdinaryBodiesKeepTheDefaultChrome() {
      var observer = new NavigationPresentationObserver(action => action());
      Assert.Null(observer.Source);
      observer.Observe(new NavigationPresentation());
      Assert.Null(observer.Source!.Title);
      Assert.Null(observer.Source.Subtitle);
      Assert.False(observer.Source.UseCompactAppBar);
      Assert.False(observer.Source.HideBottomNavigation);
   }

   [Fact]
   public void SwitchingToAnOrdinaryBodyRestoresAllDefaults() {
      var observer = new NavigationPresentationObserver(action => action());
      var entry = new NavigationPresentation { Title = "Capture", Subtitle = "Context", UseCompactAppBar = true, HideBottomNavigation = true };
      observer.Observe(entry);
      Assert.Same(entry, observer.Source);
      observer.Observe(null);
      Assert.Null(observer.Source);
      entry.Title = "Late title";
      entry.HideBottomNavigation = false;
      Assert.Null(observer.Source);
   }

   [Fact]
   public void AnInactiveEntryCannotInvalidateTheCurrentChrome() {
      var queue = new Queue<Action>();
      var observer = new NavigationPresentationObserver(queue.Enqueue);
      var previous = new NavigationPresentation();
      var current = new NavigationPresentation { Title = "Current", HideBottomNavigation = true };
      observer.Observe(previous);
      previous.Title = "Queued before leaving";
      observer.Observe(current);
      var notifications = 0;
      observer.Changed += () => notifications++;
      previous.Subtitle = "Changed after leaving";
      while (queue.TryDequeue(out var action)) action();
      Assert.Equal(0, notifications);
      Assert.Same(current, observer.Source);
      Assert.Equal("Current", observer.Source!.Title);
      Assert.True(observer.Source.HideBottomNavigation);
   }

   [Fact]
   public void ReturningToTheSameBodyDoesNotReplayAnEarlierVisit() {
      var queue = new Queue<Action>();
      var observer = new NavigationPresentationObserver(queue.Enqueue);
      var entry = new NavigationPresentation();
      observer.Observe(entry);
      entry.Title = "First visit";
      observer.Observe(null);
      observer.Observe(entry);
      var notifications = 0;
      observer.Changed += () => notifications++;
      while (queue.TryDequeue(out var action)) action();
      Assert.Equal(0, notifications);
      entry.Title = "Second visit";
      while (queue.TryDequeue(out var action)) action();
      Assert.Equal(1, notifications);
   }

   [Fact]
   public void DetachingTheHostDropsPendingCallbacksAndSubscriptions() {
      var queue = new Queue<Action>();
      var observer = new NavigationPresentationObserver(queue.Enqueue);
      var entry = new NavigationPresentation();
      observer.Observe(entry);
      entry.HideBottomNavigation = true;
      observer.Observe(null);
      var notifications = 0;
      observer.Changed += () => notifications++;
      while (queue.TryDequeue(out var action)) action();
      entry.HideBottomNavigation = false;
      Assert.Empty(queue);
      Assert.Equal(0, notifications);
   }

   [Fact]
   public void SwitchingBetweenOptedBodiesUsesTheCurrentBodyOnly() {
      var observer = new NavigationPresentationObserver(action => action());
      var first = new NavigationPresentation { Title = "First", Subtitle = "First context", UseCompactAppBar = true, HideBottomNavigation = true };
      var second = new NavigationPresentation { Title = "Second" };
      observer.Observe(first);
      observer.Observe(second);
      Assert.Same(second, observer.Source);
      Assert.Equal("Second", observer.Source!.Title);
      Assert.Null(observer.Source.Subtitle);
      Assert.False(observer.Source.UseCompactAppBar);
      Assert.False(observer.Source.HideBottomNavigation);
   }

   [Fact]
   public void ClosingTheKeyboardOrSheetRestoresTabsOnTheActiveBody() {
      var observer = new NavigationPresentationObserver(action => action());
      var entry = new NavigationPresentation { UseCompactAppBar = true };
      observer.Observe(entry);
      entry.HideBottomNavigation = true;
      Assert.True(observer.Source!.HideBottomNavigation);
      entry.HideBottomNavigation = false;
      Assert.False(observer.Source.HideBottomNavigation);
      Assert.True(observer.Source.UseCompactAppBar);
   }

   [Fact]
   public void ExplicitEmptySubtitleIsDifferentFromFallback() {
      var entry = new NavigationPresentation();
      Assert.Null(entry.Subtitle);
      entry.Subtitle = "";
      Assert.Equal("", entry.Subtitle);
   }

   [Fact]
   public async Task BackgroundNotificationIsDeliveredOnlyOnTheOwnerThread() {
      using var queue = new BlockingCollection<Action>();
      var ready = new TaskCompletionSource<NavigationPresentation>(TaskCreationOptions.RunContinuationsAsynchronously);
      var delivered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
      var ownerId = 0;
      var thread = new Thread(() => {
         ownerId = Environment.CurrentManagedThreadId;
         foreach (var action in queue.GetConsumingEnumerable()) action();
      }) { IsBackground = true };
      if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
      thread.Start();
      try {
         queue.Add(() => {
            var observer = new NavigationPresentationObserver(action => queue.Add(action));
            var entry = new NavigationPresentation();
            observer.Observe(entry);
            observer.Changed += () => delivered.TrySetResult(Environment.CurrentManagedThreadId);
            ready.SetResult(entry);
         });
         var source = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
         var workerId = await Task.Run(() => { source.Title = "Background update"; return Environment.CurrentManagedThreadId; });
         Assert.Equal(ownerId, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
         Assert.NotEqual(ownerId, workerId);
      }
      finally { queue.CompleteAdding(); Assert.True(thread.Join(TimeSpan.FromSeconds(5))); }
   }
}
