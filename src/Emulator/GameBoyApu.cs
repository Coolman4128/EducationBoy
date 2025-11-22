using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace EducationBoy.Emulator;

/// <summary>
/// Minimal audio processing unit: two square channels with simple mixing.
/// </summary>
public class GameBoyApu : IDisposable
{
    private const int SampleRate = 44100;
    private const float MaxMixerGain = 0.25f; // limit overall loudness
    private const float NoiseMixGain = 0.5f;  // attenuate noise channel
    private float _mixerGain = MaxMixerGain;

    private readonly double _cyclesPerSample;
    private double _cycleAccumulator;

    private readonly BufferedWaveProvider _buffer;
    private readonly WaveOutEvent _waveOut;
    private readonly byte[] _sampleBytes = new byte[8]; // two float samples (stereo)
    private readonly byte[] _registers = new byte[0x30]; // FF10-FF3F

    private bool _masterEnabled = true;
    private byte _nr50 = 0x77; // Master volume
    private byte _nr51 = 0xF3; // Panning
    private byte _nr52 = 0xF1; // Master enable + status

    private readonly SquareChannel _ch1 = new();
    private readonly SquareChannel _ch2 = new();
    private readonly WaveChannel _ch3 = new();
    private readonly NoiseChannel _ch4 = new();

    public GameBoyApu()
    {
        _cyclesPerSample = GameBoyClock.ClockSpeedHz / SampleRate;

        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true
        };

