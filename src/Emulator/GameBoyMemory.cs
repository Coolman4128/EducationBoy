using System;

namespace EducationBoy.Emulator;

[Flags]
public enum InterruptFlags : byte
{
    VBlank = 1 << 0,
    Stat   = 1 << 1,
    Timer  = 1 << 2,
    Serial = 1 << 3,
    Joypad = 1 << 4
}

public class GameBoyMemory
{
    private GameBoyCPU? _cpu;

    // I/O register addresses
    private const ushort ADDR_DIV  = 0xFF04;
    private const ushort ADDR_TIMA = 0xFF05;
    private const ushort ADDR_TMA  = 0xFF06;
    private const ushort ADDR_TAC  = 0xFF07;
    private const ushort ADDR_IF   = 0xFF0F;
    private const ushort ADDR_LCDC = 0xFF40;
    private const ushort ADDR_STAT = 0xFF41;
    private const ushort ADDR_SCY  = 0xFF42;
    private const ushort ADDR_SCX  = 0xFF43;
    private const ushort ADDR_LY   = 0xFF44;
    private const ushort ADDR_LYC  = 0xFF45;
    private const ushort ADDR_DMA  = 0xFF46;
    private const ushort ADDR_BGP  = 0xFF47;
    private const ushort ADDR_OBP0 = 0xFF48;
    private const ushort ADDR_OBP1 = 0xFF49;
    private const ushort ADDR_WY   = 0xFF4A;
    private const ushort ADDR_WX   = 0xFF4B;
    private const ushort ADDR_IE   = 0xFFFF;

    // Backing memory regions
    private readonly byte[] _romBank0 = new byte[0x4000]; // 0000–3FFF
    private readonly byte[] _romBankX = new byte[0x4000]; // 4000–7FFF
    private readonly byte[] _vram     = new byte[0x2000]; // 8000–9FFF
    private readonly byte[] _eram     = new byte[0x2000]; // A000–BFFF
    private readonly byte[] _wram     = new byte[0x2000]; // C000–DFFF (8KB)
    private readonly byte[] _oam      = new byte[0x00A0]; // FE00–FE9F
    private readonly byte[] _io       = new byte[0x0080]; // FF00–FF7F
    private readonly byte[] _hram     = new byte[0x007F]; // FF80–FFFE

    private byte _interruptEnable; // IE
    private byte _interruptFlags;  // IF

    // PPU-related registers stored separately for proper behaviour
    private byte _stat; // internal STAT (bits 0–6)
    private byte _ly;   // current scanline
    private byte _lyc;  // LY compare

    // Timers (edge-based)
    private ushort _divInternal;   // 16-bit internal divider
    private byte   _div;           // public DIV register (high byte of _divInternal)
    private byte   _tima;
    private byte   _tma;
    private byte   _tac;

    // TIMA overflow behaviour
    private bool _timaOverflow;
    private int  _timaOverflowCycles;

    // DMA (OAM transfer)
    private bool   _dmaActive;
    private ushort _dmaSource;
    private int    _dmaIndex;
    private int    _dmaCycleCounter;

    public byte InterruptEnable => _interruptEnable;
    public byte InterruptFlag   => _interruptFlags;

    public void ConnectCpu(GameBoyCPU cpu)
    {
        _cpu = cpu;
    }

    public void Reset(byte[] romImage)
    {
        romImage ??= Array.Empty<byte>();

        Array.Clear(_romBank0);
        Array.Clear(_romBankX);
        Array.Clear(_vram);
        Array.Clear(_eram);
        Array.Clear(_wram);
        Array.Clear(_oam);
        Array.Clear(_io);
        Array.Clear(_hram);

        LoadRomBanks(romImage);

        _interruptEnable = 0;
        _interruptFlags  = 0;

        _stat = 0;
        _ly   = 0;
        _lyc  = 0;

        // Timers
        _divInternal        = 0;
        _div                = 0;
        _tima               = 0;
        _tma                = 0;
        _tac                = 0;
        _timaOverflow       = false;
        _timaOverflowCycles = 0;

        // DMA
        _dmaActive       = false;
        _dmaSource       = 0;
        _dmaIndex        = 0;
        _dmaCycleCounter = 0;

        LoadPostBootDefaults();
    }

