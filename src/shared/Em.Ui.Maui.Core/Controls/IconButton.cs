using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Em.Ui.Maui.Controls
{
   /// <summary>
   /// Tombol ikon bulat ala Material: sebuah ikon di dalam bidang sentuh 48x48, dengan kilatan singkat
   /// sebagai tanda ia benar-benar tertekan. Dipakai di app bar dan tempat lain yang aksinya cukup
   /// diwakili satu gambar.
   /// </summary>
   /// <remarks>
   /// Ditulis sebagai control sendiri, bukan <c>Button</c> ber-gambar, karena <c>Button</c> di MAUI hanya
   /// menerima <c>ImageSource</c> - sementara ikon di sini digambar sebagai huruf font supaya warnanya
   /// bisa ikut tema tanpa menyiapkan satu berkas gambar per warna.
   /// </remarks>
   public class IconButton : ContentView
   {
      // Lapisan keadaan saat penunjuk berada di atas tombol. Abu-abu tembus pandang, bukan warna
      // tertentu: ia menggelapkan tema terang dan mencerahkan tema gelap, jadi satu nilai cukup untuk
      // keduanya - aturan yang sama dipakai seluruh bahasa desain di repo ini.
      private static readonly Color HoverColor = Color.FromRgba(128, 128, 128, 31);

      private readonly FontIcon _icon;
      private readonly Border _surface;
      private readonly RoundRectangle _surfaceShape;

      public IconButton() {
         _icon = new FontIcon {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
         };
         _surfaceShape = new RoundRectangle { CornerRadius = 24 };
         _surface = new Border {
            Padding = 0,
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            StrokeShape = _surfaceShape,
            Content = _icon
         };
         Content = _surface;
         WidthRequest = HeightRequest = 48;

         // Bidangnya selalu bulat penuh, berapa pun ukuran tombolnya - dan ukurannya memang diubah di
         // beberapa tempat pemakaian, jadi radiusnya dihitung ulang bukan disetel sekali.
         SizeChanged += (_, _) => _surfaceShape.CornerRadius = Math.Min(Width, Height) / 2;

         var tap = new TapGestureRecognizer();
         tap.Tapped += OnTapped;
         GestureRecognizers.Add(tap);

         // Hover hanya ada di perangkat yang punya penunjuk - mouse di desktop, atau stylus. Di layar
         // sentuh kedua event ini tidak pernah datang, dan yang memberi tanda di sana adalah kilatan
         // di OnTapped. Keduanya dipasang bersama supaya control ini tidak perlu tahu ia sedang
         // dijalankan di perangkat yang mana.
         var pointer = new PointerGestureRecognizer();
         pointer.PointerEntered += (_, _) => _surface.BackgroundColor = HoverColor;
         pointer.PointerExited += (_, _) => _surface.BackgroundColor = Colors.Transparent;
         GestureRecognizers.Add(pointer);
      }

      /// <summary>Karakter glyph ikon, diambil dari <see cref="FontIcons"/>.</summary>
      public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
         nameof(Glyph), typeof(string), typeof(IconButton), string.Empty,
         propertyChanged: (b, _, n) => ((IconButton)b)._icon.Glyph = (string)n);

      /// <inheritdoc cref="GlyphProperty" />
      public string Glyph {
         get => (string)GetValue(GlyphProperty);
         set => SetValue(GlyphProperty, value);
      }

      /// <summary>Warna ikon.</summary>
      public static readonly BindableProperty TintColorProperty = BindableProperty.Create(
         nameof(TintColor), typeof(Color), typeof(IconButton), Colors.Black,
         propertyChanged: (b, _, n) => ((IconButton)b)._icon.TintColor = (Color)n);

      /// <inheritdoc cref="TintColorProperty" />
      public Color TintColor {
         get => (Color)GetValue(TintColorProperty);
         set => SetValue(TintColorProperty, value);
      }

      /// <summary>Panjang sisi ikonnya sendiri, bukan bidang sentuhnya. Bawaannya 24.</summary>
      public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
         nameof(IconSize), typeof(double), typeof(IconButton), FontIcons.DefaultSize,
         propertyChanged: (b, _, n) => ((IconButton)b)._icon.IconSize = (double)n);

      /// <inheritdoc cref="IconSizeProperty" />
      public double IconSize {
         get => (double)GetValue(IconSizeProperty);
         set => SetValue(IconSizeProperty, value);
      }

      /// <summary>Perintah yang dijalankan saat tombol ditekan.</summary>
      public static readonly BindableProperty CommandProperty = BindableProperty.Create(
         nameof(Command), typeof(ICommand), typeof(IconButton));

      /// <inheritdoc cref="CommandProperty" />
      public ICommand? Command {
         get => (ICommand?)GetValue(CommandProperty);
         set => SetValue(CommandProperty, value);
      }

      /// <summary>Parameter yang diteruskan ke <see cref="Command"/>.</summary>
      public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
         nameof(CommandParameter), typeof(object), typeof(IconButton));

      /// <inheritdoc cref="CommandParameterProperty" />
      public object? CommandParameter {
         get => GetValue(CommandParameterProperty);
         set => SetValue(CommandParameterProperty, value);
      }

      private async void OnTapped(object? sender, TappedEventArgs e) {
         if (Command is not { } command || !command.CanExecute(CommandParameter)) return;

         // Kilatan singkat sebagai ganti efek riak bawaan platform, yang tidak tersedia untuk control
         // yang menggambar dirinya sendiri seperti ini. Sengaja dijalankan sampai selesai sebelum
         // perintahnya berjalan: perpindahan layar mengganti seluruh isi halaman, dan animasi yang
         // masih berjalan di atas control yang sudah dibuang tidak pernah selesai.
         await this.FadeToAsync(0.4, 60);
         await this.FadeToAsync(1, 90);
         command.Execute(CommandParameter);
      }
   }
}
