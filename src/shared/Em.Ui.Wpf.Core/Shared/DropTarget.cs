using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Membuat sebuah elemen bisa menerima jatuhan dari <see cref="DragSource"/>, cukup dengan
   /// menyerahkan command yang mengerjakannya di XAML:
   /// <code>&lt;Grid shared:DropTarget.Command="{Binding Commands[GrantDroppedCommand]}"&gt;</code>
   /// <para>
   /// Tidak ada penyaringan tipe di sini: yang menjawab boleh atau tidaknya sebuah muatan mendarat
   /// adalah <c>CanExecute</c> command-nya sendiri, dan jawaban itu sekaligus yang menentukan
   /// bentuk kursornya. Menaruh aturan yang sama di dua tempat berarti dua tempat yang bisa
   /// berselisih.
   /// </para>
   /// </summary>
   public static class DropTarget
   {
      #region Command

      /// <summary>
      /// Command yang dijalankan saat sebuah muatan dijatuhkan di elemen ini, dengan muatannya
      /// sebagai parameter. Memasangnya sekaligus menyalakan <see cref="UIElement.AllowDrop"/>.
      /// </summary>
      public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
         "Command",
         typeof(ICommand),
         typeof(DropTarget),
         new PropertyMetadata(null, OnCommandChanged));

      /// <summary>Membaca command penerima jatuhan yang terpasang pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dibaca.</param>
      /// <returns>Command-nya, atau <c>null</c> kalau elemen ini bukan tempat jatuhan.</returns>
      public static ICommand? GetCommand(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (ICommand?)element.GetValue(CommandProperty);
      }

      /// <summary>Memasang command penerima jatuhan pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dipasangi.</param>
      /// <param name="value">Command-nya; <c>null</c> berarti elemen ini berhenti menerima jatuhan.</param>
      public static void SetCommand(DependencyObject element, ICommand? value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(CommandProperty, value);
      }

      #endregion

      #region Target

      /// <summary>
      /// Sasaran jatuhan pada elemen ini, biasanya <c>{Binding}</c> sebuah baris. Kalau diisi,
      /// parameter command-nya menjadi <see cref="DropRequest"/> yang membawa muatan sekaligus
      /// sasaran ini; kalau kosong, parameternya muatan itu sendiri.
      /// </summary>
      public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
         "Target",
         typeof(object),
         typeof(DropTarget),
         new PropertyMetadata(null));

      /// <summary>Membaca sasaran jatuhan pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dibaca.</param>
      /// <returns>Sasarannya, atau <c>null</c>.</returns>
      public static object? GetTarget(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return element.GetValue(TargetProperty);
      }

      /// <summary>Memasang sasaran jatuhan pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dipasangi.</param>
      /// <param name="value">Sasarannya.</param>
      public static void SetTarget(DependencyObject element, object? value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(TargetProperty, value);
      }

      #endregion

      #region Effect

      /// <summary>
      /// Bentuk kursor saat muatan boleh mendarat di sini: <see cref="DragDropEffects.Copy"/>
      /// (bawaan) atau mis. <see cref="DragDropEffects.Move"/> untuk tempat yang memindahkan apa yang
      /// dijatuhkan. Hanya tampilan - yang benar-benar terjadi tetap ditentukan command-nya.
      /// </summary>
      public static readonly DependencyProperty EffectProperty = DependencyProperty.RegisterAttached(
         "Effect",
         typeof(DragDropEffects),
         typeof(DropTarget),
         new PropertyMetadata(DragDropEffects.Copy));

      /// <summary>Membaca bentuk kursor jatuhan pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dibaca.</param>
      /// <returns>Efek yang ditampilkan.</returns>
      public static DragDropEffects GetEffect(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (DragDropEffects)element.GetValue(EffectProperty);
      }

      /// <summary>Memasang bentuk kursor jatuhan pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dipasangi.</param>
      /// <param name="value">Efek yang ditampilkan.</param>
      public static void SetEffect(DependencyObject element, DragDropEffects value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(EffectProperty, value);
      }

      #endregion

      #region IsDraggingOver

      private static readonly DependencyPropertyKey IsDraggingOverKey = DependencyProperty.RegisterAttachedReadOnly(
         "IsDraggingOver",
         typeof(bool),
         typeof(DropTarget),
         new PropertyMetadata(false));

      /// <summary>
      /// Menyala selama ada muatan yang <i>boleh</i> mendarat sedang melayang di atas elemen ini.
      /// Hanya untuk dibaca XAML - inilah yang membuat sorotan area jatuhan bisa digambar lewat
      /// trigger, tanpa satu baris pun keadaan sorotan itu masuk ke view model.
      /// </summary>
      public static readonly DependencyProperty IsDraggingOverProperty = IsDraggingOverKey.DependencyProperty;

      /// <summary>Apakah ada muatan yang boleh mendarat sedang melayang di atas elemen ini.</summary>
      /// <param name="element">Elemen yang dibaca.</param>
      /// <returns><c>true</c> selama muatannya melayang di atas elemen ini.</returns>
      public static bool GetIsDraggingOver(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (bool)element.GetValue(IsDraggingOverProperty);
      }

      #endregion

      private static void OnCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
         if (sender is not UIElement element) return;

         element.DragEnter -= OnDragOver;
         element.DragOver -= OnDragOver;
         element.DragLeave -= OnDragLeave;
         element.Drop -= OnDrop;

         element.AllowDrop = e.NewValue != null;
         if (e.NewValue == null) {
            element.SetValue(IsDraggingOverKey, false);
            return;
         }

         element.DragEnter += OnDragOver;
         element.DragOver += OnDragOver;
         element.DragLeave += OnDragLeave;
         element.Drop += OnDrop;
      }

      // DragOver menyala terus-menerus selama kursor ada di atas elemen ini, jadi ia sekaligus yang
      // menegaskan kembali sorotannya. Itu yang menahan kedipan saat kursor melintasi kartu-kartu di
      // dalamnya: sorotannya dinyalakan ulang lebih cepat daripada mata bisa melihatnya padam.
      private static void OnDragOver(object sender, DragEventArgs e) {
         if (sender is not UIElement element) return;

         var accepted = CanAccept(element, e.Data, out _);

         e.Effects = accepted ? GetEffect(element) : DragDropEffects.None;
         e.Handled = true;
         element.SetValue(IsDraggingOverKey, accepted);
         ClearOuterTargets(element);
      }

      // A target inside another - a folder row inside the list that also takes drops - handles the
      // drag itself, so the outer one hears neither DragOver nor DragLeave while the pointer is over
      // the inner one, and would keep the highlight it had a moment ago. The inner one switches it
      // off on its way through.
      private static void ClearOuterTargets(DependencyObject element) {
         for (var node = VisualTreeHelper.GetParent(element); node != null; node = VisualTreeHelper.GetParent(node)) {
            if (node.GetValue(CommandProperty) != null) node.SetValue(IsDraggingOverKey, false);
         }
      }

      // DragLeave juga menyala saat kursor sekadar berpindah ke anak elemen, jadi keluarnya
      // dipastikan dari koordinat kursornya sendiri - bukan dari event-nya semata.
      private static void OnDragLeave(object sender, DragEventArgs e) {
         if (sender is not FrameworkElement element) return;

         var cursor = e.GetPosition(element);
         if (cursor.X >= 0 && cursor.Y >= 0 &&
             cursor.X <= element.ActualWidth && cursor.Y <= element.ActualHeight) {
            return;
         }

         element.SetValue(IsDraggingOverKey, false);
         e.Handled = true;
      }

      private static void OnDrop(object sender, DragEventArgs e) {
         if (sender is not UIElement element) return;

         element.SetValue(IsDraggingOverKey, false);
         ClearOuterTargets(element);

         if (!CanAccept(element, e.Data, out var payload)) {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
         }

         e.Effects = GetEffect(element);
         e.Handled = true;
         GetCommand(element)?.Execute(payload);
      }

      private static bool CanAccept(UIElement element, IDataObject? data, out object? payload) {
         payload = null;

         if (data == null) return false;

         object? dropped = null;
         if (data.GetDataPresent(DragSource.PayloadFormat)) {
            // A drag of our own that also carries virtual files runs over a COM data object, which
            // cannot hand back the .NET payload - DragSource keeps it for exactly this read.
            dropped = DragSource.ActivePayload ?? data.GetData(DragSource.PayloadFormat);
         }
         else if (data.GetDataPresent(DataFormats.FileDrop) &&
                  data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) {
            dropped = new DroppedFiles(paths);
         }

         if (dropped == null) return false;

         payload = GetTarget(element) is { } target ? new DropRequest(dropped, target) : dropped;
         return GetCommand(element) is { } command && command.CanExecute(payload);
      }
   }
}