    private void LoadRomBanks(byte[]? romImage)
    {
        if (romImage is null || romImage.Length == 0)
            return;

        int bank0Length = Math.Min(_romBank0.Length, romImage.Length);
        Array.Copy(romImage, 0, _romBank0, 0, bank0Length);

        if (romImage.Length > 0x4000)
        {
            int bankXLength = Math.Min(_romBankX.Length, romImage.Length - 0x4000);
            Array.Copy(romImage, 0x4000, _romBankX, 0, bankXLength);
        }
    }

    /// <summary>
    /// Simulates post-boot state (i.e. as if Nintendo boot ROM has run).
    /// </summary>
    private void LoadPostBootDefaults()
    {
        // LCD / PPU
        WriteByte(ADDR_LCDC, 0x91); // LCDC
        WriteByte(ADDR_STAT, 0x85); // STAT
        WriteByte(ADDR_SCY,  0x00);
        WriteByte(ADDR_SCX,  0x00);
        WriteLyInternal(0x00);
        WriteByte(ADDR_LYC,  0x00);
        WriteByte(ADDR_BGP,  0xFC); // BG palette
        WriteByte(ADDR_OBP0, 0xFF);
        WriteByte(ADDR_OBP1, 0xFF);
        WriteByte(ADDR_WY,   0x00);
        WriteByte(ADDR_WX,   0x00);

        // Sound registers (values typical after boot ROM; audio not implemented)
        WriteByte(0xFF10, 0x80);
        WriteByte(0xFF11, 0xBF);
        WriteByte(0xFF12, 0xF3);
        WriteByte(0xFF14, 0xBF);
        WriteByte(0xFF16, 0x3F);
        WriteByte(0xFF17, 0x00);
        WriteByte(0xFF19, 0xBF);
        WriteByte(0xFF1A, 0x7F);
        WriteByte(0xFF1B, 0xFF);
        WriteByte(0xFF1C, 0x9F);
        WriteByte(0xFF1E, 0xBF);
        WriteByte(0xFF20, 0xFF);
        WriteByte(0xFF21, 0x00);
        WriteByte(0xFF22, 0x00);
        WriteByte(0xFF23, 0xBF);
        WriteByte(0xFF24, 0x77);
        WriteByte(0xFF25, 0xF3);
        WriteByte(0xFF26, 0xF1);

        // Timers / interrupts
        WriteByte(ADDR_DIV,  0x00);
        WriteByte(ADDR_TIMA, 0x00);
        WriteByte(ADDR_TMA,  0x00);
        WriteByte(ADDR_TAC,  0x00);
        WriteByte(ADDR_IF,   0xE1);
    }

    public byte ReadByte(ushort address)
    {
        switch (address)
        {
            case <= 0x3FFF:
                return _romBank0[address];

            case <= 0x7FFF:
                return _romBankX[address - 0x4000];

            case <= 0x9FFF:
                // VRAM
                return _vram[address - 0x8000];

            case <= 0xBFFF:
                // External RAM
                return _eram[address - 0xA000];

            case <= 0xDFFF:
                // Work RAM
                return _wram[address - 0xC000];

            case <= 0xFDFF:
            {
                // Echo of C000–DDFF (WRAM)
                ushort mirror = (ushort)(address - 0x2000); // E000 → C000, FDFF → DDFF
                return _wram[mirror - 0xC000];
            }

            case <= 0xFE9F:
                // OAM: during DMA, bus reads see 0xFF
                if (_dmaActive)
                    return 0xFF;
                return _oam[address - 0xFE00];

            case <= 0xFEFF:
                // Unusable memory area
                return 0xFF;

            case <= 0xFF7F:
                return ReadIo(address);

            case <= 0xFFFE:
                return _hram[address - 0xFF80];

            case ADDR_IE:
                return _interruptEnable;

            default:
                return 0xFF;
        }
    }

