using System;

namespace EducationBoy.Emulator
{
    public class GameBoyCPU
    {
        // Registers
        public byte A; // Accumulator
        public byte F; // Flags (Z N H C in upper nibble)
        public byte B;
        public byte C;
        public byte D;
        public byte E;
        public byte H;
        public byte L;
        public ushort SP; // Stack Pointer
        public ushort PC; // Program Counter

        // Register Helper Properties
        ushort AF { get => (ushort)((A << 8) | F); set { A = (byte)(value >> 8); F = (byte)(value & 0xF0); } }
        ushort BC { get => (ushort)((B << 8) | C); set { B = (byte)(value >> 8); C = (byte)value; } }
        ushort DE { get => (ushort)((D << 8) | E); set { D = (byte)(value >> 8); E = (byte)value; } }
        ushort HL { get => (ushort)((H << 8) | L); set { H = (byte)(value >> 8); L = (byte)value; } }

        // Flags
        bool FlagZ { get => (F & 0x80) != 0; set => F = value ? (byte)(F | 0x80) : (byte)(F & ~0x80); }
        bool FlagN { get => (F & 0x40) != 0; set => F = value ? (byte)(F | 0x40) : (byte)(F & ~0x40); }
        bool FlagH { get => (F & 0x20) != 0; set => F = value ? (byte)(F | 0x20) : (byte)(F & ~0x20); }
        bool FlagC { get => (F & 0x10) != 0; set => F = value ? (byte)(F | 0x10) : (byte)(F & ~0x10); }

        // Interrupt Helpers
        public bool IME; // Interrupt Master Enable
        private bool _enableIMENextCycle;

        // Hardware Components
        private readonly GameBoyMemory _memory;
        public int Cycles;
        public bool IsHalted;
        public bool IsStopped;

        // Constructor
        public GameBoyCPU(GameBoyMemory memory)
        {
            _memory = memory;
            Reset();
        }

        // Functions
        public void Reset()
        {
            A = B = C = D = E = H = L = 0;
            F = 0;
            SP = 0xFFFE;
            PC = 0x0100;

            IME = false;
            _enableIMENextCycle = false;
            IsHalted = false;
            IsStopped = false;
            Cycles = 0;
        }

        public int Step()
        {
            int cycles = 0;

            // 1. Handle enabling IME if requested
            if (_enableIMENextCycle)
            {
                IME = true;
                _enableIMENextCycle = false;
            }

            // 2. Handle Interrupts
            if (HandleInterrupts(ref cycles))
            {
                Cycles += cycles;
                return cycles;
            }

            // 3. Check if CPU is halted
            if (IsHalted)
            {
                cycles = 4;
                Cycles += cycles;
                return cycles;
            }

            // 4. Fetch, Decode, Execute
            byte opcode = _memory.ReadByte(PC++);
            cycles = ExecuteInstruction(opcode);
            Cycles += cycles;
            return cycles;
        }

