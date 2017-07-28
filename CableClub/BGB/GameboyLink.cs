using System;
using System.Net.Sockets;
using System.Threading;

namespace BGB
{
    class GameboyLink
    {
        public delegate void LogCallback(string line, int debugLevel = 0, bool nl = true);
        public delegate void Sync1Callback(byte b2, int i1);

        private const string host = "127.0.0.1";
        private const int defaultPort = 8765;

        private static class Command
        {
            public const byte Version = 1;
            public const byte Joypad = 101;
            public const byte Sync1 = 104;
            public const byte Sync2 = 105;
            public const byte Sync3 = 106;
            public const byte Status = 108;
        }

        private static class Status
        {
            public const byte Running = 1 << 0;
            public const byte Paused = 1 << 1;
            public const byte Reconnect = 1 << 2;
        }

        private const int bufferSize = 0x8000;
        private const int packetSize = 8;

        private LogCallback Log;
        private Sync1Callback sync1Callback;
        private TcpClient client;
        private NetworkStream stream;
        private byte[] buffer = new byte[bufferSize];
        private int bufferLength = 0;
        private bool paused = false;

        public GameboyLink(LogCallback logCallback, Sync1Callback callback, out int port)
        {
            Log = logCallback;
            sync1Callback = callback;

            Log(String.Format("Enter BGB port: (leave blank for {0}) ", defaultPort), 0, false);

            if (!Int32.TryParse(Console.ReadLine(), out port))
                port = defaultPort;

            try
            {
                client = new TcpClient(host, port);
                client.NoDelay = true;
                stream = client.GetStream();
            }
            catch
            {
                throw new Exception(String.Format("Could not connect to {0}:{1}.", host, port));
            }
        }

        public void Read()
        {
            if (paused && !stream.DataAvailable)
            {
                Thread.Sleep(1);
                return;
            }

            bufferLength += stream.Read(buffer, bufferLength, bufferSize - bufferLength);

            while (bufferLength >= packetSize)
            {
                RecvPacket();
                Array.Copy(buffer, packetSize, buffer, 0, bufferLength - packetSize);
                bufferLength -= packetSize;
            }
        }

        private void RecvPacket()
        {
            byte b1 = buffer[0];
            byte b2 = buffer[1];
            byte b3 = buffer[2];
            byte b4 = buffer[3];
            int i1 = buffer[4] |
                (buffer[5] << 8) |
                (buffer[6] << 16) |
                (buffer[7] << 24);

            PrintPacket(buffer);

            switch (b1)
            {
                case Command.Version:
                    SendPacket(Command.Version, 1, 4, 0, 0);
                    SendPacket(Command.Status, Status.Running | Status.Reconnect, 0, 0, 0);
                    break;

                case Command.Sync1:
                    sync1Callback(b2, i1);
                    break;

                case Command.Sync3:
                    if (b2 == 0)
                        SendPacket(Command.Sync3, 0, 0, 0, i1);
                    break;

                case Command.Status:
                    paused = ((b2 & Status.Paused) == Status.Paused);
                    SendPacket(Command.Sync2, 0, 0x80, 0, 0);
                    break;
            }
        }

        private void SendPacket(byte b1, byte b2, byte b3, byte b4, int i1)
        {
            byte[] packet = new byte[packetSize]
            {
                b1,
                b2,
                b3,
                b4,
                (byte)i1,
                (byte)(i1 >> 8),
                (byte)(i1 >> 16),
                (byte)(i1 >> 24)
            };

            PrintPacket(packet, ">");
            stream.Write(packet, 0, packetSize);
        }

        private void PrintPacket(byte[] packet, string prefix = "")
        {
            byte b1 = packet[0];
            byte b2 = packet[1];
            byte b3 = packet[2];
            byte b4 = packet[3];
            int i1 = packet[4] |
                (packet[5] << 8) |
                (packet[6] << 16) |
                (packet[7] << 24);

            if (b1 != Command.Joypad && !(b1 == Command.Sync3 && b2 == 0))
            {
                Log(String.Format(prefix + "{0} {1:X2} {2:X2} {3:X2} {4}",
                    b1, b2, b3, b4, i1), 2);
            }
        }

        public void SendByte(byte b2)
        {
            SendPacket(Command.Sync2, b2, 0x80, 1, 0);
        }
    }
}