        _waveOut = new WaveOutEvent { DesiredLatency = 60 };
        _waveOut.Init(_buffer);
        _waveOut.Play();
    }

    public void Reset()
    {
        Array.Clear(_registers, 0, _registers.Length);
        _cycleAccumulator = 0;
        _masterEnabled = true;
        _nr50 = 0x77;
        _nr51 = 0xF3;
        _nr52 = 0xF1;
        _ch1.Disable();
        _ch2.Disable();
        _ch3.Disable();
        _ch4.Disable();
    }

    public void SetMasterVolume(float volume)
    {
        // Apply a slight curve so the midpoint is ~50% lower than before
        float normalized = Math.Clamp(volume / MaxMixerGain, 0f, 1f);
        _mixerGain = MaxMixerGain * normalized * normalized;
    }

    public void Step(int cycles)
    {
        _cycleAccumulator += cycles;
        while (_cycleAccumulator >= _cyclesPerSample)
        {
            _cycleAccumulator -= _cyclesPerSample;
            WriteSample();
        }
    }

    public void WriteRegister(ushort address, byte value)
    {
        int idx = address - 0xFF10;
        if ((uint)idx < _registers.Length)
            _registers[idx] = value;

        switch (address)
        {
            case 0xFF10: // NR10 sweep (ignored)
                break;

            case 0xFF11: // NR11 duty/length
                _ch1.Duty = (byte)((value >> 6) & 0x03);
                break;

            case 0xFF12: // NR12 envelope (use initial volume)
                _ch1.Volume = (byte)(value >> 4);
                if (_ch1.Volume == 0)
                    _ch1.Enabled = false;
                break;

            case 0xFF13: // NR13 frequency low
                _ch1.Frequency = (ushort)((_ch1.Frequency & 0x0700) | value);
                break;

            case 0xFF14: // NR14 frequency high + trigger
                _ch1.Frequency = (ushort)((_ch1.Frequency & 0x00FF) | ((value & 0x07) << 8));
                if ((value & 0x80) != 0)
                    _ch1.Trigger();
                break;

            case 0xFF16: // NR21 duty/length
                _ch2.Duty = (byte)((value >> 6) & 0x03);
                break;

            case 0xFF17: // NR22 envelope
                _ch2.Volume = (byte)(value >> 4);
                if (_ch2.Volume == 0)
                    _ch2.Enabled = false;
                break;

            case 0xFF18: // NR23 frequency low
                _ch2.Frequency = (ushort)((_ch2.Frequency & 0x0700) | value);
                break;

            case 0xFF19: // NR24 frequency high + trigger
                _ch2.Frequency = (ushort)((_ch2.Frequency & 0x00FF) | ((value & 0x07) << 8));
                if ((value & 0x80) != 0)
                    _ch2.Trigger();
                break;

            case 0xFF1A: // NR30 wave DAC on/off
                _ch3.SetDacEnabled((value & 0x80) != 0);
                break;

            case 0xFF1B: // NR31 length (ignored)
                _ch3.Length = value;
                break;

            case 0xFF1C: // NR32 volume
                _ch3.VolumeCode = (byte)((value >> 5) & 0x03);
                break;

            case 0xFF1D: // NR33 freq low
                _ch3.Frequency = (ushort)((_ch3.Frequency & 0x0700) | value);
                break;

            case 0xFF1E: // NR34 freq high + trigger
                _ch3.Frequency = (ushort)((_ch3.Frequency & 0x00FF) | ((value & 0x07) << 8));
                if ((value & 0x80) != 0)
                    _ch3.Trigger();
                break;

            case 0xFF20: // NR41 noise length (ignored)
                _ch4.Length = value;
                break;

            case 0xFF21: // NR42 envelope
                _ch4.SetEnvelope(value);
                break;

            case 0xFF22: // NR43 polynomial counter
                _ch4.SetPolynomial(value);
                break;

            case 0xFF23: // NR44 trigger
                if ((value & 0x80) != 0)
                    _ch4.Trigger();
                break;

            case 0xFF24: // NR50 master volume
                _nr50 = value;
                break;

            case 0xFF25: // NR51 panning
                _nr51 = value;
                break;

            case 0xFF26: // NR52 master control
                _masterEnabled = (value & 0x80) != 0;
                _nr52 = (byte)(value | 0x70);
                if (!_masterEnabled)
                {
                    _ch1.Disable();
                    _ch2.Disable();
                    _ch3.Disable();
                    _ch4.Disable();
                }
                break;

            case >= 0xFF30 and <= 0xFF3F: // Wave pattern RAM
                _ch3.WriteWaveRam(address - 0xFF30, value);
                break;
        }
    }

    public byte ReadRegister(ushort address)
    {
        if (address == 0xFF26)
        {
            byte status = (byte)(_masterEnabled ? 0x80 : 0x00);
            if (_ch1.Enabled) status |= 0x01;
            if (_ch2.Enabled) status |= 0x02;
            if (_ch3.Enabled) status |= 0x04;
            if (_ch4.Enabled) status |= 0x08;
            return (byte)(status | 0x70);
        }

        int idx = address - 0xFF10;
        if ((uint)idx < _registers.Length)
            return _registers[idx];

        return 0xFF;
    }

    private void WriteSample()
    {
        Mix(out float left, out float right);

        MemoryMarshal.Write(_sampleBytes.AsSpan(0, 4), in left);
        MemoryMarshal.Write(_sampleBytes.AsSpan(4, 4), in right);
        _buffer.AddSamples(_sampleBytes, 0, _sampleBytes.Length);
    }

    private void Mix(out float left, out float right)
    {
        if (!_masterEnabled)
        {
            left = right = 0f;
            return;
        }

        double dt = 1.0 / SampleRate;
        float ch1 = _ch1.Next(dt);
        float ch2 = _ch2.Next(dt);
        float ch3 = _ch3.Next(dt);
        float ch4 = _ch4.Next(dt) * NoiseMixGain;

        left = 0f;
        right = 0f;

        // NR51 routes
        if ((_nr51 & 0x10) != 0) left += ch1;
        if ((_nr51 & 0x20) != 0) left += ch2;
        if ((_nr51 & 0x40) != 0) left += ch3;
        if ((_nr51 & 0x80) != 0) left += ch4;
        if ((_nr51 & 0x01) != 0) right += ch1;
        if ((_nr51 & 0x02) != 0) right += ch2;
        if ((_nr51 & 0x04) != 0) right += ch3;
        if ((_nr51 & 0x08) != 0) right += ch4;

        float leftGain = (((_nr50 >> 4) & 0x07) + 1) / 8f;
        float rightGain = ((_nr50 & 0x07) + 1) / 8f;

        left *= leftGain * _mixerGain;
        right *= rightGain * _mixerGain;
    }

    public void Dispose()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
    }

    private sealed class SquareChannel
    {
        public byte Duty;
        public byte Volume;
        public ushort Frequency;
        public bool Enabled;

        private double _phase;

        public float Next(double dt)
        {
            if (!Enabled || Volume == 0 || Frequency >= 2048)
                return 0f;

            double freq = 131072.0 / Math.Max(1, 2048 - Frequency);
            _phase += freq * dt;
            _phase -= Math.Floor(_phase);

            double dutyThreshold = Duty switch
            {
                0 => 1.0 / 8.0,
                1 => 2.0 / 8.0,
                2 => 4.0 / 8.0,
                3 => 6.0 / 8.0,
                _ => 0.5
            };

            double amp = _phase < dutyThreshold ? 1.0 : -1.0;
            return (float)(amp * (Volume / 15.0));
        }

        public void Trigger()
        {
            Enabled = Volume > 0;
            _phase = 0.0;
        }

        public void Disable()
        {
            Enabled = false;
            Volume = 0;
            _phase = 0;
        }
    }

    private sealed class WaveChannel
    {
        public bool Enabled;
        public bool DacEnabled = true;
        public byte VolumeCode;
        public ushort Frequency;
        public byte Length;

        private readonly byte[] _waveRam = new byte[16]; // 32 samples, 4-bit each
        private double _phase;

        public void SetDacEnabled(bool enabled)
        {
            DacEnabled = enabled;
            if (!enabled)
                Disable();
        }

        public void Trigger()
        {
            if (!DacEnabled)
            {
                Enabled = false;
                return;
            }

            Enabled = true;
            _phase = 0.0;
        }

        public void WriteWaveRam(int index, byte value)
        {
            if ((uint)index < _waveRam.Length)
                _waveRam[index] = value;
        }

        public float Next(double dt)
        {
            if (!Enabled || !DacEnabled)
                return 0f;

            double freq = 65536.0 / Math.Max(1, 2048 - Frequency);
            if (freq <= 0)
                return 0f;

            _phase += freq * dt;
            _phase -= Math.Floor(_phase);

            double pos = _phase * 32.0;
            int sampleIndex = (int)pos & 0x1F;
            byte packed = _waveRam[sampleIndex / 2];
            int nybble = (sampleIndex & 1) == 0 ? (packed >> 4) & 0x0F : packed & 0x0F;

            if (VolumeCode == 0)
                return 0f;

            float sample = (nybble / 15f) * 2f - 1f;
            float vol = VolumeCode switch
            {
                1 => 1f,
                2 => 0.5f,
                3 => 0.25f,
                _ => 0f
            };

            return sample * vol;
        }

        public void Disable()
        {
            Enabled = false;
            _phase = 0.0;
        }
    }

    private sealed class NoiseChannel
    {
        public bool Enabled;
        public bool DacEnabled = true;
        public byte Length;
        public byte Volume;
        public byte DivisorCode;
        public byte ShiftClock;
        public bool Width7;

        private ushort _lfsr = 0x7FFF;
        private double _period = 1.0 / 524288.0;
        private double _timer;

        private static readonly int[] Divisors = { 8, 16, 32, 48, 64, 80, 96, 112 };

        public void SetEnvelope(byte value)
        {
            Volume = (byte)((value >> 4) & 0x0F);
            DacEnabled = (value & 0xF8) != 0;

            if (!DacEnabled)
                Disable();
        }

        public void SetPolynomial(byte value)
        {
            DivisorCode = (byte)(value & 0x07);
            Width7 = (value & 0x08) != 0;
            ShiftClock = (byte)((value >> 4) & 0x0F);
            UpdatePeriod();
        }

        public void Trigger()
        {
            if (!DacEnabled)
            {
                Enabled = false;
                return;
            }

            Enabled = Volume > 0;
            _lfsr = 0x7FFF;
            _timer = 0.0;
        }

        public float Next(double dt)
        {
            if (!Enabled || !DacEnabled || Volume == 0)
                return 0f;

            if (_period <= 0)
                return 0f;

            _timer += dt;
            while (_timer >= _period)
            {
                _timer -= _period;
                StepLfsr();
            }

            float amp = ((_lfsr & 0x01) == 0) ? 1f : -1f;
            return amp * (Volume / 15f);
        }

        public void Disable()
        {
            Enabled = false;
            Volume = 0;
            _timer = 0;
        }

        private void UpdatePeriod()
        {
            int divisor = Divisors[Math.Min(Divisors.Length - 1, DivisorCode)];
            double freq = 524288.0 / divisor / Math.Pow(2, ShiftClock + 1);
            _period = freq > 0 ? 1.0 / freq : double.MaxValue;
        }

        private void StepLfsr()
        {
            int bit = (_lfsr & 1) ^ ((_lfsr >> 1) & 1);
            _lfsr = (ushort)((_lfsr >> 1) | (bit << 14));

            if (Width7)
            {
                _lfsr = (ushort)((_lfsr & ~0x40) | (bit << 6));
            }
        }
    }
}