        // =========================
        // Core opcode dispatcher
        // =========================
        private int ExecuteInstruction(byte opcode)
        {
            if (opcode == 0xCB)
            {
                byte cb = ReadIMM8();
                return ExecuteCB(cb);
            }

            switch (opcode)
            {
                // 0x0x
                case 0x00: // NOP
                    return 4;

                case 0x01: // LD BC, n16
                    BC = ReadIMM16();
                    return 12;

                case 0x02: // LD [BC], A
                    _memory.WriteByte(BC, A);
                    return 8;

                case 0x03: // INC BC
                    BC++;
                    return 8;

                case 0x04: // INC B
                    B = Inc8(B);
                    return 4;

                case 0x05: // DEC B
                    B = Dec8(B);
                    return 4;

                case 0x06: // LD B, n8
                    B = ReadIMM8();
                    return 8;

                case 0x07: // RLCA
                    Rlca();
                    return 4;

                case 0x08: // LD [a16], SP
                    {
                        ushort addr = ReadIMM16();
                        _memory.WriteByte(addr, (byte)(SP & 0xFF));
                        _memory.WriteByte((ushort)(addr + 1), (byte)(SP >> 8));
                        return 20;
                    }

                case 0x09: // ADD HL, BC
                    AddHL(BC);
                    return 8;

                case 0x0A: // LD A, [BC]
                    A = _memory.ReadByte(BC);
                    return 8;

                case 0x0B: // DEC BC
                    BC--;
                    return 8;

                case 0x0C: // INC C
                    C = Inc8(C);
                    return 4;

                case 0x0D: // DEC C
                    C = Dec8(C);
                    return 4;

                case 0x0E: // LD C, n8
                    C = ReadIMM8();
                    return 8;

                case 0x0F: // RRCA
                    Rrca();
                    return 4;

                // 0x1x
                case 0x10: // STOP n8
                    ReadIMM8(); // consume padding byte
                    IsStopped = true;
                    IsHalted = true;
                    return 4;

                case 0x11: // LD DE, n16
                    DE = ReadIMM16();
                    return 12;

                case 0x12: // LD [DE], A
                    _memory.WriteByte(DE, A);
                    return 8;

                case 0x13: // INC DE
                    DE++;
                    return 8;

                case 0x14: // INC D
                    D = Inc8(D);
                    return 4;

                case 0x15: // DEC D
                    D = Dec8(D);
                    return 4;

                case 0x16: // LD D, n8
                    D = ReadIMM8();
                    return 8;

                case 0x17: // RLA
                    Rla();
                    return 4;

                case 0x18: // JR e8
                    JrRelative(true);
                    return 12;

                case 0x19: // ADD HL, DE
                    AddHL(DE);
                    return 8;

                case 0x1A: // LD A, [DE]
                    A = _memory.ReadByte(DE);
                    return 8;

                case 0x1B: // DEC DE
                    DE--;
                    return 8;

                case 0x1C: // INC E
                    E = Inc8(E);
                    return 4;

                case 0x1D: // DEC E
                    E = Dec8(E);
                    return 4;

                case 0x1E: // LD E, n8
                    E = ReadIMM8();
                    return 8;

                case 0x1F: // RRA
                    Rra();
                    return 4;

                // 0x2x
                case 0x20: // JR NZ, e8
                    return JrRelative(!FlagZ) ? 12 : 8;

                case 0x21: // LD HL, n16
                    HL = ReadIMM16();
                    return 12;

                case 0x22: // LD [HL+], A
                    _memory.WriteByte(HL, A);
                    HL++;
                    return 8;

                case 0x23: // INC HL
                    HL++;
                    return 8;

                case 0x24: // INC H
                    H = Inc8(H);
                    return 4;

                case 0x25: // DEC H
                    H = Dec8(H);
                    return 4;

                case 0x26: // LD H, n8
                    H = ReadIMM8();
                    return 8;

                case 0x27: // DAA
                    Daa();
                    return 4;

                case 0x28: // JR Z, e8
                    return JrRelative(FlagZ) ? 12 : 8;

                case 0x29: // ADD HL, HL
                    AddHL(HL);
                    return 8;

                case 0x2A: // LD A, [HL+]
                    A = _memory.ReadByte(HL);
                    HL++;
                    return 8;

                case 0x2B: // DEC HL
                    HL--;
                    return 8;

                case 0x2C: // INC L
                    L = Inc8(L);
                    return 4;

                case 0x2D: // DEC L
                    L = Dec8(L);
                    return 4;

                case 0x2E: // LD L, n8
                    L = ReadIMM8();
                    return 8;

                case 0x2F: // CPL
                    A = (byte)~A;
                    FlagN = true;
                    FlagH = true;
                    return 4;

                // 0x3x
                case 0x30: // JR NC, e8
                    return JrRelative(!FlagC) ? 12 : 8;

                case 0x31: // LD SP, n16
                    SP = ReadIMM16();
                    return 12;

                case 0x32: // LD [HL-], A
                    _memory.WriteByte(HL, A);
                    HL--;
                    return 8;

                case 0x33: // INC SP
                    SP++;
                    return 8;

                case 0x34: // INC [HL]
                    {
                        byte val = _memory.ReadByte(HL);
                        val = Inc8(val);
                        _memory.WriteByte(HL, val);
                        return 12;
                    }

                case 0x35: // DEC [HL]
                    {
                        byte val = _memory.ReadByte(HL);
                        val = Dec8(val);
                        _memory.WriteByte(HL, val);
                        return 12;
                    }

                case 0x36: // LD [HL], n8
                    _memory.WriteByte(HL, ReadIMM8());
                    return 12;

                case 0x37: // SCF
                    FlagN = false;
                    FlagH = false;
                    FlagC = true;
                    return 4;

                case 0x38: // JR C, e8
                    return JrRelative(FlagC) ? 12 : 8;

                case 0x39: // ADD HL, SP
                    AddHL(SP);
                    return 8;

                case 0x3A: // LD A, [HL-]
                    A = _memory.ReadByte(HL);
                    HL--;
                    return 8;

                case 0x3B: // DEC SP
                    SP--;
                    return 8;

                case 0x3C: // INC A
                    A = Inc8(A);
                    return 4;

                case 0x3D: // DEC A
                    A = Dec8(A);
                    return 4;

                case 0x3E: // LD A, n8
                    A = ReadIMM8();
                    return 8;

                case 0x3F: // CCF
                    FlagN = false;
                    FlagH = false;
                    FlagC = !FlagC;
                    return 4;

                // 0x40–0x7F: LD r1, r2 + HALT
                case >= 0x40 and <= 0x7F:
                    if (opcode == 0x76) // HALT
                    {
                        IsHalted = true;
                        return 4;
                    }
                    else
                    {
                        int dest = (opcode >> 3) & 0x07;
                        int src = opcode & 0x07;
                        byte val = ReadReg8(src);
                        WriteReg8(dest, val);
                        // any access involving [HL] takes 8 cycles, else 4
                        bool mem = (dest == 6) || (src == 6);
                        return mem ? 8 : 4;
                    }

                // 0x80–0xBF: ALU A, r
                case >= 0x80 and <= 0xBF:
                    {
                        int op = (opcode - 0x80) / 8; // 0..7
                        int src = opcode & 0x07;
                        byte val = ReadReg8(src);

                        switch (op)
                        {
                            case 0: AddA(val); break;        // ADD
                            case 1: AdcA(val); break;        // ADC
                            case 2: SubA(val); break;        // SUB
                            case 3: SbcA(val); break;        // SBC
                            case 4: AndA(val); break;        // AND
                            case 5: XorA(val); break;        // XOR
                            case 6: OrA(val); break;         // OR
                            case 7: CpA(val); break;         // CP
                        }

                        bool mem = (src == 6);
                        return mem ? 8 : 4;
                    }

                // 0xC0–0xFF: control / immediate ALU / misc
                // Cx
                case 0xC0: // RET NZ
                    if (!FlagZ)
                    {
                        PC = PopWord();
                        return 20;
                    }
                    return 8;

                case 0xC1: // POP BC
                    BC = PopWord();
                    return 12;

                case 0xC2: // JP NZ, a16
                    {
                        ushort addr = ReadIMM16();
                        if (!FlagZ)
                        {
                            PC = addr;
                            return 16;
                        }
                        return 12;
                    }

                case 0xC3: // JP a16
                    PC = ReadIMM16();
                    return 16;

                case 0xC4: // CALL NZ, a16
                    {
                        ushort addr = ReadIMM16();
                        if (!FlagZ)
                        {
                            PushWord(PC);
                            PC = addr;
                            return 24;
                        }
                        return 12;
                    }

                case 0xC5: // PUSH BC
                    PushWord(BC);
                    return 16;

                case 0xC6: // ADD A, n8
                    AddA(ReadIMM8());
                    return 8;

                case 0xC7: // RST 00h
                    Rst(0x00);
                    return 16;

                case 0xC8: // RET Z
                    if (FlagZ)
                    {
                        PC = PopWord();
                        return 20;
                    }
                    return 8;

                case 0xC9: // RET
                    PC = PopWord();
                    return 16;

                case 0xCA: // JP Z, a16
                    {
                        ushort addr = ReadIMM16();
                        if (FlagZ)
                        {
                            PC = addr;
                            return 16;
                        }
                        return 12;
                    }

                case 0xCB: // handled at top (never reached)
                    return 0;

                case 0xCC: // CALL Z, a16
                    {
                        ushort addr = ReadIMM16();
                        if (FlagZ)
                        {
                            PushWord(PC);
                            PC = addr;
                            return 24;
                        }
                        return 12;
                    }

                case 0xCD: // CALL a16
                    {
                        ushort addr = ReadIMM16();
                        PushWord(PC);
                        PC = addr;
                        return 24;
                    }

                case 0xCE: // ADC A, n8
                    AdcA(ReadIMM8());
                    return 8;

                case 0xCF: // RST 08h
                    Rst(0x08);
                    return 16;

                // Dx
                case 0xD0: // RET NC
                    if (!FlagC)
                    {
                        PC = PopWord();
                        return 20;
                    }
                    return 8;

                case 0xD1: // POP DE
                    DE = PopWord();
                    return 12;

                case 0xD2: // JP NC, a16
                    {
                        ushort addr = ReadIMM16();
                        if (!FlagC)
                        {
                            PC = addr;
                            return 16;
                        }
                        return 12;
                    }

                case 0xD3: // — (unused)
                    return 4;

                case 0xD4: // CALL NC, a16
                    {
                        ushort addr = ReadIMM16();
                        if (!FlagC)
                        {
                            PushWord(PC);
                            PC = addr;
                            return 24;
                        }
                        return 12;
                    }

                case 0xD5: // PUSH DE
                    PushWord(DE);
                    return 16;

                case 0xD6: // SUB A, n8
                    SubA(ReadIMM8());
                    return 8;

                case 0xD7: // RST 10h
                    Rst(0x10);
                    return 16;

                case 0xD8: // RET C
                    if (FlagC)
                    {
                        PC = PopWord();
                        return 20;
                    }
                    return 8;

                case 0xD9: // RETI
                    PC = PopWord();
                    IME = true;
                    return 16;

                case 0xDA: // JP C, a16
                    {
                        ushort addr = ReadIMM16();
                        if (FlagC)
                        {
                            PC = addr;
                            return 16;
                        }
                        return 12;
                    }

                case 0xDB: // — (unused)
                    return 4;

                case 0xDC: // CALL C, a16
                    {
                        ushort addr = ReadIMM16();
                        if (FlagC)
                        {
                            PushWord(PC);
                            PC = addr;
                            return 24;
                        }
                        return 12;
                    }

                case 0xDD: // — (unused)
                    return 4;

                case 0xDE: // SBC A, n8
                    SbcA(ReadIMM8());
                    return 8;

                case 0xDF: // RST 18h
                    Rst(0x18);
                    return 16;

                // Ex
                case 0xE0: // LDH [a8], A
                    {
                        ushort addr = (ushort)(0xFF00 + ReadIMM8());
                        _memory.WriteByte(addr, A);
                        return 12;
                    }

                case 0xE1: // POP HL
                    HL = PopWord();
                    return 12;

                case 0xE2: // LDH [C], A
                    _memory.WriteByte((ushort)(0xFF00 + C), A);
                    return 8;

                case 0xE3: // — (unused)
                case 0xE4:
                    return 4;

                case 0xE5: // PUSH HL
                    PushWord(HL);
                    return 16;

                case 0xE6: // AND A, n8
                    AndA(ReadIMM8());
                    return 8;

                case 0xE7: // RST 20h
                    Rst(0x20);
                    return 16;

                case 0xE8: // ADD SP, e8
                    {
                        sbyte e = (sbyte)ReadIMM8();
                        int result = SP + e;
                        FlagZ = false;
                        FlagN = false;
                        int low = (SP & 0xFF) + (e & 0xFF);
                        FlagH = ((SP & 0x0F) + (e & 0x0F)) > 0x0F;
                        FlagC = low > 0xFF;
                        SP = (ushort)result;
                        return 16;
                    }

                case 0xE9: // JP HL
                    PC = HL;
                    return 4;

                case 0xEA: // LD [a16], A
                    {
                        ushort addr = ReadIMM16();
                        _memory.WriteByte(addr, A);
                        return 16;
                    }

                case 0xEB: // — (unused)
                case 0xEC:
                case 0xED:
                    return 4;

                case 0xEE: // XOR A, n8
                    XorA(ReadIMM8());
                    return 8;

                case 0xEF: // RST 28h
                    Rst(0x28);
                    return 16;

                // Fx
                case 0xF0: // LDH A, [a8]
                    {
                        ushort addr = (ushort)(0xFF00 + ReadIMM8());
                        A = _memory.ReadByte(addr);
                        return 12;
                    }

                case 0xF1: // POP AF
                    AF = PopWord();
                    return 12;

                case 0xF2: // LDH A, [C]
                    A = _memory.ReadByte((ushort)(0xFF00 + C));
                    return 8;

                case 0xF3: // DI
                    IME = false;
                    _enableIMENextCycle = false;
                    return 4;

                case 0xF4: // — (unused)
                    return 4;

                case 0xF5: // PUSH AF
                    PushWord(AF);
                    return 16;

                case 0xF6: // OR A, n8
                    OrA(ReadIMM8());
                    return 8;

                case 0xF7: // RST 30h
                    Rst(0x30);
                    return 16;

                case 0xF8: // LD HL, SP+e8
                    {
                        sbyte e = (sbyte)ReadIMM8();
                        int result = SP + e;
                        FlagZ = false;
                        FlagN = false;
                        int low = (SP & 0xFF) + (e & 0xFF);
                        FlagH = ((SP & 0x0F) + (e & 0x0F)) > 0x0F;
                        FlagC = low > 0xFF;
                        HL = (ushort)result;
                        return 12;
                    }

                case 0xF9: // LD SP, HL
                    SP = HL;
                    return 8;

                case 0xFA: // LD A, [a16]
                    {
                        ushort addr = ReadIMM16();
                        A = _memory.ReadByte(addr);
                        return 16;
                    }

                case 0xFB: // EI
                    _enableIMENextCycle = true;
                    return 4;

                case 0xFC: // — (unused)
                case 0xFD:
                    return 4;

                case 0xFE: // CP A, n8
                    CpA(ReadIMM8());
                    return 8;

                case 0xFF: // RST 38h
                    Rst(0x38);
                    return 16;

            }
        }

