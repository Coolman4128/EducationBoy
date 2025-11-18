using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace EducationBoy.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public WriteableBitmap FramebufferBitmap { get; }

    private const int Width = 160;
    private const int Height = 144;
    private const int BytesPerPixel = 4;
    private readonly byte[] _framebuffer = new byte[Width * Height * BytesPerPixel];
    private readonly DispatcherTimer _timer;
    private double _time; // seconds

    public MainWindowViewModel()
    {
        FramebufferBitmap = new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        // ~60 FPS animation on the UI thread
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) =>
        {
            _time += 0.016; // approximate frame delta
            RenderSpiralFrame(_framebuffer, _time);
            UpdateFrame(_framebuffer);
        };
        _timer.Start();
    }

    private void UpdateFrame(byte[] framebuffer)
    {
        using var fb = FramebufferBitmap.Lock();
        Marshal.Copy(framebuffer, 0, fb.Address, framebuffer.Length);
        // Force UI to refresh the binding
        OnPropertyChanged(nameof(FramebufferBitmap));
    }

    /// <summary>
    /// Renders a rotating spiral into the framebuffer.
    /// </summary>
    private void RenderSpiralFrame(byte[] fb, double t)
    {
        int cx = Width / 2;
        int cy = Height / 2;

        // Tweak these to change the effect
        double radialFreq = 0.30;   // spacing of rings
        double angleFreq  = 4.0;    // how many "arms" the spiral has
        double spinSpeed  = 2.0;    // rotation speed (radians/sec)

        for (int y = 0; y < Height; y++)
        {
            int dy = y - cy;

            for (int x = 0; x < Width; x++)
            {
                int dx = x - cx;

                double r = Math.Sqrt(dx * dx + dy * dy);     // radius
                double angle = Math.Atan2(dy, dx);           // -π..π

                // Make the spiral *rotate* by adding t to the angle term
                double v = Math.Sin(r * radialFreq + (angle + t * spinSpeed) * angleFreq);

                // Map [-1,1] -> [0,255]
                byte intensity = (byte)(127 + 128 * v);

                // Optional: add a subtle color shift over time so motion is super obvious
                byte rCol = intensity;
                byte gCol = (byte)(intensity * (0.5 + 0.5 * Math.Sin(t)));
                byte bCol = (byte)(intensity * (0.5 + 0.5 * Math.Cos(t)));

                int idx = (y * Width + x) * BytesPerPixel;

                fb[idx + 0] = bCol;      // B
                fb[idx + 1] = gCol;      // G
                fb[idx + 2] = rCol;      // R
                fb[idx + 3] = 255;       // A
            }
        }
    }
}
