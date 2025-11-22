using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
    private GameBoyApu _apu;

    public GameBoyClock(GameBoyCPU cpu, GameBoyPPU ppu, GameBoyMemory memory, GameBoyApu apu)
    {
        _cpu = cpu;
        _ppu = ppu;
        _memory = memory;
        _apu = apu;
        _stopwatch = new Stopwatch();
    }


    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _stopwatch.Restart();
        EmulatorLogger.BeginInstructionCapture();
        long ticksPerFrame = (long)(Stopwatch.Frequency / FrameRate);
        long nextFrameTick = _stopwatch.ElapsedTicks + ticksPerFrame;

        Task.Run(() =>
        {
            int cyclesThisFrame = 0;

            while (_running)
            {
                while (_running && cyclesThisFrame < CyclesPerFrame)
                {
                    int stepCycles = _cpu.Step();
                    cyclesThisFrame += stepCycles;
                    _memory.Step(stepCycles);
                    _ppu.Step(stepCycles);
                    _apu.Step(stepCycles);
                }

                if (!_running)
                    break;

                long now = _stopwatch.ElapsedTicks;
                long sleepTicks = nextFrameTick - now;
                if (sleepTicks > 0)
                {
                    int sleepMs = (int)(sleepTicks * 1000 / Stopwatch.Frequency);
                    if (sleepMs > 0)
                    {
                        Thread.Sleep(sleepMs);
                    }

                    // Short spin to finish the frame without oversleeping
                    while (_stopwatch.ElapsedTicks < nextFrameTick)
                    {
                        Thread.SpinWait(50);
                    }
                }

                nextFrameTick += ticksPerFrame;

                // If we fell more than a frame behind, drop the backlog instead of trying to catch up
                if (_stopwatch.ElapsedTicks > nextFrameTick + ticksPerFrame)
                {
                    nextFrameTick = _stopwatch.ElapsedTicks + ticksPerFrame;
                }

                cyclesThisFrame = 0;
            }
        });
    }

    public void Stop()
    {
        _running = false;
    }
}