        // =========================
        // CB-prefixed opcodes
        // =========================
        private int ExecuteCB(byte cb)
        {
            int group = cb >> 6;
            int op = (cb >> 3) & 0x07;
            int r = cb & 0x07;

            switch (group)
            {
                case 0: // RLC/RRC/RL/RR/SLA/SRA/SWAP/SRL
                    {
                        byte value = ReadReg8(r);
                        byte result;
                        switch (op)
                        {
                            case 0: result = Rlc(value); break;
                            case 1: result = Rrc(value); break;
                            case 2: result = Rl(value); break;
                            case 3: result = Rr(value); break;
                            case 4: result = Sla(value); break;
                            case 5: result = Sra(value); break;
                            case 6: result = Swap(value); break;
                            case 7: result = Srl(value); break;
                            default: result = value; break;
                        }
                        WriteReg8(r, result);
                        bool mem = (r == 6);
                        return mem ? 16 : 8;
                    }

                case 1: // BIT b, r
                    {
                        int bit = op;
                        byte value = ReadReg8(r);
                        FlagZ = (value & (1 << bit)) == 0;
                        FlagN = false;
                        FlagH = true;
                        // C unchanged
                        bool mem = (r == 6);
                        return mem ? 12 : 8;
                    }

                case 2: // RES b, r
                    {
                        int bit = op;
                        byte value = ReadReg8(r);
                        value = (byte)(value & ~(1 << bit));
                        WriteReg8(r, value);
                        bool mem = (r == 6);
                        return mem ? 16 : 8;
                    }

                case 3: // SET b, r
                    {
                        int bit = op;
                        byte value = ReadReg8(r);
                        value = (byte)(value | (1 << bit));
                        WriteReg8(r, value);
                        bool mem = (r == 6);
                        return mem ? 16 : 8;
                    }
            }

            return 8;
        }

