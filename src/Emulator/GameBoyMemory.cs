using System;

namespace EducationBoy.Emulator;

[Flags]
public enum InterruptFlags : byte
{
    VBlank = 1 << 0,
    Stat = 1 << 1,
    Timer = 1 << 2,
    Serial = 1 << 3,
    Joypad = 1 << 4
}

public class GameBoyMemory
{
    private const ushort ADDR_DIV = 0xFF04;
    private const ushort ADDR_TIMA = 0xFF05;
    private const ushort ADDR_TMA = 0xFF06;
    private const ushort ADDR_TAC = 0xFF07;
    private const ushort ADDR_IF = 0xFF0F;
    private const ushort ADDR_LCDC = 0xFF40;
    private const ushort ADDR_STAT = 0xFF41;
    private const ushort ADDR_SCY = 0xFF42;
    private const ushort ADDR_SCX = 0xFF43;
    private const ushort ADDR_LY = 0xFF44;
    private const ushort ADDR_LYC = 0xFF45;
    private const ushort ADDR_DMA = 0xFF46;
    private const ushort ADDR_BGP = 0xFF47;
    private const ushort ADDR_OBP0 = 0xFF48;
    private const ushort ADDR_OBP1 = 0xFF49;
    private const ushort ADDR_WY = 0xFF4A;
    private const ushort ADDR_WX = 0xFF4B;
    private const ushort ADDR_IE = 0xFFFF;

    private readonly byte[] _romBank0 = new byte[0x4000];
    private readonly byte[] _romBankX = new byte[0x4000];
    private readonly byte[] _vram = new byte[0x2000];
    private readonly byte[] _eram = new byte[0x2000];
    private readonly byte[] _wram = new byte[0x2000];
    private readonly byte[] _oam = new byte[0xA0];
    private readonly byte[] _io = new byte[0x80];
    private readonly byte[] _hram = new byte[0x7F];

    private byte _interruptEnable;
    private byte _interruptFlags;

    private byte _stat;
    private byte _ly;
    private byte _lyc;

    private byte _div;
    private byte _tima;
    private byte _tma;
    private byte _tac;
    private int _divCounter;
    private int _timaCounter;

    public byte InterruptEnable => _interruptEnable;
    public byte InterruptFlag => _interruptFlags;

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
        _interruptFlags = 0;

        _stat = 0;
        _ly = 0;
        _lyc = 0;

        _div = 0;
        _tima = 0;
        _tma = 0;
        _tac = 0;
        _divCounter = 0;
        _timaCounter = 0;

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

    private void LoadPostBootDefaults()
    {
        WriteByte(ADDR_LCDC, 0x91);
        WriteByte(ADDR_STAT, 0x85);
        WriteByte(ADDR_SCY, 0x00);
        WriteByte(ADDR_SCX, 0x00);
        WriteLyInternal(0x00);
        WriteByte(ADDR_LYC, 0x00);
        WriteByte(ADDR_BGP, 0xFC);
        WriteByte(ADDR_OBP0, 0xFF);
        WriteByte(ADDR_OBP1, 0xFF);
        WriteByte(ADDR_WY, 0x00);
        WriteByte(ADDR_WX, 0x00);

        // Sound registers (silent defaults, audio not implemented but values expected by games)
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

        // Timers
        WriteByte(ADDR_DIV, 0x00);
        WriteByte(ADDR_TIMA, 0x00);
        WriteByte(ADDR_TMA, 0x00);
        WriteByte(ADDR_TAC, 0x00);
        WriteByte(ADDR_IF, 0xE1);
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
                return _vram[address - 0x8000];
            case <= 0xBFFF:
                return _eram[address - 0xA000];
            case <= 0xDFFF:
                return _wram[address - 0xC000];
            case <= 0xFDFF:
                return _wram[address - 0xE000]; // Echo RAM
            case <= 0xFE9F:
                return _oam[address - 0xFE00];
            case <= 0xFEFF:
                return 0xFF; // Unusable memory area
            case <= 0xFF7F:
                return ReadIo(address);
            case <= 0xFFFE:
                return _hram[address - 0xFF80];
            case ADDR_IE:
                return _interruptEnable;
        }
    }

    public void WriteByte(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x3FFF:
                break;
            case <= 0x7FFF:
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
                _wram[address - 0xE000] = value; // Echo RAM
                break;
            case <= 0xFE9F:
                _oam[address - 0xFE00] = value;
                break;
            case <= 0xFEFF:
                // Unusable memory area; ignore writes
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
                return (byte)(_tac | 0xF8);
            case ADDR_IF:
                return (byte)(_interruptFlags | 0xE0);
            case ADDR_STAT:
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
                _div = 0;
                _divCounter = 0;
                break;
            case ADDR_TIMA:
                _tima = value;
                break;
            case ADDR_TMA:
                _tma = value;
                break;
            case ADDR_TAC:
                _tac = (byte)(value & 0x07);
                break;
            case ADDR_IF:
                _interruptFlags = (byte)(value & 0x1F);
                break;
            case ADDR_LCDC:
                _io[address - 0xFF00] = value;
                break;
            case ADDR_STAT:
                // Bits 3-6 writable, bits 0-2 controlled by PPU
                _stat = (byte)((_stat & 0x07) | (value & 0x78));
                break;
            case ADDR_LY:
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
        ushort source = (ushort)(page << 8);
        for (int i = 0; i < 0xA0; i++)
        {
            _oam[i] = ReadByte((ushort)(source + i));
        }
    }

    public void Step(int cycles)
    {
        StepDivider(cycles);
        StepTimer(cycles);
    }

    private void StepDivider(int cycles)
    {
        _divCounter += cycles;
        while (_divCounter >= 256)
        {
            _divCounter -= 256;
            _div++;
        }
    }

    private void StepTimer(int cycles)
    {
        if ((_tac & 0x04) == 0)
        {
            _timaCounter = 0;
            return;
        }

        int period = (_tac & 0x03) switch
        {
            0 => 1024,   // 4096 Hz
            1 => 16,     // 262144 Hz
            2 => 64,     // 65536 Hz
            3 => 256,    // 16384 Hz
            _ => 1024
        };

        _timaCounter += cycles;
        while (_timaCounter >= period)
        {
            _timaCounter -= period;
            _tima++;
            if (_tima == 0)
            {
                _tima = _tma;
                RequestInterrupt(InterruptFlags.Timer);
            }
        }
    }

    public void RequestInterrupt(InterruptFlags flag)
    {
        _interruptFlags |= (byte)flag;
    }

    public void SetStatMode(byte mode)
    {
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
            _stat |= 1 << 2;
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

    public void FillTestScreen()
    {
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
                    lo |= (byte)(((color & 1) << (7 - col)));
                    hi |= (byte)((((color >> 1) & 1) << (7 - col)));
                }

                int addr = baseAddr + row * 2;
                _vram[addr] = lo;
                _vram[addr + 1] = hi;
            }
        }

        for (int i = 0; i < 1024; i++)
        {
            _vram[0x1800 + i] = (byte)(i % 256);
        }

        _io[0x47] = 0xE4;
    }
}
