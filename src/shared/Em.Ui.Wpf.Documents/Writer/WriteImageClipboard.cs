// Ported from WordCraft (doc/src/edit.rs @ fab8793ce791763761353b78caad6a63a46e5b01), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
// Upstream sync: WordCraft crates/doc/src/edit.rs @ fab8793ce791763761353b78caad6a63a46e5b01.
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Em.Write.Model;

namespace Em.Ui.Wpf.Documents.Writer;

internal static class WriteImageClipboard
{
    internal static void Copy(WriteImage image)
    {
        using var input = new MemoryStream(image.Bytes.ToArray());
        var bitmap = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var data = new DataObject();
        data.SetImage(bitmap);
        data.SetData("PNG", PngBytes(bitmap));
        Clipboard.SetDataObject(data, true);
    }

    internal static WriteImage? Read()
    {
        if (Clipboard.GetImage() is not { } bitmap || (long)bitmap.PixelWidth * bitmap.PixelHeight > 32_000_000)
            return null;
        byte[] bytes = PngBytes(bitmap);
        if (bytes.Length > 32 * 1024 * 1024)
            return null;
        double scale = Math.Min(1, 360d / bitmap.PixelWidth);
        return new([.. bytes], "image/png", bitmap.PixelWidth * scale, bitmap.PixelHeight * scale);
    }

    private static byte[] PngBytes(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