        // =========================
        // Interrupts
        // =========================
        private bool HandleInterrupts(ref int cycles)
        {
            byte ie = _memory.ReadByte(0xFFFF);
            byte iff = _memory.ReadByte(0xFF0F);
            byte pending = (byte)(ie & iff);
            if (pending == 0)
                return false;

            // HALT wake behavior (IME or not)
            if (IsHalted)
                IsHalted = false;

            if (!IME)
                return false;

            int vector = -1;
            int index = -1;
            if ((pending & 0x01) != 0) { vector = 0x40; index = 0; } // VBlank
            else if ((pending & 0x02) != 0) { vector = 0x48; index = 1; } // LCD STAT
            else if ((pending & 0x04) != 0) { vector = 0x50; index = 2; } // Timer
            else if ((pending & 0x08) != 0) { vector = 0x58; index = 3; } // Serial
            else if ((pending & 0x10) != 0) { vector = 0x60; index = 4; } // Joypad

            if (vector < 0)
                return false;

            IME = false;

            // Clear IF flag bit
            byte newIf = (byte)(iff & ~(1 << index));
            _memory.WriteByte(0xFF0F, newIf);

            // Push PC and jump to vector
            PushWord(PC);
            PC = (ushort)vector;

            cycles += 20;
            return true;
        }

