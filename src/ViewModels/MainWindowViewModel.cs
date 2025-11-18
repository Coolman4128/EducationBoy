using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Data;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EducationBoy.Emulator;

namespace EducationBoy.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public WriteableBitmap FramebufferBitmap { get; }

    private const int Width = 160;
    private const int Height = 144;
    private const int BytesPerPixel = 4;
    private readonly byte[] _framebuffer = new byte[Width * Height * BytesPerPixel];

    public EmulatorCore Emulator { get; private set; }

    public MainWindowViewModel()
    {
        FramebufferBitmap = new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        Emulator = new EmulatorCore(this);
        Emulator.Clock.Start();

        //Functions used to test memory
        // Emulator.Memory.FillTestScreen();
        // Emulator.Memory.WriteByte(0xFF40, 0x91); // LCDC: LCD on, BG on, BG map 0, tile data 0x8000
        // Emulator.Memory.WriteByte(0xFF47, 0xE4); // BGP: 11 10 01 00

    }

    public void UpdateFrame(byte[] framebuffer)
    {
        using var fb = FramebufferBitmap.Lock();
        Marshal.Copy(framebuffer, 0, fb.Address, framebuffer.Length);
        // Force UI to refresh the binding
        OnPropertyChanged(nameof(FramebufferBitmap));
    }


    [RelayCommand]
    public void LoadRom()
    {
        
    }

   
   
}
