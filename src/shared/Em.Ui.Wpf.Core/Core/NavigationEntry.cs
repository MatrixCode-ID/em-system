using System.ComponentModel;
using System.IO;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Satu tempat di sebuah <see cref="NavigationStack"/>: navigasi yang sedang terbuka berikut body,
   /// data, dan judulnya sendiri. Dibuat oleh stack saat sebuah layar dibuka dengan judul yang belum
   /// ada; module tidak membuatnya sendiri, melainkan menerimanya lewat
   /// <see cref="NavigationEventArgs.Entry"/> atau <see cref="Shared.MvvmModelBase.NavigationEntry"/>.
   /// </summary>
   public sealed class NavigationEntry : INavigationEntry, INotifyPropertyChanged
   {
      private INavigationBody? _body;
      private bool _released;

      internal NavigationEntry(NavigationStack stack, Navigation navigation, string title, object? data) {
         Stack = stack;
         Navigation = navigation;
         Title = title;
         Data = data;
      }

      #region Explicit INavigationEntry Implementation

      INavigation INavigationEntry.Navigation => Navigation;
      INavigationStack INavigationEntry.Stack => Stack;
      Task<bool> INavigationEntry.NavigateTo(INavigation navigation, object? data) =>
         NavigateTo((Navigation)navigation, data);

      #endregion

      /// <summary>Definisi layar yang dibuka entri ini.</summary>
      public Navigation Navigation { get; }

      /// <summary>
      /// Stack yang memegang entri ini. Berganti saat entrinya di-detach ke window sendiri - body dan
      /// isian yang belum disimpan ikut pindah apa adanya, jadi <see cref="NavigateTo(string,object?)"/>
      /// sesudahnya membuka layar di window baru itu.
      /// </summary>
      public NavigationStack Stack { get; internal set; }

      /// <summary>Objek aplikasi pemilik entri ini.</summary>
      public EmApp EmApp => Stack.EmApp;

      /// <summary>
      /// Judul yang tampil, sekaligus kunci unik entri ini di seluruh aplikasi. Diganti lewat
      /// <see cref="SetTitle"/>, bukan ditulis langsung, supaya keunikannya tetap terjaga.
      /// </summary>
      public string Title { get; private set; }

      /// <summary>Parameter yang dipakai saat entri ini dibuka, atau <c>null</c> kalau tidak ada.</summary>
      public object? Data { get; }

      /// <summary>
      /// Body milik entri ini. Entri yang dibuka lewat navigasi sudah membangunnya saat dibuat; hanya
      /// home yang menunggu sampai benar-benar ditampilkan, karena sebelum ada yang masuk body home
      /// memang belum boleh dibangun.
      /// </summary>
      /// <exception cref="InvalidOperationException">Kalau entri ini sudah dilepas dari stack-nya.</exception>
      public INavigationBody Body {
         get {
            // A released entry has left every stack for good; building a fresh body for it here would
            // bring back a control nobody is ever going to release.
            if (_released) throw new InvalidOperationException($"Navigation entry '{Title}' has already been released.");
            return _body ??= Navigation.BodyType.Create(this);
         }
      }

      // Reading Body builds it on demand, so anything that only wants to look at or release what
      // already exists has to ask this first instead of touching the property.
      internal bool HasBody => _body != null;

      /// <inheritdoc />
      public Task Reload() =>
         Body.OnReloadRequested(Navigation, new NavigationEventArgs {
            NavigationItem = Navigation,
            Entry = this,
            Data = Data,
         });

      /// <inheritdoc />
      public bool SetTitle(string title) {
         ArgumentException.ThrowIfNullOrWhiteSpace(title);
         if (string.Equals(Title, title, StringComparison.Ordinal)) return true;

         // The same entry may be renamed to a different casing of its own title, which is still the
         // same key and so must not count as a clash with itself.
         var owner = EmApp.FindEntry(title);
         if (owner != null && owner != this) return false;

         Title = title;
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
         return true;
      }

      /// <inheritdoc />
      public Task<bool> Close() => Stack.Close(this);

      /// <inheritdoc cref="INavigationEntry.NavigateTo(string,object?)" />
      public Task<bool> NavigateTo(string name, object? data = null) => EmApp.NavigateTo(name, data, Stack);

      /// <inheritdoc cref="INavigationEntry.NavigateTo(INavigation,object?)" />
      public Task<bool> NavigateTo(Navigation navigation, object? data = null) =>
         EmApp.NavigateTo(navigation, data, Stack);

      /// <summary>
      /// Membuka PDF di viewer bawaan aplikasi, relatif ke stack entri ini - jadi viewer terbuka di window
      /// yang sama dengan body pemanggilnya. Aturan parameternya sama dengan
      /// <see cref="Core.EmApp.ViewPdf"/>.
      /// </summary>
      /// <param name="title">Judul viewer, sekaligus kunci unik entrinya.</param>
      /// <param name="loader">Pengambil isi PDF; hak atas dokumennya dijaga di sini, bukan oleh viewer.</param>
      /// <param name="fileName">Nama file bawaan saat PDF disimpan, atau <c>null</c> untuk memakai judulnya.</param>
      /// <returns><c>false</c> kalau viewer tidak bisa dibuka.</returns>
      public Task<bool> ViewPdf(string title, Func<CancellationToken, Task<Stream>> loader, string? fileName = null) =>
         NavigateTo(Core.EmApp.PdfViewerNavigationName, new PdfViewerNavigationPayload(title, loader, fileName));

      // Called by the stack once this entry has left it and its body is no longer mounted anywhere.
      // Dropping the reference is what actually frees the control, so OnRelease is only there for
      // what would otherwise outlive it - subscriptions to long-lived publishers, timers, caches.
      internal async Task Release() {
         // The home body is the one control that always stays alive: every path out of the stack
         // lands on it, so it must never be in a state where it has to be rebuilt first.
         if (_released || this == Stack.Home) return;

         _released = true;
         if (_body == null) return;

         // The reference is dropped only after OnRelease has finished, so the body is still whole for
         // as long as it is tearing itself down.
         var body = _body;
         await body.OnRelease(Navigation);
         _body = null;
      }

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;
   }
}