        // =========================
        // Helpers: memory / immediates
        // =========================
        private byte ReadIMM8()
        {
            return _memory.ReadByte(PC++);
        }

        private ushort ReadIMM16()
        {
            ushort val = ReadWord(PC);
            PC += 2;
            return val;
        }

        private ushort ReadWord(ushort addr)
        {
            byte lo = _memory.ReadByte(addr);
            byte hi = _memory.ReadByte((ushort)(addr + 1));
            return (ushort)((hi << 8) | lo);
        }

        private void PushWord(ushort value)
        {
            SP -= 2;
            _memory.WriteByte(SP, (byte)(value & 0xFF));
            _memory.WriteByte((ushort)(SP + 1), (byte)(value >> 8));
        }

        private ushort PopWord()
        {
            byte lo = _memory.ReadByte(SP);
            byte hi = _memory.ReadByte((ushort)(SP + 1));
            SP += 2;
            return (ushort)((hi << 8) | lo);
        }

        // =========================
        // Helpers: 8-bit arithmetic
        // =========================
        private byte Inc8(byte value)
        {
            byte res = (byte)(value + 1);
            FlagZ = res == 0;
            FlagN = false;
            FlagH = (value & 0x0F) + 1 > 0x0F;
            return res;
        }

        private byte Dec8(byte value)
        {
            byte res = (byte)(value - 1);
            FlagZ = res == 0;
            FlagN = true;
            FlagH = (value & 0x0F) == 0x00;
            return res;
        }

