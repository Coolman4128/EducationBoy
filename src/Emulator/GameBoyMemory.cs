namespace EducationBoy.Emulator;

public class GameBoyMemory
{
    // Memory Arrays
    private byte[] _memory = new byte[0x3FFF + 1]; // Memory Bank 0 (0x0000 - 0x3FFF)
    private byte[] _bankedMemory = new byte[0x3FFF + 1]; // Switchable Memory Bank (0x4000 - 0x7FFF)
    private byte[] _vram = new byte[0x1FFF + 1];   // Video RAM (0x8000 - 0x9FFF)
    private byte[] _eram = new byte[0x1FFF + 1];   // External RAM (0xA000 - 0xBFFF)
    private byte[] _wram = new byte[0x1FFF + 1];   // Work RAM (0xC000 - 0xDFFF)
    private byte[] _oam = new byte[0x009F + 1];    // Object Attribute Memory (0xFE00 - 0xFE9F)
    private byte[] _io = new byte[0x007F + 1];      // I/O Registers (0xFF00 - 0xFF7F)
    private byte[] _hram = new byte[0x007F + 1];    // High RAM (0xFF80 - 0xFFFE)
    private byte _interruptEnable;               // Interrupt Enable Register (0xFFFF)


    // Functions
    public byte ReadByte(ushort address)
    {
        switch (address)
        {
            case <= 0x3FFF:
                return _memory[address];
            case <= 0x7FFF:
                // No Bank Switching Logic Implemented Yet
                return _bankedMemory[address - 0x4000];
            case <= 0x9FFF:
                return _vram[address - 0x8000];
            case <= 0xBFFF:
                return _eram[address - 0xA000];
            case <= 0xDFFF:
                return _wram[address - 0xC000];
            case <= 0xFDFF:
                // Echo RAM (mirrors C000-DDFF)
                return _wram[address - 0xE000];
            case <= 0xFE9F:
                return _oam[address - 0xFE00];
            case <= 0xFF7F:
                return _io[address - 0xFF00];
            case <= 0xFFFE:
                return _hram[address - 0xFF80];
            case 0xFFFF:
                return _interruptEnable;
        }
    }

    public void WriteByte(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x3FFF:
                _memory[address] = value;
                break;
            case <= 0x7FFF:
                // No Bank Switching Logic Implemented Yet
                _bankedMemory[address - 0x4000] = value;
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
                // Echo RAM (mirrors C000-DDFF)
                _wram[address - 0xE000] = value;
                break;
            case <= 0xFE9F:
                _oam[address - 0xFE00] = value;
                break;
            case <= 0xFF7F:
                _io[address - 0xFF00] = value;
                break;
            case <= 0xFFFE:
                _hram[address - 0xFF80] = value;
                break;
            case 0xFFFF:
                _interruptEnable = value;
                break;
        }
    }
}