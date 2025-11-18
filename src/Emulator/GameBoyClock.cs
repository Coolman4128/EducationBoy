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

    public GameBoyClock(GameBoyCPU cpu, GameBoyPPU ppu)
    {
        _cpu = cpu;
        _ppu = ppu;
        _stopwatch = new Stopwatch();
    }


    public void Start()
    {
        _running = true;
        _stopwatch.Restart();
        long lastTime = _stopwatch.ElapsedTicks;
        double cyclesRemainder = 0.0;
        int cyclesThisFrame = 0;

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
                int wholeCycles = (int)cyclesToRun;
                cyclesRemainder = cyclesToRun - wholeCycles;

                if (wholeCycles == 0)
                {
                    continue;
                }
                

                int cyclesRan = 0;
                while (cyclesRan < wholeCycles)
                {
                    int stepCycles = _cpu.Step();
                    cyclesRan += stepCycles;
                }
                cyclesThisFrame += cyclesRan;
                int overshoot = cyclesRan - wholeCycles;
                cyclesRemainder -= overshoot; // Compensate for next tick

                _ppu.Step(cyclesRan);

            }
        });
    }

    public void Stop()
    {
        _running = false;
    }
}