        private void AddA(byte value)
        {
            int a = A;
            int res = a + value;
            FlagZ = (byte)res == 0;
            FlagN = false;
            FlagH = ((a & 0x0F) + (value & 0x0F)) > 0x0F;
            FlagC = res > 0xFF;
            A = (byte)res;
        }

        private void AdcA(byte value)
        {
            int a = A;
            int c = FlagC ? 1 : 0;
            int res = a + value + c;
            FlagZ = (byte)res == 0;
            FlagN = false;
            FlagH = ((a & 0x0F) + (value & 0x0F) + c) > 0x0F;
            FlagC = res > 0xFF;
            A = (byte)res;
        }

        private void SubA(byte value)
        {
            int a = A;
            int res = a - value;
            FlagZ = (byte)res == 0;
            FlagN = true;
            FlagH = (a & 0x0F) < (value & 0x0F);
            FlagC = a < value;
            A = (byte)res;
        }

        private void SbcA(byte value)
        {
            int a = A;
            int c = FlagC ? 1 : 0;
            int res = a - value - c;
            FlagZ = (byte)res == 0;
            FlagN = true;
            FlagH = (a & 0x0F) < ((value & 0x0F) + c);
            FlagC = a < value + c;
            A = (byte)res;
        }

        private void AndA(byte value)
        {
            A = (byte)(A & value);
            FlagZ = A == 0;
            FlagN = false;
            FlagH = true;
            FlagC = false;
        }

        private void OrA(byte value)
        {
            A = (byte)(A | value);
            FlagZ = A == 0;
            FlagN = false;
            FlagH = false;
            FlagC = false;
        }

        private void XorA(byte value)
        {
            A = (byte)(A ^ value);
            FlagZ = A == 0;
            FlagN = false;
            FlagH = false;
            FlagC = false;
        }

        private void CpA(byte value)
        {
            int a = A;
            int res = a - value;
            FlagZ = (byte)res == 0;
            FlagN = true;
            FlagH = (a & 0x0F) < (value & 0x0F);
            FlagC = a < value;
        }

        private void AddHL(ushort value)
        {
            int hl = HL;
            int sum = hl + value;

            FlagN = false;
            FlagH = ((hl & 0x0FFF) + (value & 0x0FFF)) > 0x0FFF;
            FlagC = sum > 0xFFFF;

            HL = (ushort)sum;
        }

