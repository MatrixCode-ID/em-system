using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Em.Documents.Formats;
using Em.Documents.Model;
using Em.Documents.Pdf;
using Em.Editing;
using Em.Ui.Wpf.Controls;
using Em.Controls.Write.Editing;
using Em.Controls.Write.IO;
using Em.Controls.Write.Model;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Controls;

/// <summary>A word-processing editor for WriteDocument documents, with paged layout, rulers, tables, headers and footers, DOCX and PDF files, and a compact toolbar or a ribbon.</summary>
public sealed partial class MatrixWriter
{
    /// <inheritdoc/>
    protected override bool IsCommandEnabled(string id) => id switch
    {
        "undo" => Session.CanUndo,
        "redo" => Session.CanRedo,
        "previous-page" => page > 1,
        "next-page" => page < pages.Length,
        "paste-text" => Clipboard.ContainsText(),
        "change-case" or "case-sentence" or "case-lower" or "case-upper" or "case-title" or "case-toggle" => !Session.Selection.IsEmpty,
        "insert-table" or "table-2x2" or "table-3x3" or "table-4x4" or "table-custom" or "page-break" or "section-break" =>
            Session.Selection.Caret.Row < 0 && Session.Selection.Caret.Story == WriteStory.Body,
        _ => WriteCommands.Catalog.FirstOrDefault(command => command.Id == id)?.RibbonTab switch
        {
            WriteCommands.TableDesignTab or WriteCommands.TableLayoutTab => Session.Selection.Caret.Row >= 0,
            WriteCommands.HeaderFooterTab => Session.Selection.Caret.Story != WriteStory.Body,
            _ => true
        }
    };

    private static readonly Dictionary<string, uint?> NamedColors = new(StringComparer.Ordinal)
    {
        ["highlight-yellow"] = 0xFFFF00, ["highlight-green"] = 0x00FF00, ["highlight-turquoise"] = 0x00FFFF,
        ["highlight-pink"] = 0xFF00FF, ["highlight-none"] = null,
        ["color-auto"] = null, ["color-red"] = 0xC00000, ["color-blue"] = 0x0070C0, ["color-green"] = 0x00B050,
        ["shading-none"] = null, ["shading-gray"] = 0xD9D9D9, ["shading-blue"] = 0xDEEAF6, ["shading-green"] = 0xE2EFD9,
        ["shading-gold"] = 0xFFF2CC
    };

    private uint lastHighlight = 0xFFFF00;
    private uint lastColor = 0xC00000;
    private uint? lastShading = 0xD9D9D9;

