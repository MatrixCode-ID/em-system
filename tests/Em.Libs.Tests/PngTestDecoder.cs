using System.Buffers.Binary;
using System.IO.Compression;

namespace Em.Libs.Tests
{
   /// <summary>
   /// A small PNG reader for the QR tests, so the test project needs no imaging library: it handles the
   /// non-interlaced gray, RGB, palette and RGBA images at 1-8 bits that encoders write.
   /// </summary>
   internal sealed class PngTestDecoder
   {
      public int Width { get; private init; }
      public int Height { get; private init; }

      // RGB, three bytes per pixel, row by row.
      public byte[] Rgb { get; private init; } = [];

      public (byte R, byte G, byte B) Pixel(int x, int y) {
         var i = (y * Width + x) * 3;
         return (Rgb[i], Rgb[i + 1], Rgb[i + 2]);
      }

      // One luminance byte per pixel, the input ZXing reads.
      public byte[] Gray() {
         var gray = new byte[Width * Height];
         for (var i = 0; i < gray.Length; i++)
            gray[i] = (byte)((Rgb[i * 3] * 299 + Rgb[i * 3 + 1] * 587 + Rgb[i * 3 + 2] * 114) / 1000);
         return gray;
      }

      public static PngTestDecoder Decode(byte[] png) {
         byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
         Assert.True(png.AsSpan(0, 8).SequenceEqual(signature), "Not a PNG.");
         int width = 0, height = 0, depth = 0, type = 0;
         byte[] palette = [];
         using var idat = new MemoryStream();
         for (var pos = 8; pos < png.Length;) {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos, 4));
            var kind = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, length);
            switch (kind) {
               case "IHDR":
                  width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                  height = BinaryPrimitives.ReadInt32BigEndian(data[4..8]);
                  depth = data[8];
                  type = data[9];
                  Assert.Equal(0, data[12]);
                  break;
               case "PLTE": palette = data.ToArray(); break;
               case "IDAT": idat.Write(data); break;
            }
            pos += 12 + length;
         }

         var channels = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException() };
         var bitsPerPixel = channels * depth;
         var stride = (width * bitsPerPixel + 7) / 8;
         var bpp = Math.Max(1, bitsPerPixel / 8);
         idat.Position = 0;
         using var inflated = new MemoryStream();
         using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(inflated);
         var raw = inflated.ToArray();

         var rgb = new byte[width * height * 3];
         var previous = new byte[stride];
         var row = new byte[stride];
         for (var y = 0; y < height; y++) {
            var offset = y * (stride + 1);
            var filter = raw[offset];
            for (var i = 0; i < stride; i++) {
               int a = i >= bpp ? row[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
               var x = raw[offset + 1 + i];
               row[i] = (byte)(filter switch {
                  0 => x,
                  1 => x + a,
                  2 => x + b,
                  3 => x + (a + b) / 2,
                  4 => x + Paeth(a, b, c),
                  _ => throw new NotSupportedException(),
               });
            }
            for (var px = 0; px < width; px++) {
               var o = (y * width + px) * 3;
               if (depth < 8) {
                  var bit = px * depth;
                  var value = (row[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
                  if (type == 3) {
                     rgb[o] = palette[value * 3]; rgb[o + 1] = palette[value * 3 + 1]; rgb[o + 2] = palette[value * 3 + 2];
                  } else {
                     var g = (byte)(value * 255 / ((1 << depth) - 1));
                     rgb[o] = rgb[o + 1] = rgb[o + 2] = g;
                  }
               } else {
                  var s = px * channels;
                  if (type == 3) {
                     rgb[o] = palette[row[s] * 3]; rgb[o + 1] = palette[row[s] * 3 + 1]; rgb[o + 2] = palette[row[s] * 3 + 2];
                  } else if (channels >= 3) {
                     rgb[o] = row[s]; rgb[o + 1] = row[s + 1]; rgb[o + 2] = row[s + 2];
                  } else {
                     rgb[o] = rgb[o + 1] = rgb[o + 2] = row[s];
                  }
               }
            }
            (previous, row) = (row, previous);
         }
         return new PngTestDecoder { Width = width, Height = height, Rgb = rgb };
      }

      private static int Paeth(int a, int b, int c) {
         int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
         return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
   }
}
