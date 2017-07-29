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

        private static readonly ushort[] statusNeedle = new ushort[19]
        {
            0xF0, wild,
            0x3C,
            0x28, wild,
            0xF0, 0x01,
            0xE0, wild,
            0xF0, wild,
            0xE0, 0x01,
            0xF0, wild,
            0xFE, 0x02,
            0x28, wild
        };

        private static readonly ushort[] dataBlockNeedle = new ushort[42]
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
        private ushort[] statusCopy = new ushort[statusNeedle.Length];
        private IntPtr statusSource;
        private ushort hSerialConnectionStatus;
        private ushort[] dataBlockCopy = new ushort[dataBlockNeedle.Length];
        private IntPtr dataBlockSource;
        private ushort wSerialRandomNumberListBlock;
        private ushort wSerialPlayerDataBlock;
        private ushort wSerialPartyMonsPatchList;

        public byte SerialConnectionStatus
        {
            get
            {
                CheckAddrs(statusSource, statusCopy);
                return memory.ReadByte(Hram(hSerialConnectionStatus));
            }
        }

        public byte[] SerialRandomNumberListBlock
        {
            get
            {
                CheckAddrs(dataBlockSource, dataBlockCopy);
                return memory.ReadBytes(Wram(wSerialRandomNumberListBlock), 0x11);
            }
        }

        public byte[] SerialPlayerDataBlock
        {
            get
            {
                CheckAddrs(dataBlockSource, dataBlockCopy);
                return memory.ReadBytes(Wram(wSerialPlayerDataBlock), 0x1A8);
            }
        }

        public byte[] SerialPartyMonsPatchList
        {
            get
            {
                CheckAddrs(dataBlockSource, dataBlockCopy);
                return memory.ReadBytes(Wram(wSerialPartyMonsPatchList), 0xC8);
            }
        }

        public GameboyMemory(int port)
        {
            memory = new ProcessMemory(ProcessMemory.GetProcessIdFromPort(port));
            memoryOffset = memory.ReadPointer(IntPtr.Add(memory.BaseAddress, magicStrings));

            LoadAddrs();
        }

        private void CheckAddrs(IntPtr source, ushort[] copy)
        {
            try
            {
                byte[] asm = memory.ReadBytes(source, copy.Length);

                if (!MatchBytes(asm, 0, copy))
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

            if (!FindNeedle(rom, statusNeedle, out i))
                throw new Exception("Status assembly not found.");

            Array.Copy(rom, i, statusCopy, 0, statusCopy.Length);

            statusSource = IntPtr.Add(romOffset, i);
            hSerialConnectionStatus = (ushort)(0xFF00 + rom[i + 1]);

            if (!FindNeedle(rom, dataBlockNeedle, out i))
                throw new Exception("Datablock assembly not found.");

            Array.Copy(rom, i, dataBlockCopy, 0, dataBlockCopy.Length);

            dataBlockSource = IntPtr.Add(romOffset, i);
            wSerialRandomNumberListBlock = BitConverter.ToUInt16(rom, i + 1);
            wSerialPlayerDataBlock = BitConverter.ToUInt16(rom, i + 16);
            wSerialPartyMonsPatchList = BitConverter.ToUInt16(rom, i + 31);
        }

        private bool FindNeedle(byte[] haystack, ushort[] needle, out int i)
        {
            int length = haystack.Length;
            
            for (i = 0; i < length; i++)
            {
                if (MatchBytes(haystack, i, needle))
                    return true;
            }

            return false;
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