    /// <inheritdoc/>
    protected override void RunCommand(string id)
    {
        var pos = Session.Selection.Caret;
        var settings = Session.Document.Sections[pos.Section].Page;
        switch (id)
        {
            case "open":
                var dialog = new OpenFileDialog { Filter = "Word document|*.docx" };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                    Source = dialog.FileName;
                break;
            case "save": Save(false); break;
            case "export": Save(true); break;
            case "print": Print(); break;
            case "copy":
            case "cut":
                copied = Session.Copy();
                var runs = copied.Paragraphs.SelectMany(paragraph => paragraph.Runs).ToArray();
                if (runs is [{ Image: { } image }])
                    WriteImageClipboard.Copy(image);
                else if (copied.Text.Length > 0)
                    Clipboard.SetText(copied.Text);
                if (id == "cut") Session.Cut();
                break;
            case "paste":
                if (Clipboard.ContainsImage() && WriteImageClipboard.Read() is { } clipboardImage)
                    Session.InsertRun(new("", Image: clipboardImage));
                else if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (copied?.Text == text)
                        Session.Paste(copied);
                    else
                        Session.InsertText(text);
                }
                break;
            case "undo": Session.Undo(); break;
            case "redo": Session.Redo(); break;
            case "select-all": Session.SelectAll(); break;
            case "find": ShowFind(); break;
            case "previous-page": Navigate(page - 1); break;
            case "next-page": Navigate(page + 1); break;
            case "go-page":
                if (int.TryParse(Ask("Go to page", "Page", page.ToString(CultureInfo.InvariantCulture)), out int target))
                    Navigate(target);
                break;
            case "zoom-in": SetZoom(Math.Min(MaxZoom, zoom + .1)); break;
            case "zoom-out": SetZoom(Math.Max(MinZoom, zoom - .1)); break;
            case "paste-text":
                if (Clipboard.ContainsText())
                    Session.InsertText(Clipboard.GetText());
                break;
            case "grow-font" or "shrink-font":
            {
                double current = Math.Round((CaretFormat().Characters.FontSize ?? 14.667) * 3 / 4 * 2) / 2;
                double next = id == "grow-font" ? FontSizes.FirstOrDefault(size => size > current, Math.Min(current + 10, 1638)) :
                    FontSizes.LastOrDefault(size => size < current, Math.Max(1, current - 1));
                Session.FormatCharacters(style => style with { FontSize = next * 4 / 3 });
                break;
            }
            case "case-sentence": Session.ChangeCase(WriteCaseMode.Sentence); break;
            case "case-lower": Session.ChangeCase(WriteCaseMode.Lower); break;
            case "case-upper": Session.ChangeCase(WriteCaseMode.Upper); break;
            case "case-title": Session.ChangeCase(WriteCaseMode.Title); break;
            case "case-toggle": Session.ChangeCase(WriteCaseMode.Toggle); break;
            case "clear-formatting": Session.FormatCharacters(_ => new()); break;
            case "highlight": Session.FormatCharacters(style => style with { Highlight = lastHighlight }); break;
            case "color": Session.FormatCharacters(style => style with { Color = lastColor }); break;
            case "color-custom":
                if (Color(Ask("Font color", "RGB", lastColor.ToString("X6", CultureInfo.InvariantCulture))) is { } custom)
                {
                    lastColor = custom;
                    Session.FormatCharacters(style => style with { Color = custom });
                }
                break;
            case "spacing-1" or "spacing-115" or "spacing-15" or "spacing-2" or "spacing-25" or "spacing-3":
            {
                double spacing = id switch { "spacing-115" => 1.15, "spacing-15" => 1.5, "spacing-2" => 2, "spacing-25" => 2.5, "spacing-3" => 3, _ => 1 };
                Session.FormatParagraphs(style => style with { LineSpacing = spacing });
                break;
            }
            case "space-before": Session.FormatParagraphs(style => style with { SpaceBefore = style.SpaceBefore > 0 ? 0 : 16 }); break;
            case "space-after": Session.FormatParagraphs(style => style with { SpaceAfter = 0 }); break;
            case "style-title": Session.ApplyStyle("Title", WriteStyleKind.Paragraph); break;
            case "style-heading2": Session.ApplyStyle("Heading2", WriteStyleKind.Paragraph); break;
            case "style-heading3": Session.ApplyStyle("Heading3", WriteStyleKind.Paragraph); break;
            case "replace":
                ShowFind();
                findReplacement?.Focus();
                break;
            case "select-paragraph":
                if (Session.GetParagraph(pos) is { } paragraphAt)
                    Session.Select(new(pos with { Offset = 0 }, pos with { Offset = paragraphAt.Length }));
                break;
            case "table-2x2": Session.InsertTable(2, 2); break;
            case "table-3x3": Session.InsertTable(3, 3); break;
            case "table-4x4": Session.InsertTable(4, 4); break;
            case "table-custom": RunCommand("insert-table"); return;
            case "insert-date": Session.InsertText(DateTime.Now.ToString("D", CultureInfo.CurrentCulture)); break;
            case "margins-normal": Session.SetPage(settings with { Left = 96, Right = 96, Top = 96, Bottom = 96 }); break;
            case "margins-narrow": Session.SetPage(settings with { Left = 48, Right = 48, Top = 48, Bottom = 48 }); break;
            case "margins-moderate": Session.SetPage(settings with { Left = 72, Right = 72, Top = 96, Bottom = 96 }); break;
            case "margins-wide": Session.SetPage(settings with { Left = 192, Right = 192, Top = 96, Bottom = 96 }); break;
            case "orientation-portrait" or "orientation-landscape":
                if (settings.Width > settings.Height != (id == "orientation-landscape"))
                    Session.SetPage(settings with { Width = settings.Height, Height = settings.Width });
                break;
            case "size-letter" or "size-legal" or "size-a4" or "size-a5" or "size-executive":
            {
                var (paperWidth, paperHeight) = id switch
                {
                    "size-legal" => (816d, 1344d), "size-a4" => (793.7, 1122.5), "size-a5" => (559.4, 793.7),
                    "size-executive" => (696d, 1008d), _ => (816d, 1056d)
                };
                if (settings.Width > settings.Height)
                    (paperWidth, paperHeight) = (paperHeight, paperWidth);
                Session.SetPage(settings with { Width = paperWidth, Height = paperHeight });
                break;
            }
            case "table-shading": Session.FormatCells(cell => cell with { Fill = lastShading }, false); break;
            case "shading-table": Session.FormatCells(cell => cell with { Fill = lastShading }, true); break;
            case "borders-all": Session.FormatCells(cell => cell with { NoBorder = false }, true); break;
            case "borders-none": Session.FormatCells(cell => cell with { NoBorder = true }, true); break;
            case "table-borders" or "borders-cell": Session.FormatCells(cell => cell with { NoBorder = false }, false); break;
            case "borders-cell-none": Session.FormatCells(cell => cell with { NoBorder = true }, false); break;
            case "table-delete": Session.DeleteTable(); break;
            case "split-table": Session.SplitTable(); break;
            case "distribute-columns" or "autofit-fixed": Session.DistributeColumns(); break;
            case "autofit-window": Session.DistributeColumns(settings.Width - settings.Left - settings.Right); break;
            case "table-to-text": Session.TableToText(); break;
            case "zoom-100": SetZoom(1); break;
            case "one-page" or "page-width":
                if (scroll is { ViewportWidth: > 0 } view)
                {
                    double pageWidth = settings.Width + PageGap * 2, pageHeight = settings.Height + PageGap * 2;
                    SetZoom(id == "page-width" ? view.ViewportWidth / pageWidth : Math.Min(view.ViewportWidth / pageWidth, view.ViewportHeight / pageHeight));
                }
                break;
            case "ruler":
                showRuler = !showRuler;
                Refresh();
                break;
            case "font":
                if (Ask("Font", "Family", "Noto Sans") is { Length: > 0 } family)
                    Session.FormatCharacters(style => style with { FontFamily = family });
                break;
            case "size":
                if (Number(Ask("Font size", "DIP", "15")) is > 0 and <= 240 and var size)
                    Session.FormatCharacters(style => style with { FontSize = size });
                break;
            case "bold": Session.FormatCharacters(style => style with { Bold = style.Bold != true }); break;
            case "italic": Session.FormatCharacters(style => style with { Italic = style.Italic != true }); break;
            case "underline": Session.FormatCharacters(style => style with { Underline = style.Underline != true }); break;
            case "strike": Session.FormatCharacters(style => style with { Strike = style.Strike != true }); break;
            case "superscript": Session.FormatCharacters(style => style with { Baseline = style.Baseline == WriteBaseline.Superscript ? WriteBaseline.Normal : WriteBaseline.Superscript }); break;
            case "subscript": Session.FormatCharacters(style => style with { Baseline = style.Baseline == WriteBaseline.Subscript ? WriteBaseline.Normal : WriteBaseline.Subscript }); break;
            case { } named when NamedColors.TryGetValue(named, out var value):
                if (named.StartsWith("highlight-", StringComparison.Ordinal))
                {
                    if (value is { } marker) lastHighlight = marker;
                    Session.FormatCharacters(style => style with { Highlight = value });
                }
                else if (named.StartsWith("color-", StringComparison.Ordinal))
                {
                    if (value is { } chosen) lastColor = chosen;
                    Session.FormatCharacters(style => style with { Color = value });
                }
                else
                {
                    lastShading = value;
                    Session.FormatCells(cell => cell with { Fill = value }, false);
                }
                break;
            case "left" or "center" or "right" or "justify":
                Session.FormatParagraphs(style => style with { Alignment = Enum.Parse<WriteAlignment>(id, true) });
                break;
            case "indent-in" or "indent-out":
                Session.FormatParagraphs(style => style with { LeftIndent = Math.Max(0, (style.LeftIndent ?? 0) + (id == "indent-in" ? 24 : -24)) });
                break;
            case "bullets": Session.SetList(WriteListKind.Bullet); break;
            case "numbering": Session.SetList(WriteListKind.Decimal); break;
            case "paragraph": ParagraphDialog(); break;
            case "style-normal": Session.ApplyStyle("Normal", WriteStyleKind.Paragraph); break;
            case "style-heading": Session.ApplyStyle("Heading1", WriteStyleKind.Paragraph); break;
            case "styles": Styles(); break;
            case "insert-table":
                var dims = Ask("Insert table", "Rows × columns", "2x2")?.Split('x', '×');
                if (dims is { Length: 2 } && int.TryParse(dims[0], out int rows) && int.TryParse(dims[1], out int columns))
                    Session.InsertTable(rows, columns);
                break;
            case "insert-image": InsertImage(); break;
            case "resize-image":
                var img = Ask("Resize image", "Width × height (DIP)", "160x120")?.Split('x', '×');
                if (img is { Length: 2 } && Number(img[0]) is { } width && Number(img[1]) is { } height)
                    Session.ResizeImage(width, height);
                break;
            case "page-break": Session.InsertBreak(false); break;
            case "section-break": Session.InsertBreak(true); break;
            case "page-setup": PageSetupDialog(settings); break;
            case "page-number" or "page-count":
                if (pos.Story != WriteStory.Body || Session.EditStory(WriteStory.Footer, pos.Section).Accepted)
                    Session.InsertRun(new("", Field: id == "page-number" ? WriteField.Page : WriteField.PageCount));
                break;
            case "header": Session.EditStory(WriteStory.Header, pos.Section); break;
            case "footer": Session.EditStory(WriteStory.Footer, pos.Section); break;
            case "close-story": Session.EditStory(WriteStory.Body, pos.Section); break;
            case "first-page": Session.SetPage(settings with { DifferentFirstPage = !settings.DifferentFirstPage }); break;
            case "even-pages": Session.SetPage(settings with { DifferentEvenPages = !settings.DifferentEvenPages }); break;
            case "row-above": Session.ChangeRows(pos.Row, true); break;
            case "row-below": Session.ChangeRows(pos.Row + 1, true); break;
            case "row-delete": Session.ChangeRows(pos.Row, false); break;
            case "col-left": Session.ChangeColumns(pos.Cell, true); break;
            case "col-right": Session.ChangeColumns(pos.Cell + 1, true); break;
            case "col-delete": Session.ChangeColumns(pos.Cell, false); break;
            case "merge": Session.MergeCells(2); break;
            case "split": Session.SplitCell(); break;
            case "repeat-header":
                var table = (WriteTable)Session.Document.Sections[pos.Section].Blocks[pos.Block];
                Session.SetHeaderRepeat(!table.Rows[0].RepeatHeader);
                break;
            case "cell-format":
                var colors = Ask("Format cell", "Fill and border RGB (comma separated)", "FFFFFF,888888")?.Split(',');
                if (colors is { Length: 2 })
                    Session.FormatCell(Color(colors[0].Trim()), Color(colors[1].Trim()));
                break;
        }
    }

    private void Styles()
    {
        var stack = new StackPanel();
        foreach (var style in Session.Document.Styles.Values.OrderBy(style => style.Name, StringComparer.Ordinal))
        {
            var button = Material.Button(style.Name, "TextButton");
            button.Click += (_, _) => Session.ApplyStyle(style.Id, style.Kind);
            stack.Children.Add(button);
        }
        Material.Dialog(this, "Styles", new ScrollViewer { Content = stack, MaxHeight = 400 }, 300).ShowDialog();
    }

    private void ParagraphDialog()
    {
        if (Session.GetParagraph(Session.Selection.Caret) is not { } paragraph)
            return;
        var format = Session.Document.Resolve(paragraph).Paragraph;
        var stack = new StackPanel();
        var fields = Fields(stack, [("Left indent", format.LeftIndent ?? 0), ("Right indent", format.RightIndent ?? 0),
            ("First line indent", format.FirstLineIndent ?? 0), ("Space before", format.SpaceBefore ?? 0),
            ("Space after", format.SpaceAfter ?? 0), ("Line spacing", format.LineSpacing ?? 1)]);
        var keep = new CheckBox { Content = "Keep with next", IsChecked = format.KeepWithNext == true };
        var pageBreak = new CheckBox { Content = "Page break before", IsChecked = format.PageBreakBefore == true };
        stack.Children.Add(keep);
        stack.Children.Add(pageBreak);
        Confirm("Paragraph", stack, () =>
        {
            var numbers = fields.Select(field => Number(field.Text)).ToArray();
            if (numbers.Any(value => !value.HasValue) || numbers[5] <= 0)
                return false;
            return Session.FormatParagraphs(style => style with
            {
                LeftIndent = numbers[0], RightIndent = numbers[1], FirstLineIndent = numbers[2],
                SpaceBefore = numbers[3], SpaceAfter = numbers[4], LineSpacing = numbers[5],
                KeepWithNext = keep.IsChecked, PageBreakBefore = pageBreak.IsChecked
            }).Accepted;
        });
    }

    private void PageSetupDialog(WritePageSettings settings)
    {
        var stack = new StackPanel();
        var fields = Fields(stack, [("Width", settings.Width), ("Height", settings.Height), ("Left margin", settings.Left),
            ("Top margin", settings.Top), ("Right margin", settings.Right), ("Bottom margin", settings.Bottom)]);
        Confirm("Page setup", stack, () =>
        {
            var values = fields.Select(field => Number(field.Text) ?? -1).ToArray();
            return Session.SetPage(settings with
            {
                Width = values[0], Height = values[1], Left = values[2], Top = values[3], Right = values[4], Bottom = values[5]
            }).Accepted;
        });
    }

    private void ShowFind()
    {
        if (editor is null)
            return;
        if (query is not null)
        {
            query.Focus();
            return;
        }
        query = new TextBox { Width = 170 };
        var replace = new TextBox { Width = 170 };
        var next = Material.Button("Find next", "TextButton");
        var all = Material.Button("Replace all", "TextButton");
        var close = Material.IconButton("Close", "Close Find");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Height = 40 };
        Material.Paint(line, Panel.BackgroundProperty, "SurfaceContainerLow");
        line.Children.Add(query);
        line.Children.Add(next);
        findReplacement = replace;
        replaceAction = all;
        replace.Visibility = all.Visibility = Mode == EditorMode.Full ? Visibility.Visible : Visibility.Collapsed;
        line.Children.Add(replace);
        line.Children.Add(all);
        line.Children.Add(close);
        DockPanel.SetDock(line, Dock.Top);
        editor.Children.Insert(0, line);
        next.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(query?.Text))
                return;
            var matches = Session.Paragraphs().SelectMany(pos =>
            {
                string text = Session.GetParagraph(pos)!.Text;
                int match = text.IndexOf(query.Text, StringComparison.OrdinalIgnoreCase);
                return match < 0 ? Array.Empty<WritePosition>() : [pos with { Offset = match }];
            }).ToArray();
            if (matches.Length == 0)
                return;
            var start = matches.FirstOrDefault(pos => pos.CompareTo(Session.Selection.Start) > 0, matches[0]);
            var end = start with { Offset = start.Offset + query.Text.Length };
            if (Session.Select(new(start, end)))
                Navigate(pages.FirstOrDefault(page => page.FindCaret(start) is not null)?.Number ?? page);
        };
        all.Click += (_, _) =>
        {
            if (query is not null)
                Session.ReplaceAll(query.Text, replace.Text);
        };
        close.Click += (_, _) =>
        {
            editor.Children.Remove(line);
            query = null;
            findReplacement = replaceAction = null;
            surface?.Focus();
        };
        query.Focus();
    }

    private void InsertImage()
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 32 * 1024 * 1024)
                return;
            byte[] bytes = File.ReadAllBytes(dialog.FileName);
            using var stream = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            double scale = Math.Min(1, 360d / frame.PixelWidth);
            Session.InsertRun(new("", Image: new([.. bytes], Path.GetExtension(dialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase) ?
                "image/png" : "image/jpeg", frame.PixelWidth * scale, frame.PixelHeight * scale)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (status is not null) status.Text = error.Message;
        }
    }

    private void Save(bool export)
    {
        var dialog = new SaveFileDialog { Filter = export ? "Word document|*.docx|PDF document|*.pdf" : "Word document|*.docx" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        _ = WriteFile(dialog.FileName);
    }

    private async Task WriteFile(string path)
    {
        var document = Session.Document;
        try
        {
            await Task.Run(async () =>
            {
                using var stream = File.Create(path);
                if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    await WriteFiles.SavePdfAsync(document, stream, WpfFontProvider.Instance);
                else
                    WriteFiles.SaveDocx(document, stream);
            });
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
        {
            if (status is not null) status.Text = error.Message;
        }
    }

    private void Print()
    {
        var formats = new DocumentFormatRegistry();
        formats.Register(new PdfDocumentWriter());
        var snapshot = Session.Paragraphs().Select(position => (DocumentBlock)new DocumentParagraph(Session.GetParagraph(position)!.Text)).ToImmutableArray();
        var preview = new PrintPreviewWindow((setup, fit, _) => new Document("Document", [new(setup, snapshot)]) { FitToWidth = fit },
            formats, dark: GridTheme.GetMode(this) == GridThemeMode.Dark) { Owner = Window.GetWindow(this) };
        preview.Show();
    }

    private string? Ask(string title, string label, string value)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label });
        var input = new TextBox { Text = value, Margin = new Thickness(0, 6, 0, 8) };
        stack.Children.Add(input);
        return Confirm(title, stack, () => true) ? input.Text : null;
    }

    private bool Confirm(string title, StackPanel stack, Func<bool> apply)
    {
        var ok = Material.Button("OK", "TextButton");
        stack.Children.Add(ok);
        var window = Material.Dialog(this, title, stack, 340);
        ok.Click += (_, _) => { if (apply()) window.DialogResult = true; };
        return window.ShowDialog() == true;
    }

    private static TextBox[] Fields(StackPanel panel, (string Label, double Value)[] fields)
    {
        return fields.Select(item =>
        {
            panel.Children.Add(new TextBlock { Text = item.Label });
            var field = new TextBox { Text = item.Value.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(0, 4, 0, 8) };
            panel.Children.Add(field);
            return field;
        }).ToArray();
    }

    private static double? Number(string? value) => double.TryParse(value, CultureInfo.InvariantCulture, out double number) &&
        double.IsFinite(number) ? number : null;
    private static uint? Color(string? value) => uint.TryParse(value?.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint color) ? color : null;
}
