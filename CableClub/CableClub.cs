using System;
using System.IO;

using BGB;
using IRC;

namespace CableClub
{
    class CableClub
    {
        private const string logPath = "console.log";
        private const int debugLevelDown = 0;

        private static class Serial
        {
            public const byte UsingInternalClock = 0x02;
            public const byte ConnectionNotEstablished = 0xFF;
            public const byte PreambleByte = 0xFD;
            public const byte NoDataByte = 0xFE;
        }

        private enum State
        {
            None,
            SyncBeforeLinkMenu,
            LinkMenu,
            SendDataBlockDelay,
            SendDataBlock,
            SyncBeforeDataBlock,
            DataBlock,
            DataBlockRng,
            DataBlockPlayer,
            DataBlockPatch,
            SendSelection,
            SyncSelection,
            SendConfirm,
            SyncConfirm,
            Traded,
            SyncTraded
        }

        private const int queueSize = 0x1000;
        private const int frameTime = 35112;
        private const int rngSize = 0x11;
        private const int playerSize = 0x1A8;
        private const int patchSize = 0xC8;
        private const int dataBlockSize = rngSize + playerSize + patchSize;

        private StreamWriter logWriter;
        private GameboyLink gbLink;
        private GameboyMemory gbMem;
        private InternetLink netLink;
        private int timestamp;
        private byte sb;
        private byte receiveDataInstant;
        private byte receiveData;
        private byte[] queue = new byte[queueSize];
        private int queueLength = 0;
        private State linkState;
        private int linkStateTime;
        private string syncPending = "";
        private string syncState = "";
        private bool canTimeout;
        private byte[] dataBlock = new byte[dataBlockSize];
        private byte nybblePending;
        private byte sentNybble;

        public CableClub()
        {
            Console.Title = "CableClub";

            try
            {
                logWriter = new StreamWriter(logPath);
            }
            catch (Exception e)
            {
                Log("Warning: " + e.Message);
                Log("");
            }

            try
            {
                Log("Welcome to the Cable Club!");
                Log("Version: b2");
                Log("");

                int port;

                Log("Linking Gameboy...");
                gbLink = new GameboyLink(Log, RecvByte, out port);

                Log("Hooking Gameboy...");
                gbMem = new GameboyMemory(port);

                Log("Connecting to server...");
                netLink = new InternetLink(Log);

                Run();
            }
            catch (Exception e)
            {
                Log("Error: " + e.Message);
            }

            Log("");
            Log("CableClub stopped. Press <enter> to close.");

            Console.ReadLine();
        }

        private void Run()
        {
            while (true)
            {
                gbLink.Read();
                netLink.Read();
            }
        }

        public void Log(string line, int debugLevel = 0, bool nl = true)
        {
            debugLevel -= debugLevelDown;

            if (debugLevel <= 0)
            {
                if (nl)
                    Console.WriteLine(line);
                else
                    Console.Write(line);
            }

            if (logWriter != null)
            {
                logWriter.WriteLine(line);
                logWriter.Flush();
            }
        }

        public void RecvByte(byte b2, int i1)
        {
            timestamp = i1;

            if (gbMem.SerialConnectionStatus == Serial.ConnectionNotEstablished)
            {
                EstablishConnection();
                SetLinkState(State.SyncBeforeLinkMenu, "beforelinkmenu");
                return;
            }

            gbLink.SendByte(sb);
            receiveDataInstant = b2;
            RecvData();
            receiveData = b2;
            sb = QueueRemove();
        }

