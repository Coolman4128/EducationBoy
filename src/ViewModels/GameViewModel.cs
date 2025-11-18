using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EducationBoy.ViewModels;

public class GameViewModel : ViewModelBase
{
    
    public WriteableBitmap FramebufferBitmap { get; }

    private const int Width = 160;
    private const int Height = 144;

    public GameViewModel()
    {
        FramebufferBitmap = new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),              // DPI, not super important here
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
    }

   

    /// <summary>
    /// Copy a 160x144 pixel buffer (BGRA32) into the WriteableBitmap.
    /// </summary>
    public unsafe void UpdateFrame(ReadOnlySpan<int> pixels)
    {
        if (pixels.Length != Width * Height)
            throw new ArgumentException("Pixel buffer must be 160x144.", nameof(pixels));

        using var fb = FramebufferBitmap.Lock();

        // fb.Address is a pointer to the pixel memory
        fixed (int* srcPtr = pixels)
        {
            Buffer.MemoryCopy(
                srcPtr,
                (void*)fb.Address,
                fb.RowBytes * fb.Size.Height,
                pixels.Length * sizeof(int));
        }

        // We don't need to raise PropertyChanged here because the object
        // reference (FramebufferBitmap) doesn't change, only its contents.
        // Avalonia will redraw on the next UI pass.
    }
}