    public void WriteByte(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x3FFF:
                // No MBC yet (fixed ROM)
                break;

            case <= 0x7FFF:
                // Would be MBC control; ignored for simple MBC0
                break;

            case <= 0x9FFF:
                _vram[address - 0x8000] = value;
                break;

            case <= 0xBFFF:
                _eram[address - 0xA000] = value;
                break;

            case <= 0xDFFF:
                _wram[address - 0xC000] = value;
                break;

            case <= 0xFDFF:
            {
                // Echo RAM mirrors C000–DDFF
                ushort mirror = (ushort)(address - 0x2000);
                _wram[mirror - 0xC000] = value;
                break;
            }

            case <= 0xFE9F:
                // OAM: ignore CPU writes during DMA
                if (_dmaActive)
                    break;
                _oam[address - 0xFE00] = value;
                break;

            case <= 0xFEFF:
                // Unusable memory; ignore writes
                break;

            case <= 0xFF7F:
                WriteIo(address, value);
                break;

            case <= 0xFFFE:
                _hram[address - 0xFF80] = value;
                break;

            case ADDR_IE:
                _interruptEnable = value;
                break;
        }
    }

    private byte ReadIo(ushort address)
    {
        switch (address)
        {
            case ADDR_DIV:
                return _div;

            case ADDR_TIMA:
                return _tima;

            case ADDR_TMA:
                return _tma;

            case ADDR_TAC:
                // Upper bits read as 1 on DMG
                return (byte)(_tac | 0xF8);

            case ADDR_IF:
                // Upper 3 bits are always set when read
                return (byte)(_interruptFlags | 0xE0);

            case ADDR_STAT:
                // Bit 7 always 1 when read
                return (byte)(_stat | 0x80);

            case ADDR_LY:
                return _ly;

            case ADDR_LYC:
                return _lyc;

            default:
                return _io[address - 0xFF00];
        }
    }

    private void WriteIo(ushort address, byte value)
    {
        switch (address)
        {
            case ADDR_DIV:
            {
                // Writing to DIV resets it, but may cause a falling edge
                bool oldInput = GetTimerInput(_tac, _divInternal);

                _divInternal = 0;
                _div         = 0;

                bool newInput = GetTimerInput(_tac, _divInternal);
                if (oldInput && !newInput)
                    IncrementTima();

                break;
            }

            case ADDR_TIMA:
                _tima = value;
                // On real hardware, writes during overflow have special rules.
                // For now we keep it simple; most tests will still pass.
                break;

            case ADDR_TMA:
                _tma = value;
                break;

            case ADDR_TAC:
            {
                byte newTac = (byte)(value & 0x07);

                // Changing TAC can also create a falling edge on the timer input.
                bool oldInput = GetTimerInput(_tac, _divInternal);
                bool newInput = GetTimerInput(newTac, _divInternal);

                _tac = newTac;

                if (oldInput && !newInput)
                    IncrementTima();

                break;
            }

            case ADDR_IF:
                _interruptFlags = (byte)(value & 0x1F);
                break;

            case ADDR_LCDC:
                _io[address - 0xFF00] = value;
                break;

            case ADDR_STAT:
                // Bits 3–6 writable, bits 0–2 controlled by PPU
                _stat = (byte)((_stat & 0x07) | (value & 0x78));
                break;

            case ADDR_SCY:
            case ADDR_SCX:
            case ADDR_BGP:
            case ADDR_OBP0:
            case ADDR_OBP1:
            case ADDR_WY:
            case ADDR_WX:
                _io[address - 0xFF00] = value;
                break;

            case ADDR_LY:
                // Writing to LY resets it to 0
                WriteLyInternal(0);
                break;

            case ADDR_LYC:
                _lyc = value;
                UpdateCoincidence();
                break;

            case ADDR_DMA:
                DoDmaTransfer(value);
                break;

            default:
                _io[address - 0xFF00] = value;
                break;
        }
    }

    private void DoDmaTransfer(byte page)
    {
        // Start OAM DMA: source = page << 8, length 160 bytes, 4 cycles per byte
        _dmaSource       = (ushort)(page << 8);
        _dmaIndex        = 0;
        _dmaCycleCounter = 0;
        _dmaActive       = true;
    }

    public void Step(int cycles)
    {
        for (int i = 0; i < cycles; i++)
        {
            StepOneCycle();
        }
    }

    private void StepOneCycle()
    {
        // Divider increments every CPU cycle
        ushort oldDiv = _divInternal;
        _divInternal++;
        _div = (byte)(_divInternal >> 8);

        // TIMA: increment on falling edge of selected DIV bit when timer enabled
        bool oldInput = GetTimerInput(_tac, oldDiv);
        bool newInput = GetTimerInput(_tac, _divInternal);
        if (oldInput && !newInput)
        {
            IncrementTima();
        }

        // Handle delayed TIMA reload / interrupt after overflow
        if (_timaOverflow)
        {
            _timaOverflowCycles++;

            if (_timaOverflowCycles == 4)
            {
                _timaOverflow = false;
                _tima = _tma;
                RequestInterrupt(InterruptFlags.Timer);
            }
        }

        StepDmaCycle();
    }

    // Compute the current "timer input" from DIV and TAC (before edge detection)
    private bool GetTimerInput(byte tac, ushort divInternal)
    {
        if ((tac & 0x04) == 0) // timer disabled
            return false;

        int bit = (tac & 0x03) switch
        {
            0 => 9, // 4096 Hz
            1 => 3, // 262144 Hz
            2 => 5, // 65536 Hz
            3 => 7, // 16384 Hz
            _ => 9
        };

        return ((divInternal >> bit) & 1) != 0;
    }

    private void IncrementTima()
    {
        if (_tima == 0xFF)
        {
            // Start overflow sequence: TIMA becomes 0, reload is delayed
            _tima = 0x00;
            _timaOverflow       = true;
            _timaOverflowCycles = 0;
        }
        else
        {
            _tima++;
        }
    }

    private void StepDmaCycle()
    {
        if (!_dmaActive)
            return;

        _dmaCycleCounter++;
        if (_dmaCycleCounter >= 4)
        {
            _dmaCycleCounter = 0;

            ushort src = (ushort)(_dmaSource + _dmaIndex);
            _oam[_dmaIndex] = ReadByte(src);
            _dmaIndex++;

            if (_dmaIndex >= 0xA0)
            {
                _dmaActive = false;
            }
        }
    }

    public void RequestInterrupt(InterruptFlags flag)
    {
        _interruptFlags |= (byte)flag;
    }

    public void SetStatMode(byte mode)
    {
        // Set STAT mode bits (0–1), preserve coincidence (bit 2) and interrupt enables (3–6)
        _stat = (byte)((_stat & 0xFC) | (mode & 0x03));

        bool trigger = mode switch
        {
            0 => (_stat & (1 << 3)) != 0, // HBlank
            1 => (_stat & (1 << 4)) != 0, // VBlank
            2 => (_stat & (1 << 5)) != 0, // OAM
            _ => false
        };

        if (trigger)
            RequestInterrupt(InterruptFlags.Stat);
    }

    public void WriteLyInternal(byte value)
    {
        _ly = value;
        UpdateCoincidence();
    }

    private void UpdateCoincidence()
    {
        bool match = _ly == _lyc;
        if (match)
        {
            _stat |= (1 << 2);
            if ((_stat & (1 << 6)) != 0)
            {
                RequestInterrupt(InterruptFlags.Stat);
            }
        }
        else
        {
            _stat &= unchecked((byte)~(1 << 2));
        }
    }

    /// <summary>
    /// Fill VRAM with a simple test pattern and tilemap.
    /// </summary>
    public void FillTestScreen()
    {
        // Tile data: 384 tiles (0–383), each 16 bytes
        for (int tile = 0; tile < 384; tile++)
        {
            int baseAddr = tile * 16;
            for (int row = 0; row < 8; row++)
            {
                byte lo = 0;
                byte hi = 0;
                for (int col = 0; col < 8; col++)
                {
                    int color = ((row + col) % 2 == 0) ? 1 : 2;
                    lo |= (byte)((color & 1) << (7 - col));
                    hi |= (byte)(((color >> 1) & 1) << (7 - col));
                }

                int addr = baseAddr + row * 2;
                _vram[addr + 0] = lo;
                _vram[addr + 1] = hi;
            }
        }

        // BG tilemap at 0x9800 (offset 0x1800 in VRAM array)
        for (int i = 0; i < 1024; i++)
        {
            _vram[0x1800 + i] = (byte)(i % 256);
        }

        // Set a nice visible palette for the BG (write through IO)
        WriteByte(ADDR_BGP, 0xE4);
    }
}
