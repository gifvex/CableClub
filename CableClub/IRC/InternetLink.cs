using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace IRC
{
    class InternetLink
    {
        public delegate void LogCallback(string line, int debugLevel = 0, bool nl = true);

        private const string host = "irc.snoonet.org";
        private const int port = 6667;
        private const string ns = "NickServ";
        private const string nickNotice = "This nickname is registered";

        private class ParsedLine
        {
            public string Prefix { get; private set; }
            public string Nickname { get; private set; }
            public string Command { get; private set; }
            public string[] Parameters { get; private set; }

            public ParsedLine(string line)
            {
                if (line.StartsWith(":"))
                {
                    Prefix = line.Substring(1, line.IndexOf(' ') - 1);
                    line = line.Substring(line.IndexOf(' ') + 1);

                    if (Prefix.Contains('!'))
                        Nickname = Prefix.Remove(Prefix.IndexOf('!'));
                }

                if (line.Contains(' '))
                {
                    Command = line.Remove(line.IndexOf(' '));
                    line = line.Substring(line.IndexOf(' ') + 1);
                    List<string> parameters = new List<string>();

                    while (!String.IsNullOrEmpty(line))
                    {
                        if (line.StartsWith(":"))
                        {
                            parameters.Add(line.Substring(1));
                            break;
                        }

                        if (!line.Contains(' '))
                        {
                            parameters.Add(line);
                            break;
                        }

                        parameters.Add(line.Remove(line.IndexOf(' ')));
                        line = line.Substring(line.IndexOf(' ') + 1);
                    }

                    Parameters = parameters.ToArray();
                }
                else
                {
                    Command = line;
                    Parameters = new string[0];
                }
            }
        }

        private const int bufferSize = 0x1000;
        private const int codeMax = 0x10000;
        private const string nickPrefix = "cc";
        private const int codeListSize = 0x10;
        private const int dataBlockSize = 0x11 + 0x1A8 + 0xC8;

        private LogCallback Log;
        private Random rng;
        private TcpClient client;
        private NetworkStream stream;
        private byte[] buffer = new byte[bufferSize];
        private int bufferLength = 0;
        private bool connected;
        private int codeDebug;
        private bool forceChange = false;
        private int codeHere;
        private int codeThere = -1;
        private int[] codeList = new int[codeListSize];
        private int codeListLength = 0;
        private string syncState = "";
        private byte[] dataBlock = new byte[dataBlockSize];
        private string dataBlock1;
        private byte nybble = 0x60;

        public InternetLink(LogCallback callback, int code = -1)
        {
            Log = callback;
            rng = new Random();
            client = new TcpClient(host, port);
            stream = client.GetStream();
            codeDebug = code;

            Connect();
        }

        ~InternetLink()
        {
            if (client != null)
                client.Close();
        }

        private void Connect()
        {
            connected = false;

            SendLine("NICK cc");
            SendLine("USER cc 8 * :cc");

            while (!connected)
            {
                Read();

                if (CodeListCheck(codeThere))
                    connected = true;
                else
                    Thread.Sleep(1);
            }

            Log(String.Format("Linked to {0}.", codeThere));
        }

        public void Read()
        {
            if (!stream.DataAvailable)
                return;

            int i = bufferLength;
            bufferLength += stream.Read(buffer, bufferLength, bufferSize - bufferLength);

            while (i < bufferLength)
            {
                if (buffer[i] == 0x0A)
                {
                    RecvLine(Encoding.ASCII.GetString(buffer, 0, i));
                    i++;
                    Array.Copy(buffer, i, buffer, 0, bufferLength - i);
                    bufferLength -= i;
                    i = 0;
                }
                else
                {
                    i++;
                }
            }
        }

        private void RecvLine(string line)
        {
            line = line.Trim();

            if (String.IsNullOrEmpty(line))
                return;

            Log(line, 1);

            ParsedLine parsed = new ParsedLine(line);

            switch (parsed.Command)
            {
                case "PING":
                    SendLine("PONG :" + parsed.Parameters[0]);
                    break;

                case "NOTICE":
                    if (parsed.Nickname == ns && parsed.Parameters[1].StartsWith(nickNotice))
                        SendNick();
                    break;

                case "433":
                    SendNick();
                    break;

                case "NICK":
                    new Thread(() => EnterCode()).Start();
                    break;

                case "PRIVMSG":
                    if (!connected)
                        CodeListAdd(DecodeNick(parsed.Nickname));
                    else if (DecodeNick(parsed.Nickname) == codeThere)
                        RecvMessage(parsed.Parameters[1]);
                    break;
            }
        }

        private void RecvMessage(string line)
        {
            string command = line;

            if (line.Contains(' '))
            {
                command = line.Remove(line.IndexOf(' '));
                line = line.Substring(line.IndexOf(' ') + 1);
            }

            switch (command)
            {
                case "sync":
                    if (line.Contains(' '))
                    {
                        nybble = Convert.ToByte(line.Substring(line.IndexOf(' ') + 1), 16);
                        line = line.Remove(line.IndexOf(' '));
                    }

                    syncState = line;
                    break;

                case "db1":
                    dataBlock1 = line;
                    break;

                case "db2":
                    dataBlock = DecodeDataBlock(dataBlock1 + line);
                    syncState = "db";
                    break;

                case "dc":
                    syncState = "";
                    break;
            }
        }

        private void SendLine(string line)
        {
            Log(">" + line, 1);

            byte[] bytes = Encoding.ASCII.GetBytes(line + "\r\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        private void SendNick()
        {
            GenerateCode();
            SendLine("NICK " + EncodeNick(codeHere));
        }

        private void SendMessage(string line)
        {
            SendLine(String.Format("PRIVMSG {0} :{1}", EncodeNick(codeThere), line));
        }

        public void SendSyncState(string state, byte nybble = 0)
        {
            if (nybble == 0)
                SendMessage("sync " + state);
            else
                SendMessage(String.Format("sync {0} {1:X2}", state, nybble));
        }

        public void SendDataBlock(byte[] data)
        {
            string encoded = EncodeDataBlock(data);
            int length = encoded.Length / 2;

            SendMessage("db1 " + encoded.Substring(0, length));
            SendMessage("db2 " + encoded.Substring(length));
        }

        public void SendTimeout()
        {
            SendMessage("dc");
        }

        private void GenerateCode()
        {
            if (codeDebug != -1 && !forceChange)
            {
                codeHere = codeDebug;
                forceChange = true;
            }
            else
            {
                codeHere = rng.Next(codeMax);
            }
        }

        private string EncodeNick(int code)
        {
            return String.Format(nickPrefix + "{0}", code);
        }

        private int DecodeNick(string nick)
        {
            if (nick.Length < (nickPrefix.Length + 1))
                return -1;

            if (nick.Substring(0, nickPrefix.Length) != nickPrefix)
                return -1;

            int code;

            if (!Int32.TryParse(nick.Substring(nickPrefix.Length), out code))
                return -1;

            if (code < 0 || code >= codeMax)
                return -1;

            return code;
        }

        private void EnterCode()
        {
            int code = -1;

            Log("");
            Log(String.Format("Your code is {0}.", codeHere));
            Log("Enter the code of your partner: ", 0, false);

            while (code == -1)
            {
                string line = Console.ReadLine();

                if (Int32.TryParse(line, out code) && code >= 0 && code < codeMax)
                    break;

                Log("Invalid code. Try again: ", 0, false);

                code = -1;
            }

            codeThere = code;

            Log(String.Format("Waiting for {0}...", codeThere));
            SendMessage("hi");
        }

        private void CodeListAdd(int code)
        {
            if (code == -1)
                return;

            if (codeListLength == codeListSize)
            {
                Array.Copy(codeList, 1, codeList, 0, codeListSize - 1);
                codeListLength--;
            }

            codeList[codeListLength] = code;
            codeListLength++;
        }

        private bool CodeListCheck(int code)
        {
            if (code == -1)
                return false;

            for (int i = 0; i < codeListLength; i++)
            {
                if (codeList[i] == code)
                    return true;
            }

            return false;
        }

        public string GetSyncState()
        {
            return syncState;
        }

        public void ClearSyncState()
        {
            syncState = "";
        }

        private string EncodeDataBlock(byte[] data)
        {
            return Convert.ToBase64String(data);
        }

        private byte[] DecodeDataBlock(string encoded)
        {
            return Convert.FromBase64String(encoded);
        }

        public byte[] GetDataBlock()
        {
            return dataBlock;
        }

        public byte GetNybble()
        {
            return nybble;
        }
    }
}
