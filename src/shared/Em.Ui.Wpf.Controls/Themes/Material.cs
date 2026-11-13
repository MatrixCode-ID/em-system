using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Em.Ui.Wpf.Controls;

internal static class Material
{
    private const string ThemePath = "/Em.Ui.Wpf.Controls;component/Themes/";
    [ThreadStatic] private static ResourceDictionary? _styles;

    private static ResourceDictionary Styles => _styles ??= Load("Material.xaml");

    public static ResourceDictionary Load(string file) => new() { Source = new Uri(ThemePath + file, UriKind.Relative) };

    public static void Scope(FrameworkElement element) =>
        element.Resources.MergedDictionaries.Add(Load("MaterialScope.xaml"));

    public static Style Style(string name) => (Style)Styles["Material." + name];

    public static string FileIcon(string format) => format.ToLowerInvariant() switch
    {
        "pdf" => "FilePdf",
        "xlsx" => "FileSheet",
        "docx" => "FileDoc",
        "csv" => "FileCsv",
        _ => "Export"
    };

    public static void Paint(FrameworkElement element, DependencyProperty property, string role) =>
        element.SetResourceReference(property, "MatrixGrid.Brush." + role);

    public static Button Button(string label, string style = "Button") => new() { Content = label, Style = Style(style) };

    public static Button IconButton(string icon, string name, double size = 32)
    {
        var button = new Button
        {
            Style = Style("IconButton"), Content = Icon(icon), Width = size, Height = size, ToolTip = name
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    public static FrameworkElement Icon(string name, double size = 20, string? role = null)
    {
        role ??= name switch
        {
            "FilterActive" or "ArrowUpActive" => "Primary",
            "TrashError" => "Error",
            "CheckOnPrimary" => "OnPrimary",
            _ => "OnSurfaceVariant"
        };
        var canvas = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
        if (name is "SortNone" or "SortAsc" or "SortDesc")
        {
            AddPath("SortUp", name == "SortAsc" ? "Success" : "Outline", 1.2, name == "SortAsc");
            AddPath("SortDown", name == "SortDesc" ? "Success" : "Outline", 1.2, name == "SortDesc");
        }
        else if (Styles[name] is Geometry)
            AddPath(name, role, name == "DragIndicator" ? 2.6 : 1.6, false);
        else if (Em.Ui.Wpf.Controls.EditorToolbar.Geometry(name) is { } editorIcon)
        {
            // Editor toolbar outlines, so toolbar drop-downs can use the same icons as their buttons.
            var path = new Path
            {
                Data = editorIcon, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
            Paint(path, Shape.StrokeProperty, role);
            canvas.Children.Add(path);
        }
        else
        {
            // Consumer-defined menus may still supply a literal glyph.
            var glyph = new TextBlock { Text = name, Width = 20, TextAlignment = TextAlignment.Center, FontSize = 14 };
            Paint(glyph, TextBlock.ForegroundProperty, role);
            canvas.Children.Add(glyph);
        }
        return new Viewbox { Child = canvas, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

        void AddPath(string key, string brush, double stroke, bool fill)
        {
            var path = new Path
            {
                Data = (Geometry)Styles[key], StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
            Paint(path, Shape.StrokeProperty, brush);
            if (fill)
                Paint(path, Shape.FillProperty, brush);
            canvas.Children.Add(path);
        }
    }

    public static Border Panel(UIElement content, string role, double radius, Thickness padding, bool dialog = false)
    {
        var panel = new Border
        {
            Child = content, CornerRadius = new CornerRadius(radius), Padding = padding,
            Effect = new DropShadowEffect { BlurRadius = dialog ? 14 : 8, ShadowDepth = dialog ? 4 : 2, Opacity = 0.23 }
        };
        Paint(panel, Border.BackgroundProperty, role);
        Paint(panel, TextElement.ForegroundProperty, "OnSurface");
        TextElement.SetFontFamily(panel, (FontFamily)Styles["MatrixGrid.FontFamily"]);
        TextElement.SetFontSize(panel, 14);
        return panel;
    }

    public static Window Dialog(FrameworkElement source, string title, UIElement content, double width)
    {
        var window = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize
        };
        ConfigureDialog(window, source);
        var body = new DockPanel();
        var heading = new TextBlock { Text = title, FontSize = 24, Margin = new Thickness(0, 0, 0, 16) };
        heading.MouseLeftButtonDown += (_, _) => window.DragMove();
        DockPanel.SetDock(heading, Dock.Top);
        body.Children.Add(heading);
        body.Children.Add(content);
        SetDialogContent(window, body, "SurfaceContainerHigh", new Thickness(24));
        return window;
    }

    public static void ConfigureDialog(Window window, FrameworkElement? source)
    {
        var owner = source as Window ?? (source is null ? null : Window.GetWindow(source));
        window.Owner = owner;
        window.WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        window.ShowInTaskbar = false;
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.FontFamily = (FontFamily)Styles["MatrixGrid.FontFamily"];
        window.FontSize = 14;
        Paint(window, Control.ForegroundProperty, "OnSurface");
        GridTheme.SetMode(window, source is null ? GridThemeMode.Light : GridTheme.GetMode(source));
        Scope(window);
        window.Loaded += (_, _) =>
        {
            if (window.Owner?.Content is not UIElement ownerContent || AdornerLayer.GetAdornerLayer(ownerContent) is not { } layer
                || window.TryFindResource("MatrixGrid.Brush.Scrim") is not Brush scrim)
                return;
            var overlay = new Scrim(ownerContent, scrim);
            layer.Add(overlay);
            window.Closed += (_, _) => layer.Remove(overlay);
        };
    }

    public static void SetDialogContent(Window window, UIElement content, string role, Thickness padding)
    {
        var panel = Panel(content, role, 28, padding, dialog: true);
        panel.Margin = new Thickness(12);
        window.Content = panel;
    }

    private sealed class Scrim(UIElement target, Brush brush) : Adorner(target)
    {
        protected override void OnRender(DrawingContext context) => context.DrawRectangle(brush, null, new Rect(RenderSize));
        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
    }
}
