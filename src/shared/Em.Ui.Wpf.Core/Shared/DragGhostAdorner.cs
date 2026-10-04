using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Gambar bayangan yang ikut terseret bersama kursor: replika sebuah elemen, digambar di atas
   /// adorner layer dan dipindahkan lewat <see cref="Offset"/> selama drag berjalan.
   /// <para>
   /// Adorner ini tidak tahu apa pun tentang isi yang sedang diseret - ia hanya menyalin rupa
   /// elemen sumbernya. Itu yang membuatnya bisa dipakai layar mana pun: yang berbeda antar layar
   /// adalah <i>apa</i> yang diseret, bukan bagaimana bayangannya digambar.
   /// </para>
   /// </summary>
   public sealed class DragGhostAdorner : Adorner
   {
      private readonly VisualCollection _visuals;
      private readonly Rectangle _ghost;
      private readonly TranslateTransform _position = new();
      private readonly Point _grip;

      /// <summary>
      /// Membuat bayangan baru untuk sebuah elemen.
      /// </summary>
      /// <param name="adornedElement">
      /// Elemen yang di-adorn - biasanya akar layar. Seluruh posisi yang diberikan ke
      /// <see cref="Offset"/> dihitung relatif terhadap elemen ini.
      /// </param>
      /// <param name="source">Elemen yang direplika; rupanya disalin apa adanya berikut ukurannya.</param>
      /// <param name="grip">
      /// Titik pegangan: di sebelah mana dalam <paramref name="source"/> tombol mouse ditekan.
      /// Tanpa ini bayangannya akan melompat ke pojok kursor begitu drag dimulai.
      /// </param>
      public DragGhostAdorner(UIElement adornedElement, FrameworkElement source, Point grip) : base(adornedElement) {
         ArgumentNullException.ThrowIfNull(source);

         // Wadah anaknya diisi lebih dulu daripada apa pun. IsHitTestVisible di bawah adalah
         // property yang dipaksa turun ke seluruh anak, jadi menyetelnya membuat WPF menelusuri
         // anak-anak objek ini saat itu juga - lewat VisualChildrenCount, yang membaca field ini.
         _visuals = new VisualCollection(this);
         _grip = grip;

         // Kalau bayangannya ikut kena hit-test, dialah yang selalu tertabrak lebih dulu dan drop
         // tidak akan pernah menemukan targetnya - kursornya selalu "di atas" bayangan sendiri.
         IsHitTestVisible = false;

         _ghost = new Rectangle {
            Width = source.RenderSize.Width,
            Height = source.RenderSize.Height,
            RadiusX = 10,
            RadiusY = 10,
            Opacity = 0.75,
            IsHitTestVisible = false,
            RenderTransform = _position,
            Fill = new VisualBrush(source),
            Effect = new DropShadowEffect {
               BlurRadius = 16,
               ShadowDepth = 3,
               Opacity = 0.35,
               Color = Colors.Black
            }
         };

         _visuals.Add(_ghost);
      }

      /// <summary>
      /// Memindahkan bayangan ke posisi kursor sekarang, dalam koordinat elemen yang di-adorn.
      /// Titik pegangan dikurangkan sendiri, jadi pemanggil cukup menyerahkan posisi kursornya.
      /// </summary>
      /// <param name="position">Posisi kursor relatif terhadap elemen yang di-adorn.</param>
      public void Offset(Point position) {
         _position.X = position.X - _grip.X;
         _position.Y = position.Y - _grip.Y;
      }

      /// <inheritdoc />
      protected override int VisualChildrenCount => _visuals.Count;

      /// <inheritdoc />
      protected override Visual GetVisualChild(int index) => _visuals[index];

      /// <inheritdoc />
      protected override Size MeasureOverride(Size constraint) {
         _ghost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
         return _ghost.DesiredSize;
      }

      /// <inheritdoc />
      protected override Size ArrangeOverride(Size finalSize) {
         _ghost.Arrange(new Rect(new Point(0, 0), _ghost.DesiredSize));
         return finalSize;
      }
   }
}
