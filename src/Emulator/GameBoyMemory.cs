    // Fills VRAM and tilemap with a test pattern for PPU testing
    
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
                if (address < _memory.Length) return _memory[address];
                break;
            case <= 0x7FFF:
                {
                    int idx = address - 0x4000;
                    if (idx >= 0 && idx < _bankedMemory.Length) return _bankedMemory[idx];
                }
                break;
            case <= 0x9FFF:
                {
                    int idx = address - 0x8000;
                    if (idx >= 0 && idx < _vram.Length) return _vram[idx];
                }
                break;
            case <= 0xBFFF:
                {
                    int idx = address - 0xA000;
                    if (idx >= 0 && idx < _eram.Length) return _eram[idx];
                }
                break;
            case <= 0xDFFF:
                {
                    int idx = address - 0xC000;
                    if (idx >= 0 && idx < _wram.Length) return _wram[idx];
                }
                break;
            case <= 0xFDFF:
                // Echo RAM (mirrors C000-DDFF)
                {
                    int idx = address - 0xE000;
                    if (idx >= 0 && idx < _wram.Length) return _wram[idx];
                }
                break;
            case <= 0xFE9F:
                {
                    int idx = address - 0xFE00;
                    if (idx >= 0 && idx < _oam.Length) return _oam[idx];
                }
                break;
            case <= 0xFF7F:
                {
                    int idx = address - 0xFF00;
                    if (idx >= 0 && idx < _io.Length) return _io[idx];
                }
                break;
            case <= 0xFFFE:
                {
                    int idx = address - 0xFF80;
                    if (idx >= 0 && idx < _hram.Length) return _hram[idx];
                }
                break;
            case 0xFFFF:
                return _interruptEnable;
        }
        return 0xFF; // Return open bus value for unmapped/out-of-bounds
    }

    public void WriteByte(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x3FFF:
                if (address < _memory.Length) _memory[address] = value;
                break;
            case <= 0x7FFF:
                {
                    int idx = address - 0x4000;
                    if (idx >= 0 && idx < _bankedMemory.Length) _bankedMemory[idx] = value;
                }
                break;
            case <= 0x9FFF:
                {
                    int idx = address - 0x8000;
                    if (idx >= 0 && idx < _vram.Length) _vram[idx] = value;
                }
                break;
            case <= 0xBFFF:
                {
                    int idx = address - 0xA000;
                    if (idx >= 0 && idx < _eram.Length) _eram[idx] = value;
                }
                break;
            case <= 0xDFFF:
                {
                    int idx = address - 0xC000;
                    if (idx >= 0 && idx < _wram.Length) _wram[idx] = value;
                }
                break;
            case <= 0xFDFF:
                // Echo RAM (mirrors C000-DDFF)
                {
                    int idx = address - 0xE000;
                    if (idx >= 0 && idx < _wram.Length) _wram[idx] = value;
                }
                break;
            case <= 0xFE9F:
                {
                    int idx = address - 0xFE00;
                    if (idx >= 0 && idx < _oam.Length) _oam[idx] = value;
                }
                break;
            case <= 0xFF7F:
                {
                    int idx = address - 0xFF00;
                    if (idx >= 0 && idx < _io.Length) _io[idx] = value;
                }
                break;
            case <= 0xFFFE:
                {
                    int idx = address - 0xFF80;
                    if (idx >= 0 && idx < _hram.Length) _hram[idx] = value;
                }
                break;
            case 0xFFFF:
                _interruptEnable = value;
                break;
        }
    }


    public void FillTestScreen()
    {
        // Fill tile data (0x8000-0x97FF) with a checkerboard pattern
        // Each tile is 16 bytes (8x8 pixels, 2bpp)
        for (int tile = 0; tile < 384; tile++) // 384 tiles
        {
            int baseAddr = tile * 16;
            for (int row = 0; row < 8; row++)
            {
                byte lo = 0, hi = 0;
                for (int col = 0; col < 8; col++)
                {
                    // Checkerboard: alternate color index 1 and 2
                    int color = ((row + col) % 2 == 0) ? 1 : 2;
                    lo |= (byte)(((color & 1) << (7 - col)));
                    hi |= (byte)((((color >> 1) & 1) << (7 - col)));
                }
                int addr = baseAddr + row * 2;
                if (addr + 1 < _vram.Length)
                {
                    _vram[addr] = lo;
                    _vram[addr + 1] = hi;
                }
            }
        }

        // Fill background tilemap (0x9800-0x9BFF, 32x32 = 1024 bytes)
        for (int i = 0; i < 1024; i++)
        {
            if (0x1800 + i < _vram.Length)
                _vram[0x1800 + i] = (byte)(i % 256); // Use tile numbers 0-255
        }
        // Optionally set palette to default (BGP register)
        if (_io.Length > 0x47)
            _io[0x47] = 0xE4; // 11 10 01 00: White, Light Gray, Dark Gray, Black
    }
}