        private void RecvData()
        {
            if (!QueueEmpty())
                return;

            RequestSync();

            byte nybble;

            switch (linkState)
            {
                case State.SyncBeforeLinkMenu:
                    if (SyncAndExchangeNybble())
                        SetLinkState(State.LinkMenu);
                    break;

                case State.LinkMenu:
                    if (DelayFrames(30))
                    {
                        QueueAdd(0xD4);
                        QueueAdd(0xD4);
                        QueueAdd(0);

                        SetLinkState(State.SendDataBlock);
                    }
                    break;

                case State.SendDataBlockDelay:
                    SetLinkState(State.SendDataBlock);
                    RecvData();
                    break;

                case State.SendDataBlock:
                    if (DelayFrames(60))
                    {
                        byte[] data = new byte[dataBlockSize];
                        byte[] rng = gbMem.SerialRandomNumberListBlock;
                        byte[] player = gbMem.SerialPlayerDataBlock;
                        byte[] patch = gbMem.SerialPartyMonsPatchList;

                        Array.Copy(rng, 0, data, 0, rngSize);
                        Array.Copy(player, 0, data, rngSize, playerSize);
                        Array.Copy(patch, 0, data, rngSize + playerSize, patchSize);

                        netLink.SendDataBlock(data);

                        SetLinkState(State.SyncBeforeDataBlock);
                        syncState = "db";
                        RecvData();
                    }
                    break;

                case State.SyncBeforeDataBlock:
                    if (SyncAndExchangeNybble(netLink.GetNybble()))
                        SetLinkState(State.DataBlock);
                    break;

                case State.DataBlock:
                    if (DelayFrames(22 * 2 + 1, true))
                    {
                        dataBlock = netLink.GetDataBlock();

                        SetLinkState(State.DataBlockRng);
                        RecvData();
                    }
                    break;

                case State.DataBlockRng:
                    QueueAdd(Serial.PreambleByte);

                    if (receiveDataInstant == Serial.PreambleByte)
                    {
                        for (int i = 0; i < rngSize; i++)
                            QueueAdd(dataBlock[i]);

                        SetLinkState(State.DataBlockPlayer);
                    }
                    break;

                case State.DataBlockPlayer:
                    QueueAdd(Serial.PreambleByte);

                    if (receiveDataInstant == Serial.PreambleByte)
                    {
                        for (int i = 0; i < playerSize; i++)
                            QueueAdd(dataBlock[rngSize + i]);

                        SetLinkState(State.DataBlockPatch);
                    }
                    break;

                case State.DataBlockPatch:
                    QueueAdd(Serial.PreambleByte);

                    if (receiveDataInstant == Serial.PreambleByte)
                    {
                        for (int i = 0; i < patchSize; i++)
                            QueueAdd(dataBlock[rngSize + playerSize + i]);

                        SetLinkState(State.SendSelection);
                    }
                    break;

                case State.SendSelection:
                    if (IsNybble(receiveDataInstant))
                    {
                        SetLinkState(State.SyncSelection, "selection", receiveDataInstant);
                        RecvData();
                    }
                    break;

                case State.SyncSelection:
                    nybble = netLink.GetNybble();

                    if (SyncAndExchangeNybble(nybble))
                    {
                        if (sentNybble == 0x6F && nybble == 0x6F)
                            SetLinkState(State.SendDataBlockDelay);
                        else if (sentNybble == 0x6F || nybble == 0x6F)
                            SetLinkState(State.SendSelection);
                        else
                            SetLinkState(State.SendConfirm);
                    }
                    break;

                case State.SendConfirm:
                    if (IsNybble(receiveDataInstant))
                    {
                        SetLinkState(State.SyncConfirm, "confirm", receiveDataInstant);
                        RecvData();
                    }
                    break;

                case State.SyncConfirm:
                    nybble = netLink.GetNybble();

                    if (SyncAndExchangeNybble(nybble))
                    {
                        if (sentNybble == 0x61 || nybble == 0x61)
                            SetLinkState(State.SendSelection);
                        else
                            SetLinkState(State.Traded);
                    }
                    break;

                case State.Traded:
                    if (DelayFrames(30))
                    {
                        SetLinkState(State.SyncTraded, "traded");
                        RecvData();
                    }
                    break;

                case State.SyncTraded:
                    if (SyncAndExchangeNybble(netLink.GetNybble()))
                        SetLinkState(State.SendDataBlockDelay);
                    break;
            }
        }

        private void QueueClear()
        {
            queueLength = 0;
        }

        private void QueueAdd(byte b2)
        {
            queue[queueLength++] = b2;
        }

        private byte QueueRemove()
        {
            if (queueLength == 0)
                return Serial.NoDataByte;

            byte b2 = queue[0];
            queueLength--;
            Array.Copy(queue, 1, queue, 0, queueLength);
            return b2;
        }

        private bool QueueEmpty()
        {
            return (queueLength == 0);
        }

        private void EstablishConnection()
        {
            sb = 0;
            receiveDataInstant = 0;
            receiveData = 0;

            QueueClear();

            gbLink.SendByte(Serial.UsingInternalClock);
            QueueAdd(0);
            QueueAdd(0);
        }

        private void SetLinkState(State state, string sync = "", byte nybble = 0)
        {
            linkState = state;
            linkStateTime = timestamp;
            syncPending = sync;
            canTimeout = false;
            nybblePending = nybble;
        }

        private void RequestSync()
        {
            if (syncPending == "")
                return;

            syncState = syncPending;
            sentNybble = nybblePending;

            netLink.SendSyncState(syncState, sentNybble);

            syncPending = "";
            nybblePending = 0;
        }

        private bool SyncAndExchangeNybble(byte nybble = 0x60)
        {
            if (syncState != "")
            {
                if (canTimeout && receiveDataInstant == 0)
                {
                    netLink.SendTimeout();
                    SetLinkState(State.None);
                    return false;
                }

                if (IsNybble(receiveData))
                    canTimeout = true;

                if (netLink.GetSyncState() != syncState)
                    return false;

                syncState = "";
            }

            if (IsNybble(receiveData))
            {
                QueueAdd(nybble);

                for (int i = 0; i < 10; i++)
                    QueueAdd(nybble);

                for (int i = 0; i < 10; i++)
                    QueueAdd(0);

                netLink.ClearSyncState();
                return true;
            }

            QueueAdd(nybble);
            return false;
        }

        private bool DelayFrames(int frames, bool halfFrames = false)
        {
            int time = frameTime;

            if (halfFrames)
                time /= 2;

            return (timestamp >= linkStateTime + frames * time || timestamp < linkStateTime);
        }

        private bool IsNybble(byte nybble)
        {
            return ((nybble & 0xF0) == 0x60);
        }
    }
}
