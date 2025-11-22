using Avalonia.Data;
using EducationBoy.ViewModels;

namespace EducationBoy.Emulator;

public class EmulatorCore
{
    public GameBoyCPU CPU { get; private set;}
    public GameBoyMemory Memory { get; private set;}
    public GameBoyClock Clock { get; private set;}
    public GameBoyPPU PPU { get; private set;}
    public GameBoyApu Apu { get; private set; }
    public MainWindowViewModel ScreenRendererViewModel { get; private set;}
    

    public EmulatorCore(MainWindowViewModel screenRendererViewModel)
    {
        Memory = new GameBoyMemory();
        Apu = new GameBoyApu();
        CPU = new GameBoyCPU(Memory);
        Memory.ConnectCpu(CPU);
        Memory.ConnectApu(Apu);
        PPU = new GameBoyPPU(Memory, this);
        Clock = new GameBoyClock(CPU, PPU, Memory, Apu);
        ScreenRendererViewModel = screenRendererViewModel;
    }

    public void LoadRom(byte[] romImage, string? romName = null)
    {
        Clock.Stop();
        EmulatorLogger.StartNewSession(romImage, romName);
        Apu.Reset();
        Memory.Reset(romImage);
        Memory.FillTestScreen();
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

    public void SetButtonState(GameBoyButton button, bool pressed)
    {
        Memory.SetButtonState(button, pressed);
    }

    public void SetVolume(float volume)
    {
        Apu.SetMasterVolume(volume);
    }
}
