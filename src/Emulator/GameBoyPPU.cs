using System;
using System.Collections.Generic;

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
    private int _windowLine;
    private int _modeClock; // Clock cycles in current mode
    private int _mode; // 0: HBlank, 1: VBlank, 2: OAM, 3: VRAM

    // LCDC/SCY/SCX/Palette registers
    private const ushort REG_LCDC = 0xFF40;
    private const ushort REG_SCY = 0xFF42;
    private const ushort REG_SCX = 0xFF43;
    private const ushort REG_LY = 0xFF44;
    private const ushort REG_LYC = 0xFF45;
    private const ushort REG_BGP = 0xFF47;
    private const ushort REG_OBP0 = 0xFF48;
    private const ushort REG_OBP1 = 0xFF49;
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
        Reset();
    }

    public void Reset()
    {
        _scanline = 0;
        _windowLine = 0;
        _modeClock = 0;
        _mode = 2;
        _memory.WriteLyInternal(0);
        _memory.SetStatMode(2);
    }

    public void Step(int cycles)
    {
        byte lcdc = _memory.ReadByte(REG_LCDC);
        if ((lcdc & 0x80) == 0)
        {
            _mode = 0;
            _modeClock = 0;
            _scanline = 0;
            _windowLine = 0;
            _memory.SetStatMode(0);
            _memory.WriteLyInternal(0);
            return;
        }

        _modeClock += cycles;
        bool advanced = true;
        while (advanced)
        {
            advanced = false;
            switch (_mode)
            {
                case 2: // OAM Search
                    if (_modeClock >= OAM_CYCLES)
                    {
                        _modeClock -= OAM_CYCLES;
                        _mode = 3;
                        _memory.SetStatMode(3);
                        advanced = true;
                    }
                    break;
                case 3: // VRAM (drawing)
                    if (_modeClock >= VRAM_CYCLES)
                    {
                        _modeClock -= VRAM_CYCLES;
                        if (_scanline < ScreenHeight)
                        {
                            RenderScanline(_scanline);
                        }
                        _mode = 0;
                        _memory.SetStatMode(0);
                        advanced = true;
                    }
                    break;
                case 0: // HBlank
                    if (_modeClock >= HBLANK_CYCLES)
                    {
                        _modeClock -= HBLANK_CYCLES;
                        _scanline++;
                        if (_scanline == ScreenHeight)
                        {
                            EnterVBlank();
                        }
                        else
                        {
                            _mode = 2; // Next scanline OAM
                            _memory.WriteLyInternal((byte)_scanline);
                            _memory.SetStatMode(2);
                        }
                        advanced = true;
                    }
                    break;
                case 1: // VBlank
                    if (_modeClock >= SCANLINE_CYCLES)
                    {
                        _modeClock -= SCANLINE_CYCLES;
                        _scanline++;
                        if (_scanline > 153)
                        {
                            _scanline = 0;
                            _windowLine = 0;
                            _mode = 2;
                            _memory.WriteLyInternal(0);
                            _memory.SetStatMode(2);
                        }
                        else
                        {
                            _memory.WriteLyInternal((byte)_scanline);
                        }
                        advanced = true;
                    }
                    break;
            }
        }
    }

    private void EnterVBlank()
    {
        _mode = 1;
        _memory.WriteLyInternal((byte)_scanline);
        _memory.SetStatMode(1);
        _memory.RequestInterrupt(InterruptFlags.VBlank);
        _emuCore.TriggerScreenRerender(Framebuffer);
    }

    private void RenderScanline(int y)
    {
        byte lcdc = _memory.ReadByte(REG_LCDC);
        bool bgEnable = (lcdc & 0x01) != 0;
        bool spriteEnable = (lcdc & 0x02) != 0;
        bool tallSprites = (lcdc & 0x04) != 0;
        ushort bgTileMapAddr = (lcdc & 0x08) != 0 ? (ushort)0x9C00 : (ushort)0x9800;
        ushort tileDataAddr = (lcdc & 0x10) != 0 ? (ushort)0x8000 : (ushort)0x8800;
        bool windowEnable = (lcdc & 0x20) != 0;
        ushort windowTileMapAddr = (lcdc & 0x40) != 0 ? (ushort)0x9C00 : (ushort)0x9800;

        byte scy = _memory.ReadByte(REG_SCY);
        byte scx = _memory.ReadByte(REG_SCX);
        byte wy = _memory.ReadByte(REG_WY);
        byte wx = _memory.ReadByte(REG_WX);
        byte bgp = _memory.ReadByte(REG_BGP);
        byte obp0 = _memory.ReadByte(REG_OBP0);
        byte obp1 = _memory.ReadByte(REG_OBP1);

        bool windowUsedThisLine = false;
        Span<byte> bgLine = stackalloc byte[ScreenWidth];

        for (int x = 0; x < ScreenWidth; x++)
        {
            bool windowXVisible = windowEnable && y >= wy && wx <= 166 && x >= Math.Max(0, wx - 7);
            byte colorIdx = 0;
            if (bgEnable)
            {
                ushort tileMap = bgTileMapAddr;
                int mapX;
                int mapY;
                int sourceY;

                if (windowXVisible)
                {
                    windowUsedThisLine = true;
                    tileMap = windowTileMapAddr;
                    mapX = x - Math.Max(0, wx - 7);
                    mapY = _windowLine;
                    sourceY = _windowLine;
                }
                else
                {
                    mapX = (x + scx) & 0xFF;
                    mapY = (y + scy) & 0xFF;
                    sourceY = mapY;
                }

                int tileX = mapX / 8;
                int tileY = mapY / 8;
                ushort tileMapAddr = (ushort)(tileMap + tileY * 32 + tileX);
                byte tileNum = _memory.ReadByte(tileMapAddr);
                ushort tileAddr = tileDataAddr == 0x8000
                    ? (ushort)(0x8000 + tileNum * 16)
                    : (ushort)(0x9000 + (sbyte)tileNum * 16);

                int pixelY = sourceY % 8;
                int pixelX = 7 - (mapX % 8);
                byte lo = _memory.ReadByte((ushort)(tileAddr + pixelY * 2));
                byte hi = _memory.ReadByte((ushort)(tileAddr + pixelY * 2 + 1));
                colorIdx = (byte)((((hi >> pixelX) & 1) << 1) | ((lo >> pixelX) & 1));
            }

            bgLine[x] = colorIdx;
            uint color = MapColor(bgp, colorIdx);
            int idx = (y * ScreenWidth + x) * BytesPerPixel;
            _framebuffer[idx + 0] = (byte)((color >> 0) & 0xFF);
            _framebuffer[idx + 1] = (byte)((color >> 8) & 0xFF);
            _framebuffer[idx + 2] = (byte)((color >> 16) & 0xFF);
            _framebuffer[idx + 3] = 0xFF;
        }

        if (windowUsedThisLine)
        {
            _windowLine++;
        }

        if (!spriteEnable)
            return;

        var sprites = GetVisibleSprites(y, tallSprites);
        foreach (var sprite in sprites)
        {
            int spriteHeight = tallSprites ? 16 : 8;
            int tileLine = y - sprite.Y;
            if ((sprite.Attributes & 0x40) != 0) // Y flip
            {
                tileLine = spriteHeight - 1 - tileLine;
            }

            int tileNumber = sprite.TileIndex;
            if (tallSprites)
            {
                tileNumber &= 0xFE;
                if (tileLine >= 8)
                {
                    tileNumber |= 0x01;
                    tileLine -= 8;
                }
            }

            ushort tileAddr = (ushort)(0x8000 + tileNumber * 16);
            byte lo = _memory.ReadByte((ushort)(tileAddr + tileLine * 2));
            byte hi = _memory.ReadByte((ushort)(tileAddr + tileLine * 2 + 1));

            for (int px = 0; px < 8; px++)
            {
                int screenX = sprite.X + px;
                if (screenX < 0 || screenX >= ScreenWidth)
                    continue;

                int bitX = (sprite.Attributes & 0x20) != 0 ? px : 7 - px;
                byte paletteColor = (byte)((((hi >> bitX) & 1) << 1) | ((lo >> bitX) & 1));
                if (paletteColor == 0)
                    continue; // Transparent

                bool bgOpaque = bgLine[screenX] != 0;
                bool behindBg = (sprite.Attributes & 0x80) != 0 && bgOpaque;
                if (behindBg)
                    continue;

                byte palette = (sprite.Attributes & 0x10) != 0 ? obp1 : obp0;
                uint color = MapColor(palette, paletteColor);
                int idx = (y * ScreenWidth + screenX) * BytesPerPixel;
                _framebuffer[idx + 0] = (byte)((color >> 0) & 0xFF);
                _framebuffer[idx + 1] = (byte)((color >> 8) & 0xFF);
                _framebuffer[idx + 2] = (byte)((color >> 16) & 0xFF);
            }
        }
    }

    private IReadOnlyList<Sprite> GetVisibleSprites(int y, bool tallSprites)
    {
        Span<Sprite> buffer = stackalloc Sprite[10];
        int count = 0;
        int spriteHeight = tallSprites ? 16 : 8;
        for (int i = 0; i < 40 && count < 10; i++)
        {
            ushort oamAddr = (ushort)(0xFE00 + i * 4);
            int spriteY = _memory.ReadByte(oamAddr) - 16;
            int spriteX = _memory.ReadByte((ushort)(oamAddr + 1)) - 8;
            byte tile = _memory.ReadByte((ushort)(oamAddr + 2));
            byte attrs = _memory.ReadByte((ushort)(oamAddr + 3));

            if (spriteY <= y && y < spriteY + spriteHeight && spriteX > -8 && spriteX < ScreenWidth)
            {
                buffer[count++] = new Sprite(spriteX, spriteY, tile, attrs, i);
            }
        }

        var result = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = buffer[i];
        }
        return result;
    }

    private static uint MapColor(byte palette, byte colorIdx)
    {
        byte entry = (byte)((palette >> (colorIdx * 2)) & 0x03);
        return entry switch
        {
            0 => Color4,
            1 => Color3,
            2 => Color2,
            3 => Color1,
            _ => Color1
        };
    }

    private readonly record struct Sprite(int X, int Y, int TileIndex, byte Attributes, int OamIndex);
}
