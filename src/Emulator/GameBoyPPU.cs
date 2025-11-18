using System.Reflection;

namespace EducationBoy.Emulator;

public class GameBoyPPU
{
    private const int ScreenWidth = 160;
    private const int ScreenHeight = 144;
    private const int BytesPerPixel = 4; // BGRA32 format

    private const uint Color1 = 0xFF000000; // Black
    private const uint Color2 = 0xFF555555; // Dark Gray
    private const uint Color3 = 0xFFAAAAAA; // Light Gray
    private const uint Color4 = 0xFFFFFFFF; // White

    private readonly GameBoyMemory _memory;
    private readonly EmulatorCore _emuCore;
    private readonly byte[] _framebuffer = new byte[ScreenWidth * ScreenHeight * BytesPerPixel];

    // PPU State
    private int _scanline; // LY Register (0-153)
    private int _modeClock; // Clock cycles in current mode
    private int _mode; // 0: HBlank, 1: VBlank, 2: OAM, 3: VRAM

    // LCDC/SCY/SCX/Palette registers
    private const ushort REG_LCDC = 0xFF40;
    private const ushort REG_SCY = 0xFF42;
    private const ushort REG_SCX = 0xFF43;
    private const ushort REG_LY = 0xFF44;
    private const ushort REG_BGP = 0xFF47;
    private const ushort REG_WY = 0xFF4A;
    private const ushort REG_WX = 0xFF4B;

    // Mode timing (cycles)
    private const int OAM_CYCLES = 80;
    private const int VRAM_CYCLES = 172;
    private const int HBLANK_CYCLES = 204;
    private const int SCANLINE_CYCLES = 456;

    public byte[] Framebuffer => _framebuffer;

    public GameBoyPPU(GameBoyMemory memory, EmulatorCore emuCore)
    {
        _memory = memory;
        _emuCore = emuCore;
    }

    public void Step(int cycles)
    {
        _modeClock += cycles;
        var cycleTotal = _modeClock;
        var cyclesUsed = 0;
        while (_modeClock >= 0)
        {
            switch (_mode)
            {
                case 2: // OAM Search
                    if (_modeClock >= OAM_CYCLES)
                    {
                        _modeClock -= OAM_CYCLES;
                        cyclesUsed += OAM_CYCLES;
                        _mode = 3;
                    }
                    else
                    {
                        _modeClock = -1; // break loop
                    }
                    break;
                case 3: // VRAM (drawing)
                    if (_modeClock >= VRAM_CYCLES)
                    {
                        _modeClock -= VRAM_CYCLES;
                        cyclesUsed += VRAM_CYCLES;
                        _mode = 0;
                        // Draw scanline
                        if (_scanline < ScreenHeight)
                        {
                            RenderScanline(_scanline);
                        }
                    }
                    else
                    {
                        _modeClock = -1;
                    }
                    break;
                case 0: // HBlank
                    if (_modeClock >= HBLANK_CYCLES)
                    {
                        _modeClock -= HBLANK_CYCLES;
                        cyclesUsed += HBLANK_CYCLES;
                        _scanline++;
                        if (_scanline == ScreenHeight)
                        {
                            _mode = 1; // Enter VBlank
                        }
                        else
                        {
                            _mode = 2; // Next scanline OAM
                        }
                        // Write LY
                        _memory.WriteByte(REG_LY, (byte)_scanline);
                    }
                    else
                    {
                        _modeClock = -1;
                    }
                    break;
                case 1: // VBlank
                    if (_modeClock >= SCANLINE_CYCLES)
                    {
                        if (_scanline == 144){
                            _emuCore.TriggerScreenRerender(Framebuffer);
                        }
                        _modeClock -= SCANLINE_CYCLES;
                        cyclesUsed += SCANLINE_CYCLES;
                        _scanline++;
                        if (_scanline > 153)
                        {
                            _scanline = 0;
                            _mode = 2;
                        }
                        // Write LY
                        _memory.WriteByte(REG_LY, (byte)_scanline);
                    }
                    else
                    {
                        _modeClock = -1;
                    }
                    break;
            }
        }
        var unusedCycles = cycleTotal - cyclesUsed;
        _modeClock = unusedCycles;
    }

    private void RenderScanline(int y)
    {
        // LCDC register
        byte lcdc = _memory.ReadByte(REG_LCDC);
        if ((lcdc & 0x80) == 0) // LCD off
        {
            // Fill with white
            for (int x = 0; x < ScreenWidth; x++)
            {
                int idx = (y * ScreenWidth + x) * BytesPerPixel;
                _framebuffer[idx + 0] = 0xFF;
                _framebuffer[idx + 1] = 0xFF;
                _framebuffer[idx + 2] = 0xFF;
                _framebuffer[idx + 3] = 0xFF;
            }
            return;
        }

        // Background rendering
        bool bgEnable = (lcdc & 0x01) != 0;
        ushort bgTileMapAddr = (lcdc & 0x08) != 0 ? (ushort)0x9C00 : (ushort)0x9800;
        ushort bgTileDataAddr = (lcdc & 0x10) != 0 ? (ushort)0x8000 : (ushort)0x8800;

        byte scy = _memory.ReadByte(REG_SCY);
        byte scx = _memory.ReadByte(REG_SCX);
        byte bgp = _memory.ReadByte(REG_BGP);

        for (int x = 0; x < ScreenWidth; x++)
        {
            byte colorIdx = 0;
            if (bgEnable)
            {
                int mapX = (x + scx) & 0xFF;
                int mapY = (y + scy) & 0xFF;
                int tileX = mapX / 8;
                int tileY = mapY / 8;
                int tileMapIndex = tileY * 32 + tileX;
                ushort tileMapAddr = (ushort)(bgTileMapAddr + tileMapIndex);
                byte tileNum = _memory.ReadByte(tileMapAddr);

                // Tile data addressing
                ushort tileAddr;
                if (bgTileDataAddr == 0x8000)
                {
                    tileAddr = (ushort)(0x8000 + tileNum * 16);
                }
                else
                {
                    // Signed index
                    sbyte snum = (sbyte)tileNum;
                    tileAddr = (ushort)(0x9000 + snum * 16);
                }

                int pixelY = mapY % 8;
                int pixelX = 7 - (mapX % 8); // Bit 7 is leftmost
                byte lo = _memory.ReadByte((ushort)(tileAddr + pixelY * 2));
                byte hi = _memory.ReadByte((ushort)(tileAddr + pixelY * 2 + 1));
                colorIdx = (byte)(((hi >> pixelX) & 1) << 1 | ((lo >> pixelX) & 1));
            }

            // Palette mapping (BGP)
            byte palette = (byte)((bgp >> (colorIdx * 2)) & 0x03);
            uint color = palette switch
            {
                0 => Color4,
                1 => Color3,
                2 => Color2,
                3 => Color1,
                _ => Color1
            };

            int idx = (y * ScreenWidth + x) * BytesPerPixel;
            _framebuffer[idx + 0] = (byte)((color >> 0) & 0xFF);   // B
            _framebuffer[idx + 1] = (byte)((color >> 8) & 0xFF);   // G
            _framebuffer[idx + 2] = (byte)((color >> 16) & 0xFF);  // R
            _framebuffer[idx + 3] = 0xFF;                         // A
        }
    }
}