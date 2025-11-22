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
    private GameBoyApu? _apu;

    // I/O register addresses
    private const ushort ADDR_JOYP = 0xFF00;
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
    private byte[] _rom = Array.Empty<byte>();
    private int _romBankCount;
    private int _currentRomBank;
    private int _currentRomBank0;

    private readonly byte[] _vram = new byte[0x2000]; // 8000-9FFF
    private byte[] _externalRam = Array.Empty<byte>(); // A000-BFFF (banked)
    private int _ramBankSize;
    private int _ramBankCount;
    private int _currentRamBank;
    private bool _ramEnabled;

    private enum MbcType { RomOnly, Mbc1, Mbc2, Mbc3, Mbc5 }
    private MbcType _mbcType;

    // MBC1 state
    private int _mbc1RomBankLow5;
    private int _mbc1RomBankHigh2;
    private bool _mbc1RamBanking;

    // MBC3 state
    private int _mbc3RomBank;
    private int _mbc3RamBankOrRtc;
    private bool _mbc3LatchState;
    private readonly byte[] _mbc3RtcRegisters = new byte[5];

    // MBC5 state
    private int _mbc5RomBank;

    private readonly byte[] _wram = new byte[0x2000]; // C000-DFFF (8KB)
    private readonly byte[] _oam  = new byte[0x00A0]; // FE00-FE9F
    private readonly byte[] _io   = new byte[0x0080]; // FF00-FF7F
    private readonly byte[] _hram = new byte[0x007F]; // FF80-FFFE
    // Joypad
    private byte _joypadSelect; // bits 4-5 selected by writes to JOYP
    private bool _btnRight;
    private bool _btnLeft;
    private bool _btnUp;
    private bool _btnDown;
    private bool _btnA;
    private bool _btnB;
    private bool _btnSelect;
    private bool _btnStart;

    private byte _interruptEnable; // IE
    private byte _interruptFlags;  // IF

    // PPU-related registers stored separately for proper behaviour
    private byte _stat; // internal STAT (bits 0-6)
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

    public void ConnectApu(GameBoyApu apu)
    {
        _apu = apu;
    }

    public void Reset(byte[] romImage)
    {
        romImage ??= Array.Empty<byte>();

        SetupCartridge(romImage);

        Array.Clear(_vram);
        Array.Clear(_wram);
        Array.Clear(_oam);
        Array.Clear(_io);
        Array.Clear(_hram);
        if (_externalRam.Length > 0)
        {
            Array.Clear(_externalRam);
        }

        _joypadSelect = 0x30;
        _btnRight = _btnLeft = _btnUp = _btnDown = false;
        _btnA = _btnB = _btnSelect = _btnStart = false;

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

    private void SetupCartridge(byte[] romImage)
    {
        _rom = romImage ?? Array.Empty<byte>();

        byte cartType   = ReadHeaderByte(0x0147);
        byte romSize    = ReadHeaderByte(0x0148);
        byte ramSize    = ReadHeaderByte(0x0149);

        _mbcType = cartType switch
        {
            0x00 => MbcType.RomOnly,
            0x01 or 0x02 or 0x03 => MbcType.Mbc1,
            0x05 or 0x06 => MbcType.Mbc2,
            0x0F or 0x10 or 0x11 or 0x12 or 0x13 => MbcType.Mbc3,
            0x19 or 0x1A or 0x1B or 0x1C or 0x1D or 0x1E => MbcType.Mbc5,
            _ => MbcType.RomOnly
        };

        _romBankCount = CalculateRomBankCount(romSize, _rom.Length);
        ConfigureRamBanks(ramSize);
        ResetMbcState();
    }

    private byte ReadHeaderByte(int offset)
    {
        if (_rom.Length > offset)
            return _rom[offset];
        return 0;
    }

    private int CalculateRomBankCount(byte romSizeCode, int romLength)
    {
        int banksFromHeader = romSizeCode switch
        {
            0x00 => 2,
            0x01 => 4,
            0x02 => 8,
            0x03 => 16,
            0x04 => 32,
            0x05 => 64,
            0x06 => 128,
            0x07 => 256,
            0x08 => 512,
            0x52 => 72,
            0x53 => 80,
            0x54 => 96,
            _ => 2
        };

        int banksFromLength = Math.Max(1, (romLength + 0x3FFF) / 0x4000);
        return Math.Max(banksFromHeader, banksFromLength);
    }

    private void ConfigureRamBanks(byte ramSizeCode)
    {
        (_ramBankCount, _ramBankSize) = ramSizeCode switch
        {
            0x00 => (0, 0x2000), // None
            0x01 => (1, 0x0800), // 2KB
            0x02 => (1, 0x2000), // 8KB
            0x03 => (4, 0x2000), // 32KB (4 x 8KB)
            0x04 => (16, 0x2000), // 128KB (16 x 8KB)
            0x05 => (8, 0x2000), // 64KB (8 x 8KB)
            _ => (0, 0x2000)
        };

        if (_mbcType == MbcType.Mbc2)
        {
            _ramBankCount = 1;
            _ramBankSize  = 0x200; // 512 bytes (4-bit values, stored as bytes)
        }

        int totalRamSize = _ramBankCount * _ramBankSize;
        _externalRam = totalRamSize > 0 ? new byte[totalRamSize] : Array.Empty<byte>();
    }

    private void ResetMbcState()
    {
        _ramEnabled = false;
        _currentRamBank = 0;
        _currentRomBank0 = 0;

        _mbc1RomBankLow5 = 1;
        _mbc1RomBankHigh2 = 0;
        _mbc1RamBanking = false;

        _mbc3RomBank = 1;
        _mbc3RamBankOrRtc = 0;
        _mbc3LatchState = false;
        Array.Clear(_mbc3RtcRegisters, 0, _mbc3RtcRegisters.Length);

        _mbc5RomBank = 1;

        _currentRomBank = _mbcType switch
        {
            MbcType.RomOnly => _romBankCount > 1 ? 1 : 0,
            MbcType.Mbc2 => 1,
            MbcType.Mbc3 => 1,
            MbcType.Mbc5 => 1,
            _ => 1
        };

        UpdateMbc1Banks();
    }

    private void UpdateMbc1Banks()
    {
        if (_mbcType != MbcType.Mbc1)
            return;

        int bank = (_mbc1RomBankLow5 & 0x1F) | ((_mbc1RomBankHigh2 & 0x03) << 5);
        if ((bank & 0x1F) == 0)
            bank |= 1;

        _currentRomBank = NormalizeRomBank(bank);
        _currentRomBank0 = _mbc1RamBanking ? NormalizeRomBank(_mbc1RomBankHigh2 << 5) : 0;

        if (_mbc1RamBanking && _ramBankCount > 0)
            _currentRamBank = Math.Min(_mbc1RomBankHigh2, _ramBankCount - 1);
        else
            _currentRamBank = 0;
    }

    private int NormalizeRomBank(int bank)
    {
        if (_romBankCount <= 0)
            return 0;

        bank %= _romBankCount;
        if (bank < 0)
            bank += _romBankCount;
        return bank;
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
        WriteByte(ADDR_JOYP, 0xCF);
    }

    public byte ReadByte(ushort address)
    {
        switch (address)
        {
            case <= 0x3FFF:
                return ReadRomBank0(address);

            case <= 0x7FFF:
                return ReadSwitchableRom((ushort)(address - 0x4000));

            case <= 0x9FFF:
                return _vram[address - 0x8000];

            case <= 0xBFFF:
                return ReadExternalRam(address);

            case <= 0xDFFF:
                return _wram[address - 0xC000];

            case <= 0xFDFF:
            {
                ushort mirror = (ushort)(address - 0x2000); // Echo of C000-DDFF
                return _wram[mirror - 0xC000];
            }

            case <= 0xFE9F:
                if (_dmaActive)
                    return 0xFF;
                return _oam[address - 0xFE00];

            case <= 0xFEFF:
                return 0xFF;

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
            case <= 0x7FFF:
                HandleMbcWrite(address, value);
                return;

            case <= 0x9FFF:
                _vram[address - 0x8000] = value;
                return;

            case <= 0xBFFF:
                WriteExternalRam(address, value);
                return;

            case <= 0xDFFF:
                _wram[address - 0xC000] = value;
                return;

            case <= 0xFDFF:
            {
                ushort mirror = (ushort)(address - 0x2000);
                _wram[mirror - 0xC000] = value;
                return;
            }

            case <= 0xFE9F:
                if (_dmaActive)
                    return;
                _oam[address - 0xFE00] = value;
                return;

            case <= 0xFEFF:
                return;

            case <= 0xFF7F:
                WriteIo(address, value);
                return;

            case <= 0xFFFE:
                _hram[address - 0xFF80] = value;
                return;

            case ADDR_IE:
                _interruptEnable = value;
                return;
        }
    }

    private byte ReadRomBank0(int offset)
    {
        int bank = (_mbcType == MbcType.Mbc1 && _mbc1RamBanking) ? _currentRomBank0 : 0;
        return ReadRomByte(bank, offset);
    }

    private byte ReadSwitchableRom(ushort offset)
    {
        int bank = _mbcType switch
        {
            MbcType.Mbc1 => _currentRomBank,
            MbcType.Mbc2 => _currentRomBank == 0 ? 1 : _currentRomBank,
            MbcType.Mbc3 => _mbc3RomBank == 0 ? 1 : _mbc3RomBank,
            MbcType.Mbc5 => _currentRomBank,
            _ => _romBankCount > 1 ? 1 : 0
        };

        return ReadRomByte(bank, offset);
    }

    private byte ReadRomByte(int bank, int offset)
    {
        if (_rom.Length == 0)
            return 0xFF;

        bank = NormalizeRomBank(bank);
        int index = bank * 0x4000 + offset;
        if ((uint)index < _rom.Length)
            return _rom[index];
        return 0xFF;
    }

    private byte ReadExternalRam(ushort address)
    {
        if (_ramBankCount == 0 || !_ramEnabled)
            return 0xFF;

        if (_mbcType == MbcType.Mbc2)
        {
            int idx = (address - 0xA000) & 0x01FF;
            if ((uint)idx < _externalRam.Length)
                return (byte)(_externalRam[idx] | 0xF0);
            return 0xFF;
        }

        if (_mbcType == MbcType.Mbc3 && _mbc3RamBankOrRtc is >= 0x08 and <= 0x0C)
        {
            int rtcIndex = _mbc3RamBankOrRtc - 0x08;
            return _mbc3RtcRegisters[rtcIndex];
        }

        int bank = _mbcType switch
        {
            MbcType.Mbc1 => _currentRamBank,
            MbcType.Mbc3 => _mbc3RamBankOrRtc,
            MbcType.Mbc5 => _currentRamBank,
            _ => _currentRamBank
        };

        int offset = (address - 0xA000) + bank * _ramBankSize;
        if ((uint)offset < _externalRam.Length)
            return _externalRam[offset];

        return 0xFF;
    }

    private void WriteExternalRam(ushort address, byte value)
    {
        if (_ramBankCount == 0 || !_ramEnabled)
            return;

        if (_mbcType == MbcType.Mbc2)
        {
            int idx = (address - 0xA000) & 0x01FF;
            if ((uint)idx < _externalRam.Length)
                _externalRam[idx] = (byte)(value & 0x0F);
            return;
        }

        if (_mbcType == MbcType.Mbc3 && _mbc3RamBankOrRtc is >= 0x08 and <= 0x0C)
        {
            int rtcIndex = _mbc3RamBankOrRtc - 0x08;
            _mbc3RtcRegisters[rtcIndex] = value;
            return;
        }

        int bank = _mbcType switch
        {
            MbcType.Mbc1 => _currentRamBank,
            MbcType.Mbc3 => _mbc3RamBankOrRtc,
            MbcType.Mbc5 => _currentRamBank,
            _ => _currentRamBank
        };

        int offset = (address - 0xA000) + bank * _ramBankSize;
        if ((uint)offset < _externalRam.Length)
            _externalRam[offset] = value;
    }

    private void HandleMbcWrite(ushort address, byte value)
    {
        switch (_mbcType)
        {
            case MbcType.Mbc1:
                HandleMbc1Write(address, value);
                break;

            case MbcType.Mbc2:
                HandleMbc2Write(address, value);
                break;

            case MbcType.Mbc3:
                HandleMbc3Write(address, value);
                break;

            case MbcType.Mbc5:
                HandleMbc5Write(address, value);
                break;

            default:
                break;
        }
    }

    private void HandleMbc1Write(ushort address, byte value)
    {
        if (address <= 0x1FFF)
        {
            _ramEnabled = (value & 0x0F) == 0x0A;
            return;
        }

        if (address <= 0x3FFF)
        {
            _mbc1RomBankLow5 = value & 0x1F;
            if (_mbc1RomBankLow5 == 0)
                _mbc1RomBankLow5 = 1;
            UpdateMbc1Banks();
            return;
        }

        if (address <= 0x5FFF)
        {
            _mbc1RomBankHigh2 = value & 0x03;
            UpdateMbc1Banks();
            return;
        }

        if (address <= 0x7FFF)
        {
            _mbc1RamBanking = (value & 0x01) != 0;
            UpdateMbc1Banks();
        }
    }

    private void HandleMbc2Write(ushort address, byte value)
    {
        if (address <= 0x3FFF)
        {
            if ((address & 0x0100) == 0)
            {
                _ramEnabled = (value & 0x0F) == 0x0A;
            }
            else
            {
                int bank = value & 0x0F;
                if (bank == 0)
                    bank = 1;
                _currentRomBank = NormalizeRomBank(bank);
            }
        }
    }

    private void HandleMbc3Write(ushort address, byte value)
    {
        if (address <= 0x1FFF)
        {
            _ramEnabled = (value & 0x0F) == 0x0A;
            return;
        }

        if (address <= 0x3FFF)
        {
            int bank = value & 0x7F;
            if (bank == 0)
                bank = 1;
            _mbc3RomBank = NormalizeRomBank(bank);
            return;
        }

        if (address <= 0x5FFF)
        {
            if (value <= 0x03)
            {
                _mbc3RamBankOrRtc = value;
                _currentRamBank = value;
            }
            else if (value >= 0x08 && value <= 0x0C)
            {
                _mbc3RamBankOrRtc = value;
            }
            return;
        }

        if (address <= 0x7FFF)
        {
            bool latch = (value & 0x01) != 0;
            if (!_mbc3LatchState && latch)
            {
                // RTC latch edge detected (timer not implemented, but keep registers stable)
            }
            _mbc3LatchState = latch;
        }
    }

    private void HandleMbc5Write(ushort address, byte value)
    {
        if (address <= 0x1FFF)
        {
            _ramEnabled = (value & 0x0F) == 0x0A;
            return;
        }

        if (address <= 0x2FFF)
        {
            _mbc5RomBank = (_mbc5RomBank & 0x100) | value;
            _currentRomBank = NormalizeRomBank(_mbc5RomBank);
            return;
        }

        if (address <= 0x3FFF)
        {
            _mbc5RomBank = (_mbc5RomBank & 0x0FF) | ((value & 0x01) << 8);
            _currentRomBank = NormalizeRomBank(_mbc5RomBank);
            return;
        }

        if (address <= 0x5FFF)
        {
            _currentRamBank = _ramBankCount > 0 ? Math.Min(value & 0x0F, _ramBankCount - 1) : 0;
        }
    }

    private byte ReadIo(ushort address)
    {
        switch (address)
        {
            case >= 0xFF10 and <= 0xFF3F:
                return _apu?.ReadRegister(address) ?? _io[address - 0xFF00];

            case ADDR_JOYP:
                return ComposeJoypadState();

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
            case >= 0xFF10 and <= 0xFF3F:
                _io[address - 0xFF00] = value;
                _apu?.WriteRegister(address, value);
                break;

            case ADDR_JOYP:
                _joypadSelect = (byte)(value & 0x30);
                UpdateJoypadRegister();
                break;

            case ADDR_DIV:
            {
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
                break;

            case ADDR_TMA:
                _tma = value;
                break;

            case ADDR_TAC:
            {
                byte newTac = (byte)(value & 0x07);

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
        ushort oldDiv = _divInternal;
        _divInternal++;
        _div = (byte)(_divInternal >> 8);

        bool oldInput = GetTimerInput(_tac, oldDiv);
        bool newInput = GetTimerInput(_tac, _divInternal);
        if (oldInput && !newInput)
        {
            IncrementTima();
        }

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

    private bool GetTimerInput(byte tac, ushort divInternal)
    {
        if ((tac & 0x04) == 0)
            return false;

        int bit = (tac & 0x03) switch
        {
            0 => 9,  // 4096 Hz
            1 => 3,  // 262144 Hz
            2 => 5,  // 65536 Hz
            3 => 7,  // 16384 Hz
            _ => 9
        };

        return ((divInternal >> bit) & 1) != 0;
    }

    private void IncrementTima()
    {
        if (_tima == 0xFF)
        {
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

    public void SetButtonState(GameBoyButton button, bool pressed)
    {
        bool wasPressed = button switch
        {
            GameBoyButton.Right  => _btnRight,
            GameBoyButton.Left   => _btnLeft,
            GameBoyButton.Up     => _btnUp,
            GameBoyButton.Down   => _btnDown,
            GameBoyButton.A      => _btnA,
            GameBoyButton.B      => _btnB,
            GameBoyButton.Select => _btnSelect,
            GameBoyButton.Start  => _btnStart,
            _ => false
        };

        switch (button)
        {
            case GameBoyButton.Right:  _btnRight  = pressed; break;
            case GameBoyButton.Left:   _btnLeft   = pressed; break;
            case GameBoyButton.Up:     _btnUp     = pressed; break;
            case GameBoyButton.Down:   _btnDown   = pressed; break;
            case GameBoyButton.A:      _btnA      = pressed; break;
            case GameBoyButton.B:      _btnB      = pressed; break;
            case GameBoyButton.Select: _btnSelect = pressed; break;
            case GameBoyButton.Start:  _btnStart  = pressed; break;
        }

        if (pressed && !wasPressed)
        {
            RequestInterrupt(InterruptFlags.Joypad);
        }

        UpdateJoypadRegister();
    }

    private void UpdateJoypadRegister()
    {
        _io[ADDR_JOYP - 0xFF00] = ComposeJoypadState();
    }

    private byte ComposeJoypadState()
    {
        byte value = 0x0F;
        bool selectDirections = (_joypadSelect & 0x10) == 0;
        bool selectButtons    = (_joypadSelect & 0x20) == 0;

        if (selectDirections)
        {
            if (_btnRight) value &= 0x0E;
            if (_btnLeft)  value &= 0x0D;
            if (_btnUp)    value &= 0x0B;
            if (_btnDown)  value &= 0x07;
        }

        if (selectButtons)
        {
            if (_btnA)      value &= 0x0E;
            if (_btnB)      value &= 0x0D;
            if (_btnSelect) value &= 0x0B;
            if (_btnStart)  value &= 0x07;
        }

        return (byte)(0xC0 | _joypadSelect | value);
    }

    /// <summary>
    /// Fill VRAM with a simple test pattern and tilemap.
    /// </summary>
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
                    lo |= (byte)((color & 1) << (7 - col));
                    hi |= (byte)(((color >> 1) & 1) << (7 - col));
                }

                int addr = baseAddr + row * 2;
                _vram[addr + 0] = lo;
                _vram[addr + 1] = hi;
            }
        }

        for (int i = 0; i < 1024; i++)
        {
            _vram[0x1800 + i] = (byte)(i % 256);
        }

        WriteByte(ADDR_BGP, 0xE4);
    }
}
