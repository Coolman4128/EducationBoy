using Avalonia.Data;
using EducationBoy.ViewModels;

namespace EducationBoy.Emulator;

public class EmulatorCore
{
    public GameBoyCPU CPU { get; private set;}
    public GameBoyMemory Memory { get; private set;}
    public GameBoyClock Clock { get; private set;}
    public GameBoyPPU PPU { get; private set;}
    public MainWindowViewModel ScreenRendererViewModel { get; private set;}
    

    public EmulatorCore(MainWindowViewModel screenRendererViewModel)
    {
        Memory = new GameBoyMemory();
        CPU = new GameBoyCPU(Memory);
        PPU = new GameBoyPPU(Memory, this);
        Clock = new GameBoyClock(CPU, PPU, Memory);
        ScreenRendererViewModel = screenRendererViewModel;
    }

    public void LoadRom(byte[] romImage)
    {
        Clock.Stop();
        Memory.Reset(romImage);
        CPU.Reset();
        PPU.Reset();
        Clock.Start();
    }

    public void TriggerScreenRerender(byte [] framebuffer)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ScreenRendererViewModel.UpdateFrame(framebuffer);
        });
    }
}