        // DAA (decimal adjust) after addition/subtraction
        private void Daa()
        {
            int a = A;
            int adjust = 0;
            bool carry = FlagC;

            if (!FlagN)
            {
                if (FlagH || (a & 0x0F) > 9) adjust |= 0x06;
                if (carry || a > 0x99)
                {
                    adjust |= 0x60;
                    carry = true;
                }
                a += adjust;
            }
            else
            {
                if (FlagH) adjust |= 0x06;
                if (carry) adjust |= 0x60;
                a -= adjust;
            }

            A = (byte)a;
            FlagZ = A == 0;
            FlagH = false;
            FlagC = carry;
        }

        // =========================
        // Helpers: rotates & shifts (A + generic)
        // =========================
        private void Rlca()
        {
            byte old = A;
            A = (byte)((A << 1) | (A >> 7));
            FlagZ = false;
            FlagN = false;
            FlagH = false;
            FlagC = (old & 0x80) != 0;
        }

        private void Rrca()
        {
            byte old = A;
            A = (byte)((A >> 1) | (A << 7));
            FlagZ = false;
            FlagN = false;
            FlagH = false;
            FlagC = (old & 0x01) != 0;
        }

        private void Rla()
        {
            byte carryIn = (byte)(FlagC ? 1 : 0);
            byte old = A;
            A = (byte)((A << 1) | carryIn);
            FlagZ = false;
            FlagN = false;
            FlagH = false;
            FlagC = (old & 0x80) != 0;
        }

        private void Rra()
        {
            byte carryIn = (byte)(FlagC ? 0x80 : 0);
            byte old = A;
            A = (byte)((A >> 1) | carryIn);
            FlagZ = false;
            FlagN = false;
            FlagH = false;
            FlagC = (old & 0x01) != 0;
        }

        private byte Rlc(byte v)
        {
            byte carry = (byte)((v & 0x80) >> 7);
            byte res = (byte)((v << 1) | carry);
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = carry != 0;
            return res;
        }

        private byte Rrc(byte v)
        {
            byte carry = (byte)(v & 0x01);
            byte res = (byte)((v >> 1) | (carry << 7));
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = carry != 0;
            return res;
        }

        private byte Rl(byte v)
        {
            byte carryIn = (byte)(FlagC ? 1 : 0);
            byte carry = (byte)((v & 0x80) >> 7);
            byte res = (byte)((v << 1) | carryIn);
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = carry != 0;
            return res;
        }

        private byte Rr(byte v)
        {
            byte carryIn = (byte)(FlagC ? 0x80 : 0);
            byte carry = (byte)(v & 0x01);
            byte res = (byte)((v >> 1) | carryIn);
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = carry != 0;
            return res;
        }

        private byte Sla(byte v)
        {
            byte carry = (byte)((v & 0x80) >> 7);
            byte res = (byte)(v << 1);
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = carry != 0;
            return res;
        }

        private byte Sra(byte v)
        {
            byte carry = (byte)(v & 0x01);
            byte res = (byte)((v >> 1) | (v & 0x80)); // keep sign bit
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = carry != 0;
            return res;
        }

        private byte Srl(byte v)
        {
            byte carry = (byte)(v & 0x01);
            byte res = (byte)(v >> 1);
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = carry != 0;
            return res;
        }

        private byte Swap(byte v)
        {
            byte res = (byte)(((v & 0x0F) << 4) | ((v & 0xF0) >> 4));
            FlagZ = res == 0;
            FlagN = false;
            FlagH = false;
            FlagC = false;
            return res;
        }

        // =========================
        // Helpers: registers / flow
        // =========================
        private byte ReadReg8(int code)
        {
            return code switch
            {
                0 => B,
                1 => C,
                2 => D,
                3 => E,
                4 => H,
                5 => L,
                6 => _memory.ReadByte(HL),
                7 => A,
                _ => 0
            };
        }

        private void WriteReg8(int code, byte value)
        {
            switch (code)
            {
                case 0: B = value; break;
                case 1: C = value; break;
                case 2: D = value; break;
                case 3: E = value; break;
                case 4: H = value; break;
                case 5: L = value; break;
                case 6: _memory.WriteByte(HL, value); break;
                case 7: A = value; break;
            }
        }

        // JR helper: returns true if branch taken
        private bool JrRelative(bool condition)
        {
            sbyte offset = (sbyte)ReadIMM8();
            if (condition)
                PC = (ushort)(PC + offset);
            return condition;
        }

        private void Rst(ushort vector)
        {
            PushWord(PC);
            PC = vector;
        }
    }
}
