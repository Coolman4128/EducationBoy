using System;
using System.IO;
using System.Text;

namespace EducationBoy.Emulator;

internal static class EmulatorLogger
{
    private const int InstructionCaptureLimit = 5_000_000;
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogFilePath = Path.Combine(LogDirectory, "emulator.log");

    private static bool _sessionActive;
    private static bool _instructionCaptureArmed;
    private static int _instructionsLogged;

    public static void StartNewSession(byte[]? romImage, string? romName)
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogDirectory);
            using var writer = new StreamWriter(LogFilePath, append: false, Encoding.UTF8);
            writer.WriteLine($"===== EducationBoy Session {DateTime.UtcNow:O} =====");
            writer.WriteLine($"ROM: {romName ?? "Unknown ROM"}");
            writer.WriteLine($"Size: {romImage?.Length ?? 0} bytes");
            writer.WriteLine();
            writer.WriteLine("----- ROM Dump (hex + ASCII) -----");
        }

        AppendRomDump(romImage);

        lock (SyncRoot)
        {
            _sessionActive = true;
            _instructionCaptureArmed = false;
            _instructionsLogged = 0;
            AppendLine(string.Empty);
        }
    }

    public static void BeginInstructionCapture()
    {
        lock (SyncRoot)
        {
            if (!_sessionActive)
            {
                Directory.CreateDirectory(LogDirectory);
            }

            _instructionsLogged = 0;
            _instructionCaptureArmed = true;
            AppendLine($"----- Instruction Trace (first {InstructionCaptureLimit} instructions) -----");
        }
    }

    public static void LogInstruction(
        ushort pcBefore,
        byte opcode,
        byte a,
        byte f,
        byte b,
        byte c,
        byte d,
        byte e,
        byte h,
        byte l,
        ushort sp,
        ushort pcAfter,
        bool ime,
        bool isHalted,
        bool isStopped,
        int cycles)
    {
        lock (SyncRoot)
        {
            if (!_instructionCaptureArmed || _instructionsLogged >= InstructionCaptureLimit)
            {
                return;
            }

            _instructionsLogged++;
            using var writer = new StreamWriter(LogFilePath, append: true, Encoding.UTF8);
            writer.WriteLine(
                $"{_instructionsLogged,4}: PC={pcBefore:X4} OP={opcode:X2} NEXT={pcAfter:X4} " +
                $"A={a:X2} F={f:X2} B={b:X2} C={c:X2} D={d:X2} E={e:X2} H={h:X2} L={l:X2} " +
                $"SP={sp:X4} IME={(ime ? 1 : 0)} HALT={(isHalted ? 1 : 0)} STOP={(isStopped ? 1 : 0)} CYC={cycles}");

            if (_instructionsLogged == InstructionCaptureLimit)
            {
                writer.WriteLine($"Instruction capture complete ({InstructionCaptureLimit} instructions).");
                _instructionCaptureArmed = false;
            }
        }
    }

    private static void AppendRomDump(byte[]? romImage)
    {
        lock (SyncRoot)
        {
            using var writer = new StreamWriter(LogFilePath, append: true, Encoding.UTF8);
            if (romImage is null || romImage.Length == 0)
            {
                writer.WriteLine("No ROM data supplied; skipping dump.");
                writer.WriteLine();
                return;
            }

            const int bytesPerLine = 16;
            var ascii = new char[bytesPerLine];

            for (int offset = 0; offset < romImage.Length; offset += bytesPerLine)
            {
                int count = Math.Min(bytesPerLine, romImage.Length - offset);
                writer.Write($"{offset:X6}: ");

                for (int i = 0; i < bytesPerLine; i++)
                {
                    if (i < count)
                    {
                        byte value = romImage[offset + i];
                        writer.Write($"{value:X2} ");
                        ascii[i] = value is >= 0x20 and <= 0x7E ? (char)value : '.';
                    }
                    else
                    {
                        writer.Write("   ");
                        ascii[i] = ' ';
                    }

                    if (i == 7)
                    {
                        writer.Write(' ');
                    }
                }

                writer.Write(" |");
                for (int i = 0; i < bytesPerLine; i++)
                {
                    writer.Write(ascii[i]);
                }
                writer.WriteLine("|");
            }

            writer.WriteLine();
        }
    }

    private static void AppendLine(string message)
    {
        lock (SyncRoot)
        {
            using var writer = new StreamWriter(LogFilePath, append: true, Encoding.UTF8);
            writer.WriteLine(message);
        }
    }
}
