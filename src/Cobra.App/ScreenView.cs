using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cobra.Video;

namespace Cobra.App;

/// <summary>The machine's screen: the latest <see cref="VideoFrame"/> through the palette, at an integer
/// zoom, nearest-neighbour. The control owns its bitmap; frames are copied in on the UI thread.</summary>
public sealed class ScreenView : Image
{
    private WriteableBitmap? _bitmap;
    private uint[] _argb = [];

    public ScreenView()
    {
        Stretch = Stretch.Fill;
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        SetZoom(2);
    }

    public int Zoom { get; private set; }

    public long? PresentedFrame { get; private set; }

    public void SetZoom(int zoom)
    {
        if (zoom is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(zoom));
        Zoom = zoom;
        Width = CobraVideo.RasterWidth * zoom;
        Height = CobraVideo.RasterHeight * zoom;
    }

    public void Present(VideoFrame frame)
    {
        if (frame.Pixels.Length != frame.Width * frame.Height) throw new ArgumentException("Frame size mismatch.", nameof(frame));
        if (_bitmap is null || _bitmap.PixelSize != new PixelSize(frame.Width, frame.Height))
        {
            Source = null;
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _argb = new uint[frame.Width * frame.Height];
            Source = _bitmap;
        }

        var palette = Palette.Argb;
        for (var i = 0; i < _argb.Length; i++) _argb[i] = palette[frame.Pixels[i] & 15];
        using (var target = _bitmap.Lock())
        {
            var bytes = MemoryMarshal.AsBytes(_argb.AsSpan());
            for (var y = 0; y < frame.Height; y++)
                Marshal.Copy(bytes.Slice(y * frame.Width * 4, frame.Width * 4).ToArray(), 0, target.Address + y * target.RowBytes, frame.Width * 4);
        }

        PresentedFrame = frame.FrameNumber;
        InvalidateVisual();
    }
}
