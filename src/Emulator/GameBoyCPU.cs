namespace EducationBoy.Emulator;

public class GameBoyCPU
{
    // Registers
    public byte A; // Accumulator
    public byte F; //Flags
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
        SP = 0xFFFE; // common starting SP, you can tweak later
        PC = 0x0100; // usually after boot ROM; for now you can also set to 0 if you're hand-feeding opcodes

        IME = false;
        _enableIMENextCycle = false;
        IsHalted = false;
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

    private int ExecuteInstruction(byte opcode)
    {
        switch (opcode)
        {
            case 0x00: // No operation (NOP)
                return 4;
            case 0x01: // Load immediate 16-bit into BC
                BC = ReadIMM16();
                return 12;
            case 0x02: // Load A into address pointed by BC
                _memory.WriteByte(BC, A);
                return 8;
            case 0x03: // Increment BC
                BC = (ushort)(BC + 1);
                return 8;
            case 0x04: // Increment B
                B = Inc8(B);
                return 4;
            case 0x05: // Decrement B
                B = Dec8(B);
                return 4;
            case 0x06: // Load immediate 8-bit into B
                B = ReadIMM8();
                return 8;
            case 0x07: // Rotate A left (RLCA)
                Rlca();
                return 4;
            case 0x08: // Load SP into address
                ushort addr = ReadIMM16();
                _memory.WriteByte(addr, (byte)(SP & 0xFF));
                _memory.WriteByte((ushort)(addr + 1), (byte)(SP >> 8));
                return 20;
            case 0x09: // Add HL to BC
                AddHL(BC);
                return 8;
            case 0x0A: // Load from address at BC to A
                A = _memory.ReadByte(BC);
                return 8;
            case 0x0B: // Decrement BC
                BC = (ushort)(BC - 1);
                return 8;
            case 0x0C: // Increment C
                C = Inc8(C);
                return 4;
            case 0x0D: // Decrement C
                C = Dec8(C);
                return 4;
            case 0x0E: // Load immediately into C
                C = ReadIMM8();
                return 8;
            case 0x0F: // Rotate A right (RRCA)
                Rrca();
                return 4;
            case 0x10: // Stop (STOP)
                ReadIMM8(); // usually 0x00, ignored
                IsStopped = true;
                IsHalted = true; // Just to simplify, treat as halted
                return 4;
            case 0x11: // Load immediate 16-bit into DE
                DE = ReadIMM16();
                return 12;
            case 0x12: // Load A into address pointed by DE
                _memory.WriteByte(DE, A);
                return 8;
            case 0x13: // Increment DE
                DE = (ushort)(DE + 1);
                return 8;
            case 0x14: // Increment D
                D = Inc8(D);
                return 4;
            case 0x15: // Decrement D
                D = Dec8(D);
                return 4;
            case 0x16: // Load immediate 8-bit into D
                D = ReadIMM8();
                return 8;
            case 0x17: // Rotate A left through carry (RLA)
                {
                    byte oldA = A;
                    A = (byte)((A << 1) | (FlagC ? 1 : 0));
                    FlagZ = false;
                    FlagN = false;
                    FlagH = false;
                    FlagC = (oldA & 0x80) != 0;
                    return 4;
                }
            case 0x18: // Jump relative
                {
                    sbyte offset = (sbyte)ReadIMM8();
                    PC = (ushort)(PC + offset);
                    return 12;
                }
        }
        return 0;
    }

    private bool HandleInterrupts(ref int cycles)
    {
        byte ie = _memory.ReadByte(0xFFFF);
        byte iff = _memory.ReadByte(0xFF0F);
        byte pending = (byte)(ie & iff);
        if (pending == 0)
        {
            // no interrupts pending
            return false;
        }

        // HALT wake behavior (IME or not)
        if (IsHalted)
        {
            IsHalted = false;
            // If IME is false we just wake and fall through to run opcode
            // If IME is true, we will service interrupt below
        }

        if (!IME)
            return false; // not allowed to service yet, just woke from HALT if we were halted

        // Service highest priority: VBlank, LCD STAT, Timer, Serial, Joypad
        int vector = -1;
        if ((pending & 0x01) != 0) vector = 0x40; // VBlank
        else if ((pending & 0x02) != 0) vector = 0x48; // LCD STAT
        else if ((pending & 0x04) != 0) vector = 0x50; // Timer
        else if ((pending & 0x08) != 0) vector = 0x58; // Serial
        else if ((pending & 0x10) != 0) vector = 0x60; // Joypad

        if (vector < 0)
            return false;

        // Clear IME
        IME = false;

        // Clear the flag bit in IF
        byte newIf = (byte)(iff & ~(1 << ((vector - 0x40) / 8)));
        _memory.WriteByte(0xFF0F, newIf);

        // Push PC
        PushWord(PC);

        // Jump to vector
        PC = (ushort)vector;

        // Interrupt costs 20 T-cycles (5 machine cycles)
        cycles += 20;

        return true;
    }

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

    private byte Inc8(byte value)
    {
        value++;
        FlagZ = value == 0;
        FlagN = false;
        // Half-carry flag
        if ((value & 0x0F) == 0x00)
            FlagH = true;
        else
            FlagH = false;
        return value;
    }

    private byte Dec8(byte value)
    {
        value--;
        FlagZ = value == 0;
        FlagN = true;
        // Half-carry flag
        if ((value & 0x0F) == 0x0F)
            FlagH = true;
        else
            FlagH = false;
        return value;
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

    private void Rlca()
    {
        byte old = A;
        byte result = (byte)((A << 1) | (A >> 7));

        A = result;
        FlagZ = false;        // Z always 0 for RLCA/RRCA
        FlagN = false;
        FlagH = false;
        FlagC = (old & 0x80) != 0;
    }

    private void Rrca()
    {
        byte old = A;
        byte result = (byte)((A >> 1) | (A << 7));

        A = result;
        FlagZ = false;
        FlagN = false;
        FlagH = false;
        FlagC = (old & 0x01) != 0;
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


}