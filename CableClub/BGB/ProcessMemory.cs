using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security;

namespace BGB
{
    class ProcessMemory
    {
        #region pinvoke.net signatures

        public const int AF_INET = 2;

        public enum MIB_TCP_STATE
        {
            MIB_TCP_STATE_CLOSED = 1,
            MIB_TCP_STATE_LISTEN = 2,
            MIB_TCP_STATE_SYN_SENT = 3,
            MIB_TCP_STATE_SYN_RCVD = 4,
            MIB_TCP_STATE_ESTAB = 5,
            MIB_TCP_STATE_FIN_WAIT1 = 6,
            MIB_TCP_STATE_FIN_WAIT2 = 7,
            MIB_TCP_STATE_CLOSE_WAIT = 8,
            MIB_TCP_STATE_CLOSING = 9,
            MIB_TCP_STATE_LAST_ACK = 10,
            MIB_TCP_STATE_TIME_WAIT = 11,
            MIB_TCP_STATE_DELETE_TCB = 12
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint owningPid;

            public uint ProcessId
            {
                get { return owningPid; }
            }

            public IPAddress LocalAddress
            {
                get { return new IPAddress(localAddr); }
            }

            public ushort LocalPort
            {
                get
                {
                    return BitConverter.ToUInt16(new byte[2] { localPort[1], localPort[0] }, 0);
                }
            }

            public IPAddress RemoteAddress
            {
                get { return new IPAddress(remoteAddr); }
            }

            public ushort RemotePort
            {
                get
                {
                    return BitConverter.ToUInt16(new byte[2] { remotePort[1], remotePort[0] }, 0);
                }
            }

            public MIB_TCP_STATE State
            {
                get { return (MIB_TCP_STATE)state; }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.Struct, SizeConst = 1)]
            public MIB_TCPROW_OWNER_PID[] table;
        }

        public enum TCP_TABLE_CLASS
        {
            TCP_TABLE_BASIC_LISTENER,
            TCP_TABLE_BASIC_CONNECTIONS,
            TCP_TABLE_BASIC_ALL,
            TCP_TABLE_OWNER_PID_LISTENER,
            TCP_TABLE_OWNER_PID_CONNECTIONS,
            TCP_TABLE_OWNER_PID_ALL,
            TCP_TABLE_OWNER_MODULE_LISTENER,
            TCP_TABLE_OWNER_MODULE_CONNECTIONS,
            TCP_TABLE_OWNER_MODULE_ALL
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TCP_TABLE_CLASS tblClass, uint reserved = 0);

        [Flags]
        public enum ProcessAccessFlags : uint
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x000000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            QueryLimitedInformation = 0x00001000,
            Synchronize = 0x00100000
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
             ProcessAccessFlags processAccess,
             bool bInheritHandle,
             int processId
        );
        public static IntPtr OpenProcess(Process proc, ProcessAccessFlags flags)
        {
            return OpenProcess(flags, false, proc.Id);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [SuppressUnmanagedCodeSecurity]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out, MarshalAs(UnmanagedType.AsAny)] object lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            IntPtr lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        #endregion

        public static int GetProcessIdFromPort(int port)
        {
            int buffSize = 0;

            GetTcpTable(IntPtr.Zero, ref buffSize);

            IntPtr tablePtr = Marshal.AllocHGlobal(buffSize);

            try
            {
                uint ret = GetTcpTable(tablePtr, ref buffSize);

                if (ret != 0)
                    throw new Exception(AppendLastWin32Error("GetExtendedTcpTable failed."));

                MIB_TCPTABLE_OWNER_PID table = (MIB_TCPTABLE_OWNER_PID)
                    Marshal.PtrToStructure(tablePtr, typeof(MIB_TCPTABLE_OWNER_PID));
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                uint numEntries = table.dwNumEntries;
                IntPtr rowPtr = IntPtr.Add(tablePtr, 4);

                for (int i = 0; i < numEntries; i++)
                {
                    MIB_TCPROW_OWNER_PID row = (MIB_TCPROW_OWNER_PID)
                        Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_PID));

                    if (row.LocalPort == port)
                        return (int)row.ProcessId;

                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tablePtr);
            }

            throw new Exception(String.Format("Process ID from port {0} not found.", port));
        }

        private static uint GetTcpTable(IntPtr ptr, ref int buffSize)
        {
            return GetExtendedTcpTable(ptr, ref buffSize, false, AF_INET,
                TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);
        }

        private static string AppendLastWin32Error(string message)
        {
            return message + String.Format(" ({0})", Marshal.GetLastWin32Error());
        }

        private Process process;
        private IntPtr processHandle = IntPtr.Zero;

        public IntPtr BaseAddress
        {
            get
            {
                return process.MainModule.BaseAddress;
            }
        }

        public ProcessMemory(int pid)
        {
            process = Process.GetProcessById(pid);
            processHandle = OpenProcess(process, ProcessAccessFlags.VirtualMemoryRead);

            if (processHandle == IntPtr.Zero)
                throw new Exception(AppendLastWin32Error("OpenProcess returned null."));
        }

        ~ProcessMemory()
        {
            if (processHandle != IntPtr.Zero)
                CloseHandle(processHandle);
        }

        public byte[] ReadBytes(IntPtr address, int size, bool bigEndian = false)
        {
            byte[] buffer = new byte[size];
            IntPtr numBytesRead;

            if (!ReadProcessMemory(processHandle, address, buffer, size, out numBytesRead))
                throw new Exception(AppendLastWin32Error("ReadProcessMemory failed."));

            if (bigEndian)
                Array.Reverse(buffer);

            return buffer;
        }

        public byte ReadByte(IntPtr address)
        {
            return ReadBytes(address, 1)[0];
        }

        public IntPtr ReadPointer(IntPtr address)
        {
            return new IntPtr(BitConverter.ToInt32(ReadBytes(address, 4), 0));
        }
    }
}
