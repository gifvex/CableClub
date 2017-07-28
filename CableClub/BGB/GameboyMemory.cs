using System;

namespace BGB
{
    class GameboyMemory
    {
        private const int magicStrings = 0x105324;
        private const int magicRom = 0x7010;
        private const int magicHram = 0xBF74;
        private const int magicWram = 0xFFF4;

        private const ushort addrSize = 0x0148;
        private const ushort wild = 0x100;

        private static readonly ushort[] asmNeedle = new ushort[42]
        {
            0x21, wild, wild,
            0x11, wild, wild,
            0x01, 0x11, 0x00,
            0xCD, wild, wild,
            0x3E, 0xFE,
            0x12,
            0x21, wild, wild,
            0x11, wild, wild,
            0x01, 0xA8, 0x01,
            0xCD, wild, wild,
            0x3E, 0xFE,
            0x12,
            0x21, wild, wild,
            0x11, wild, wild,
            0x01, 0xC8, 0x00,
            0xCD, wild, wild
        };

        private ProcessMemory memory;
        private IntPtr memoryOffset;
        private IntPtr asmSource;
        private ushort[] asmCopy = new ushort[asmNeedle.Length];
        private ushort wSerialRandomNumberListBlock;
        private ushort wSerialPlayerDataBlock;
        private ushort wSerialPartyMonsPatchList;

        public byte[] SerialRandomNumberListBlock
        {
            get
            {
                CheckAddrs();
                return memory.ReadBytes(Wram(wSerialRandomNumberListBlock), 0x11);
            }
        }

        public byte[] SerialPlayerDataBlock
        {
            get
            {
                CheckAddrs();
                return memory.ReadBytes(Wram(wSerialPlayerDataBlock), 0x1A8);
            }
        }

        public byte[] SerialPartyMonsPatchList
        {
            get
            {
                CheckAddrs();
                return memory.ReadBytes(Wram(wSerialPartyMonsPatchList), 0xC8);
            }
        }

        public byte SerialConnectionStatus
        {
            get
            {
                return memory.ReadByte(Hram(0xFFAA));
            }
        }

        public GameboyMemory(int port)
        {
            memory = new ProcessMemory(ProcessMemory.GetProcessIdFromPort(port));
            memoryOffset = memory.ReadPointer(IntPtr.Add(memory.BaseAddress, magicStrings));

            LoadAddrs();
        }

        private void CheckAddrs()
        {
            try
            {
                byte[] asm = memory.ReadBytes(asmSource, asmCopy.Length);

                if (!MatchBytes(asm, 0, asmCopy))
                    throw new Exception();
            }
            catch
            {
                LoadAddrs();
            }
        }

        private void LoadAddrs()
        {
            IntPtr romOffset;
            int size;
            byte[] rom;

            try
            {
                romOffset = memory.ReadPointer(IntPtr.Add(memoryOffset, magicRom));
                size = 0x8000 << memory.ReadByte(IntPtr.Add(romOffset, addrSize));
                rom = memory.ReadBytes(romOffset, size);
            }
            catch
            {
                throw new Exception("Could not read ROM data.");
            }

            int i;

            for (i = 0; i < size; i++)
            {
                if (MatchBytes(rom, i, asmNeedle))
                    break;
            }

            if (i == size)
                throw new Exception("Serial assembly not found.");

            asmSource = IntPtr.Add(romOffset, i);

            Array.Copy(rom, i, asmCopy, 0, asmCopy.Length);

            wSerialRandomNumberListBlock = BitConverter.ToUInt16(rom, i + 1);
            wSerialPlayerDataBlock = BitConverter.ToUInt16(rom, i + 16);
            wSerialPartyMonsPatchList = BitConverter.ToUInt16(rom, i + 31);
        }

        private bool MatchBytes(byte[] haystack, int index, ushort[] needle)
        {
            int length = needle.Length;

            if (index + length > haystack.Length)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (needle[i] != wild && haystack[index + i] != needle[i])
                    return false;
            }

            return true;
        }

        private IntPtr Wram(int address)
        {
            return IntPtr.Add(memoryOffset, magicWram + (address - 0xC000));
        }

        private IntPtr Hram(int address)
        {
            return IntPtr.Add(memoryOffset, magicHram + (address - 0xFF80));
        }
    }
}
