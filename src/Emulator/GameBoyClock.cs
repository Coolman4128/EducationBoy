using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Data;

namespace EducationBoy.Emulator;

public class GameBoyClock
{
    public const double ClockSpeedHz = 4_194_304; // 4.194304 MHz
    public const double FrameRate = 59.73; // Approximate frame rate

    const int CyclesPerFrame = (int)(ClockSpeedHz / FrameRate); // ~70224 cycles per frame

    private Stopwatch _stopwatch;
    private volatile bool _running;
    private GameBoyCPU _cpu;
    private GameBoyPPU _ppu;
    private GameBoyMemory _memory;

    public GameBoyClock(GameBoyCPU cpu, GameBoyPPU ppu, GameBoyMemory memory)
    {
        _cpu = cpu;
        _ppu = ppu;
        _memory = memory;
        _stopwatch = new Stopwatch();
    }


    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _stopwatch.Restart();
        EmulatorLogger.BeginInstructionCapture();
        long lastTime = _stopwatch.ElapsedTicks;
        double cyclesRemainder = 0.0;

        Task.Run(() =>
        {
            while (_running)
            {
                long now = _stopwatch.ElapsedTicks;
                double elapsedSeconds = (now - lastTime) / (double)Stopwatch.Frequency;
                lastTime = now;
                double cyclesToRun = elapsedSeconds * ClockSpeedHz + cyclesRemainder;
                if (cyclesToRun < 0)
                {
                    cyclesRemainder = cyclesToRun;
                    continue;
                }
                int cyclesBudget = (int)cyclesToRun;
                cyclesRemainder = cyclesToRun - cyclesBudget;

                if (cyclesBudget == 0)
                {
                    continue;
                }


                int cyclesRan = 0;
                while (cyclesRan < cyclesBudget && _running)
                {
                    int stepCycles = _cpu.Step();
                    cyclesRan += stepCycles;
                    _memory.Step(stepCycles);
                    _ppu.Step(stepCycles);
                }
            }
        });
    }

    public void Stop()
    {
        _running = false;
    }
}
