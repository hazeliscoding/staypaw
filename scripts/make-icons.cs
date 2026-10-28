#:package Svg.Skia@5.2.3
#:property ManagePackageVersionsCentrally=false

// Renders docs/brand/app-icon.svg into the multi-size .ico used by the app and Setup, a grey one for the tray while
// Staypaw is paused, and a 256 px PNG for the README. Run from the repo root: dotnet run scripts/make-icons.cs
using SkiaSharp;
using Svg.Skia;

var brand = Path.Combine("docs", "brand");
using var svg = new SKSvg();
svg.Load(Path.Combine(brand, "app-icon.svg"));
var picture = svg.Picture ?? throw new InvalidOperationException("app-icon.svg did not render");

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var pngs = sizes.Select(size => Render(picture, size, null)).ToList();
File.WriteAllBytes(Path.Combine(brand, "app-icon-256.png"), pngs[^1]);
WriteIco(Path.Combine(brand, "staypaw.ico"), sizes, pngs);

// Greyscale at 55% opacity reads as switched off on light and dark taskbars alike. Only the tray shows it, and the
// tray never draws an icon above 64 px.
using var grey = SKColorFilter.CreateColorMatrix(
[
    0.2126f, 0.7152f, 0.0722f, 0, 0,
    0.2126f, 0.7152f, 0.0722f, 0, 0,
    0.2126f, 0.7152f, 0.0722f, 0, 0,
    0, 0, 0, 0.55f, 0,
]);
int[] traySizes = [.. sizes.Where(size => size <= 64)];
WriteIco(Path.Combine(brand, "staypaw-paused.ico"), traySizes, [.. traySizes.Select(size => Render(picture, size, grey))]);

Console.WriteLine($"Wrote staypaw.ico ({string.Join(", ", sizes)}), staypaw-paused.ico ({string.Join(", ", traySizes)}) and app-icon-256.png");

static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
{
    using var ico = File.Create(path);
    using var writer = new BinaryWriter(ico);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        // A 256 px entry is stored as 0 in the one-byte width and height fields.
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(pngs[i].Length);
        writer.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var png in pngs)
    {
        writer.Write(png);
    }
}

static byte[] Render(SKPicture picture, int size, SKColorFilter? filter)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    var scale = size / picture.CullRect.Width;
    canvas.Scale(scale);
    using var paint = new SKPaint { ColorFilter = filter };
    canvas.DrawPicture(picture, paint);
    canvas.Flush();
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}
