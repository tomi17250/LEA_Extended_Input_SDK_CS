using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Security.Cryptography;
using System.IO;

namespace LEASDKCS
{
    /// <summary>
    /// Contains basic communication logic with LEA Extended Input server
    /// </summary>
    class TCPLayerLite
    {
        public enum deviceType
        {
            NULL,
            CLIENT,
            GAME,
            SERVER,
            MANAGER,
        }

        /// <summary>
        /// Can be used in TCPLayerLite.enqueueDataToSend methods to allow data placed in buffer to be fused with this one (if needed) before being sent. See help for more informations
        /// </summary>
        public interface ISendQueue
        {
            /// <summary>
            /// Get bytes to be sent
            /// </summary>
            byte[] GetBytes();
            /// <summary>
            /// This method will be executed to try to fuse data. You have no control when it will be executed.
            /// </summary>
            /// <param name="with">The buffer to fuse against.</param>
            /// <returns>True if data have been successfully fused. False if the data must be added as is in the buffer (typically no data to fuse with inside the buffer).</returns>
            bool Fuse(List<ISendQueue> with);
        }

        public enum securityMode
        {
            NONE = 0,
            PASS_SHA1,
            AES_ENCRYPTION,
            AES_256KEY_ENCRYPTION,
        }

        static securityMode defaultSecurity;
        static byte[] defaultUserName;
        static byte[] defaultPassword;
        static byte[] defaultPassSha1;
        static bool defaultCheckCRC;

        static Guid instanceGuid = Guid.NewGuid();

        class CRC32
        {
            private int[] iTable;

            public CRC32()
            {
                this.iTable = new int[256];
                Init();
            }

            /// <summary>
            /// Initialize the iTable applying the polynomial used by PKZIP, WINZIP and Ethernet.
            /// </summary>
            private void Init()
            {
                // 0x04C11DB7 is the official polynomial used by PKZip, WinZip and Ethernet.
                int iPolynomial = 0x04C11DB7;

                // 256 values representing ASCII character codes.
                for (int iAscii = 0; iAscii <= 0xFF; iAscii++)
                {
                    this.iTable[iAscii] = this.Reflect(iAscii, (byte)8) << 24;

                    for (int i = 0; i <= 7; i++)
                    {
                        if ((this.iTable[iAscii] & 0x80000000L) == 0) this.iTable[iAscii] = (this.iTable[iAscii] << 1) ^ 0;
                        else this.iTable[iAscii] = (this.iTable[iAscii] << 1) ^ iPolynomial;
                    }
                    this.iTable[iAscii] = this.Reflect(this.iTable[iAscii], (byte)32);
                }
            }

            /// <summary>
            /// Reflection is a requirement for the official CRC-32 standard. Note that you can create CRC without it, but it won't conform to the standard.
            /// </summary>
            /// <param name="iReflect">value to apply the reflection</param>
            /// <param name="iValue">iValue</param>
            /// <returns>the calculated value</returns>
            private int Reflect(int iReflect, int iValue)
            {
                int iReturned = 0;
                // Swap bit 0 for bit 7, bit 1 For bit 6, etc....
                for (int i = 1; i < (iValue + 1); i++)
                {
                    if ((iReflect & 1) != 0)
                    {
                        iReturned |= (1 << (iValue - i));
                    }
                    iReflect >>= 1;
                }
                return iReturned;
            }

            /// <summary>
            /// PartialCRC calculates the CRC32 by looping through each byte in sData
            /// </summary>
            /// <param name="lCRC">the variable to hold the CRC. It must have been initialized. See fullCRC for an example</param>
            /// <param name="sData">array of byte to calculate the CRC</param>
            /// <param name="iDataLength">the length of the data</param>
            /// <returns>the new calculated CRC</returns>
            public long CalculateCRC(long lCRC, byte[] sData, int iDataLength)
            {
                for (int i = 0; i < iDataLength; i++)
                {
                    lCRC = (lCRC >> 8) ^ (long)(this.iTable[(int)(lCRC & 0xFF) ^ (int)(sData[i] & 0xff)] & 0xffffffffL);
                }
                return lCRC;
            }

            /// <summary>
            /// Calculates the CRC32 for the given Data
            /// </summary>
            /// <param name="sData">the data to calculate the CRC</param>
            /// <param name="iDataLength">the length of the data</param>
            /// <returns>the calculated CRC32</returns>
            public long FullCRC(byte[] sData, int iDataLength)
            {
                long lCRC = 0xffffffffL;
                lCRC = this.CalculateCRC(lCRC, sData, iDataLength);
                return (lCRC ^ 0xffffffffL);
            }

            /// <summary>
            /// Calculate CRC32 Of a stream
            /// </summary>
            /// <param name="dataStream">Stream from which calculate CRC32</param>
            /// <returns>the calculated CRC32</returns>
            public long StreamCRC(Stream dataStream)
            {
                long iOutCRC = 0xffffffffL; // Initialize the CRC.

                int iBytesRead = 0;
                int buffSize = 32 * 1024;
                byte[] data = new byte[buffSize];
                while ((iBytesRead = dataStream.Read(data, 0, buffSize)) > 0)
                {
                    iOutCRC = this.CalculateCRC(iOutCRC, data, iBytesRead);
                }
                return (iOutCRC ^ 0xffffffffL); // Finalize the CRC.
            }
        }

        class AESEncryptor
        {
            public bool key256 { get; private set; }

            RijndaelManaged AES;
            RandomNumberGenerator RNG;

            public AESEncryptor(securityMode mode, byte[] password)
            {
                AES = new RijndaelManaged();
                AES.BlockSize = 128;
                AES.Padding = PaddingMode.Zeros;
                AES.Mode = CipherMode.CBC;
                if (mode == securityMode.AES_ENCRYPTION)
                {
                    AES.KeySize = 128;
                    key256 = false;
                }
                else if (mode == securityMode.AES_256KEY_ENCRYPTION)
                {
                    AES.KeySize = 256;
                    key256 = true;
                }
                AES.Key = password;
                RNG = new RNGCryptoServiceProvider();
            }

            public byte[] encrypt(byte[] data)
            {
                byte[] encyptedData;
                byte[] IV = new byte[16];
                RNG.GetBytes(IV);
                using (ICryptoTransform encryptor = AES.CreateEncryptor(AES.Key, IV))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        {
                            cs.Write(data, 0, data.Length);
                            cs.FlushFinalBlock();
                            encyptedData = ms.ToArray();
                        }
                    }
                }
                byte[] retData = new byte[encyptedData.Length + 20];
                encyptedData.CopyTo(retData, 0);
                IV.CopyTo(retData, encyptedData.Length);
                byte[] size = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
                size.CopyTo(retData, retData.Length - 4);
                return retData;
            }

            public byte[] decrypt(byte[] data)
            {
                byte[] IV = new byte[16];
                byte[] size = new byte[4];
                int posInIV = 0;
                for (int i = data.Length - 20; posInIV < IV.Length; i++)
                {
                    IV[posInIV] = data[i];
                    posInIV++;
                }
                int posInSize = 0;
                for (int i = data.Length - 4; posInSize < size.Length; i++)
                {
                    size[posInSize] = data[i];
                    posInSize++;
                }
                int dataSize = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(size, 0));
                if (dataSize < data.Length - 16 - 20 || dataSize > data.Length)
                {
                    throw new Exception("Bad length");
                }
                byte[] retData = new byte[dataSize];
                using (ICryptoTransform decryptor = AES.CreateDecryptor(AES.Key, IV))
                {
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                        {
                            cs.Read(retData, 0, retData.Length);
                        }
                    }
                }
                return retData;
            }

        }

        /// <summary>
        /// Contains the remote device data, and a point to communicate with.
        /// </summary>
        public class device : IDisposable
        {
            /// <summary>
            /// The machine name of the remote device
            /// </summary>
            public string remoteName { get; private set; }
            /// <summary>
            /// The Socket allowing communication with remote device
            /// </summary>
            public Socket sock { get; private set; }
            /// <summary>
            /// The type of remote device
            /// </summary>
            public deviceType type { get; private set; }
            /// <summary>
            /// Remote end point (IP address and port)
            /// </summary>
            public IPEndPoint remoteIPEP { get; private set; }
            /// <summary>
            /// Maximum size of messages
            /// </summary>
            public int maxDataLength { get; set; }
            /// <summary>
            /// The device has been disposed
            /// </summary>
            public bool isDisposed { get; private set; }
            /// <summary>
            /// The socket is sending data
            /// </summary>
            public bool sending { get; private set; }
            /// <summary>
            /// Amount of data to send on current request
            /// </summary>
            public int bytesToSend { get; private set; }
            /// <summary>
            /// Amount of data sent on current request
            /// </summary>
            public int bytesSent { get; private set; }
            /// <summary>
            /// The current device is receiving
            /// </summary>
            public bool receiving { get; private set; }
            /// <summary>
            /// The currently receiving message length
            /// </summary>
            public int bytesToReceive { get; private set; }
            /// <summary>
            /// The amount of data received on current message
            /// </summary>
            public int bytesReceived { get; private set; }
            /// <summary>
            /// The remote device accepted the password
            /// </summary>
            public bool passwordChecked { get; private set; }
            /// <summary>
            /// Remote instance GUID
            /// </summary>
            public Guid? remoteGuid { get; private set; }

            Queue<byte[]> dataToSend;
            List<ISendQueue> advDataToSend;
            AutoResetEvent sendARE;
            Timer pingTimer;
            Timer passwordCheckTimer;
            bool sendPing;
            object sendingLock;
            bool sendPingAnswer;
            bool started;
            bool negociate;
            int latency;
            DateTime lastSend;
            int requestedLatency;
            bool requestSendUserName;
            bool requestSendGuid;
            bool requestChangeLatency;
            bool requestRemoteName;
            bool requestCheckPassword;
            bool sendShutdown;

            securityMode security;
            public byte[] userName { get; private set; }
            byte[] password;
            byte[] passSha1;
            bool checkCRC;

            CRC32 crc = new CRC32();
            AESEncryptor AES;

            enum protocol
            {
                TYPE,
                SHUTDOWN,
                CHANGE_DATA_RATE,
                CHECK_PASSWORD,
            }

            public device()
            {
                remoteName = "No name";
                dataToSend = new Queue<byte[]>();
                advDataToSend = new List<ISendQueue>();
                sendARE = new AutoResetEvent(false);
                maxDataLength = 15728640;
                pingTimer = new Timer(timerCallback, null, 0, 30000);
                passwordCheckTimer = new Timer(passCheckTimeout, null, 5000, 0);
                sendingLock = new object();
                latency = 0;
                lastSend = DateTime.Now;
            }

            void timerCallback(object unused)
            {
                if (!started)
                {
                    return;
                }
                if (sending || receiving)
                {
                    sendPing = false;
                    return;
                }
                bool disposeNeeded = false;
                lock (sendingLock)
                {
                    if (sendPing)
                    {
                        disposeNeeded = true;
                    }
                    else
                    {
                        sendPing = true;
                        sendARE.Set();
                    }
                }
                if (disposeNeeded)
                {
                    Dispose();
                }
            }

            void passCheckTimeout(object unused)
            {
                passwordCheckTimer.Dispose();
                passwordCheckTimer = null;
                if (!passwordChecked)
                {
                    Dispose();
                }
            }

            public bool startWithSocket(Socket sockArg, deviceType typeArg)
            {
                if (!sockArg.Connected)
                {
                    return false;
                }
                sock = sockArg;
                sock.Blocking = false;
                sock.NoDelay = true;
                remoteIPEP = (IPEndPoint)sock.RemoteEndPoint;
                type = typeArg;
                security = defaultSecurity;
                userName = defaultUserName;
                password = defaultPassword;
                passSha1 = defaultPassSha1;
                checkCRC = defaultCheckCRC;
                registerConnectedDevice(this);
                launchThreads();
                return true;
            }

            public bool startAndConnectTo(IPEndPoint IPEPArg, deviceType typeArg)
            {
                try
                {
                    sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    sock.Blocking = true;
                    sock.Connect(IPEPArg);
                    sock.Blocking = false;
                    sock.NoDelay = true;
                    remoteIPEP = (IPEndPoint)sock.RemoteEndPoint;
                    type = typeArg;
                    security = defaultSecurity;
                    userName = defaultUserName;
                    password = defaultPassword;
                    passSha1 = defaultPassSha1;
                    checkCRC = defaultCheckCRC;
                    registerConnectedDevice(this);
                }
                catch
                {
                    return false;
                }
                launchThreads();
                return true;
            }

            public bool startWithSocket(Socket sockArg, deviceType typeArg, securityMode SM, byte[] user, byte[] pass, bool CRC)
            {
                if (!sockArg.Connected)
                {
                    return false;
                }
                sock = sockArg;
                sock.Blocking = false;
                sock.NoDelay = true;
                remoteIPEP = (IPEndPoint)sock.RemoteEndPoint;
                type = typeArg;
                security = SM;
                userName = user;
                password = pass;
                using (SHA1Managed sha = new SHA1Managed())
                {
                    passSha1 = sha.ComputeHash(pass);
                }
                checkCRC = CRC;
                registerConnectedDevice(this);
                launchThreads();
                return true;
            }

            public bool startAndConnectTo(IPEndPoint IPEPArg, deviceType typeArg, securityMode SM, byte[] user, byte[] pass, bool CRC)
            {
                try
                {
                    sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    sock.Blocking = true;
                    sock.Connect(IPEPArg);
                    sock.Blocking = false;
                    sock.NoDelay = true;
                    remoteIPEP = (IPEndPoint)sock.RemoteEndPoint;
                    type = typeArg;
                    security = SM;
                    userName = user;
                    password = pass;
                    using (SHA1Managed sha = new SHA1Managed())
                    {
                        passSha1 = sha.ComputeHash(pass);
                    }
                    checkCRC = CRC;
                    registerConnectedDevice(this);
                }
                catch
                {
                    return false;
                }
                launchThreads();
                return true;
            }

            void launchThreads()
            {
                Thread thrd = new Thread(receiveThread);
                thrd.IsBackground = true;
                thrd.Start();

                thrd = new Thread(sendThread);
                thrd.IsBackground = true;
                thrd.Start();
            }

            void decodeProtocol(byte[] data)
            {
                if (data.Length != 8) return;
                try
                {
                    protocol prot = (protocol)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 0));
                    if (prot == protocol.TYPE)
                    {
                        type = (deviceType)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 4));
                    }
                    else if (prot == protocol.SHUTDOWN)
                    {
                        Dispose();
                    }
                    else if (prot == protocol.CHANGE_DATA_RATE)
                    {
                        latency = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 4));
                    }
                    else if (prot == protocol.CHECK_PASSWORD)
                    {
                        requestCheckPassword = true;
                        sendARE.Set();
                    }
                }
                catch { }
            }

            void decodeRemoteName(byte[] data)
            {
                int zeroPos = -1;
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == 0x00)
                    {
                        zeroPos = i;
                        break;
                    }
                }
                if (zeroPos == -0)
                {
                    remoteName = Encoding.UTF8.GetString(data);
                }
                else
                {
                    remoteName = Encoding.UTF8.GetString(data, 0, zeroPos);
                }
            }

            void decodeUserName(byte[] data)
            {
                int zeroPos = -1;
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == 0x00)
                    {
                        zeroPos = i;
                        break;
                    }
                }
                if (zeroPos == 0)
                {
                    userName = data;
                }
                else
                {
                    userName = new byte[zeroPos];
                    for (int i = 0; i < zeroPos; i++)
                    {
                        userName[i] = data[i];
                    }
                }
            }

            void decodeRemoteGuid(byte[] data)
            {
                try
                {
                    remoteGuid = new Guid(data);
                }
                catch
                {
                    Dispose();
                }
            }

            void checkPassword(byte[] data)
            {
                if (security == securityMode.NONE)
                {
                    if (data.Length == 4 && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 0)
                    {
                        passwordChecked = true;
                    }
                    else
                    {
                        sendShutdown = true;
                        sendARE.Set();
                        return;
                    }
                }
                else
                {
                    if (userName == null)
                    {
                        byte[] nullData = new byte[4];
                        sendProtocolData(protocol.CHECK_PASSWORD, nullData);
                    }
                    else if (security == securityMode.PASS_SHA1)
                    {
                        if (data.Length == 20)
                        {
                            for (int i = 0; i < passSha1.Length; i++)
                            {
                                if (data[i] != passSha1[i])
                                {
                                    sendShutdown = true;
                                    sendARE.Set();
                                    return;
                                }
                            }
                            passwordChecked = true;
                        }
                        else
                        {
                            sendShutdown = true;
                            sendARE.Set();
                            return;
                        }
                    }
                    else if (security == securityMode.AES_ENCRYPTION)
                    {
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_ENCRYPTION, password);
                        }
                        data = AES.decrypt(data);
                        //CRC
                        byte[] retData = new byte[data.Length - 8];
                        for (int i = 0; i < data.Length - 8; i++)
                        {
                            retData[i] = data[i];
                        }
                        byte[] CRC = new byte[8];
                        int posInCRC = 0;
                        for (int i = data.Length - 8; posInCRC < CRC.Length; i++)
                        {
                            CRC[posInCRC] = data[i];
                            posInCRC++;
                        }
                        long curCRC = crc.FullCRC(retData, retData.Length);
                        long retrivedCRC = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(CRC, 0));
                        if (curCRC == retrivedCRC)
                        {
                            passwordChecked = true;
                        }
                        else
                        {
                            sendShutdown = true;
                            sendARE.Set();
                            return;
                        }
                    }
                    else if (security == securityMode.AES_256KEY_ENCRYPTION)
                    {
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_256KEY_ENCRYPTION, password);
                        }
                        data = AES.decrypt(data);
                        //CRC
                        byte[] retData = new byte[data.Length - 8];
                        for (int i = 0; i < data.Length - 8; i++)
                        {
                            retData[i] = data[i];
                        }
                        byte[] CRC = new byte[8];
                        int posInCRC = 0;
                        for (int i = data.Length - 8; posInCRC < CRC.Length; i++)
                        {
                            CRC[posInCRC] = data[i];
                            posInCRC++;
                        }
                        long curCRC = crc.FullCRC(retData, retData.Length);
                        long retrivedCRC = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(CRC, 0));
                        if (curCRC == retrivedCRC)
                        {
                            passwordChecked = true;
                        }
                        else
                        {
                            sendShutdown = true;
                            sendARE.Set();
                            return;
                        }
                    }
                    else throw new NotSupportedException("This password method is not supported.");
                }
            }

            int receiveUntilDone(byte[] data, int offset, int size)
            {
                bytesToReceive = size;
                int receivedBytes = offset;
                while (receivedBytes < size)
                {
                    SocketError err;
                    receivedBytes += sock.Receive(data, receivedBytes, size - receivedBytes, SocketFlags.None, out err);
                    bytesReceived = receivedBytes;
                    if (err == SocketError.WouldBlock || err == SocketError.IOPending || err == SocketError.NoBufferSpaceAvailable)
                    {
                        Thread.Sleep(5);
                    }
                    else if (err != SocketError.Success)
                    {
                        throw new SocketException((int)err);
                    }
                }
                bytesReceived = 0;
                bytesToReceive = 0;
                return receivedBytes;
            }

            byte[] postProcessData(byte[] data)
            {
                if (security == securityMode.NONE)
                {
                    if (checkCRC)
                    {
                        byte[] retData = new byte[data.Length - 8];
                        for (int i = 0; i < data.Length - 8; i++)
                        {
                            retData[i] = data[i];
                        }
                        //CRC
                        byte[] CRC = new byte[8];
                        int posInCRC = 0;
                        for (int i = data.Length - 8; posInCRC < CRC.Length; i++)
                        {
                            CRC[posInCRC] = data[i];
                            posInCRC++;
                        }
                        long curCRC = crc.FullCRC(retData, retData.Length);
                        long retrivedCRC = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(CRC, 0));
                        if (curCRC == retrivedCRC)
                        {
                            return retData;
                        }
                        else return null;
                    }
                    else
                    {
                        return data;
                    }
                }
                else if (security == securityMode.PASS_SHA1)
                {
                    if (checkCRC)
                    {
                        byte[] retData = new byte[data.Length - 28];
                        for (int i = 0; i < data.Length - 28; i++)
                        {
                            retData[i] = data[i];
                        }
                        //SHA1 check
                        byte[] SHA = new byte[20];
                        int posInSHA = 0;
                        for (int i = data.Length - 20; posInSHA < SHA.Length; i++)
                        {
                            SHA[posInSHA] = data[i];
                            posInSHA++;
                        }
                        for (int i = 0; i < passSha1.Length; i++)
                        {
                            if (passSha1[i] != SHA[i])
                            {
                                return null;
                            }
                        }
                        //CRC
                        byte[] CRC = new byte[8];
                        int posInCRC = 0;
                        for (int i = data.Length - 28; posInCRC < CRC.Length; i++)
                        {
                            CRC[posInCRC] = data[i];
                            posInCRC++;
                        }
                        long curCRC = crc.FullCRC(retData, retData.Length);
                        long retrivedCRC = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(CRC, 0));
                        if (curCRC == retrivedCRC)
                        {
                            return retData;
                        }
                        else return null;
                    }
                    else
                    {
                        byte[] retData = new byte[data.Length - 20];
                        for (int i = 0; i < data.Length - 20; i++)
                        {
                            retData[i] = data[i];
                        }
                        //SHA1 check
                        byte[] SHA = new byte[20];
                        int posInSHA = 0;
                        for (int i = data.Length - 20; posInSHA < SHA.Length; i++)
                        {
                            SHA[posInSHA] = data[i];
                            posInSHA++;
                        }
                        for (int i = 0; i < passSha1.Length; i++)
                        {
                            if (passSha1[i] != SHA[i])
                            {
                                return null;
                            }
                        }
                        return retData;
                    }
                }
                else if (security == securityMode.AES_ENCRYPTION)
                {
                    if (checkCRC)
                    {
                        //AES decrypt
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_ENCRYPTION, password);
                        }
                        data = AES.decrypt(data);
                        //CRC
                        byte[] retData = new byte[data.Length - 8];
                        for (int i = 0; i < data.Length - 8; i++)
                        {
                            retData[i] = data[i];
                        }
                        byte[] CRC = new byte[8];
                        int posInCRC = 0;
                        for (int i = data.Length - 8; posInCRC < CRC.Length; i++)
                        {
                            CRC[posInCRC] = data[i];
                            posInCRC++;
                        }
                        long curCRC = crc.FullCRC(retData, retData.Length);
                        long retrivedCRC = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(CRC, 0));
                        if (curCRC == retrivedCRC)
                        {
                            return retData;
                        }
                        else return null;
                    }
                    else
                    {
                        //AES decrypt
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_ENCRYPTION, password);
                        }
                        data = AES.decrypt(data);
                        return data;
                    }
                }
                else if (security == securityMode.AES_256KEY_ENCRYPTION)
                {
                    if (checkCRC)
                    {
                        //AES decrypt
                        if (AES == null || !AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_256KEY_ENCRYPTION, password);
                        }
                        data = AES.decrypt(data);
                        //CRC
                        byte[] retData = new byte[data.Length - 8];
                        for (int i = 0; i < data.Length - 8; i++)
                        {
                            retData[i] = data[i];
                        }
                        byte[] CRC = new byte[8];
                        int posInCRC = 0;
                        for (int i = data.Length - 8; posInCRC < CRC.Length; i++)
                        {
                            CRC[posInCRC] = data[i];
                            posInCRC++;
                        }
                        long curCRC = crc.FullCRC(retData, retData.Length);
                        long retrivedCRC = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(CRC, 0));
                        if (curCRC == retrivedCRC)
                        {
                            return retData;
                        }
                        else return null;
                    }
                    else
                    {
                        //AES decrypt
                        if (AES == null || !AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_256KEY_ENCRYPTION, password);
                        }
                        data = AES.decrypt(data);
                        return data;
                    }
                }
                else throw new NotSupportedException("Security mode not supported.");
            }

            void receiveThread()
            {
                byte[] length = new byte[4];
                int dataLength = -7;
                int receivedBytes = 0;
                byte[] data = null;
                byte[] protocolData = new byte[8];
                byte[] nameData = new byte[256];
                byte[] userNameData = new byte[256];
                byte[] guidData = new byte[16];
                bool eventConnectedEventTriggered = false;
                try
                {
                    while (true)
                    {
                        if (isDisposed)
                        {
                            return;
                        }
                        if (dataLength == -7)
                        {
                            receivedBytes = receiveUntilDone(length, 0, length.Length);
                            receiving = true;
                            try
                            {
                                dataLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(length, 0));
                            }
                            catch
                            {
                                Dispose();
                                return;
                            }
                            if (dataLength == -6)
                            {
                                receivedBytes = receiveUntilDone(guidData, 0, guidData.Length);
                                decodeRemoteGuid(guidData);
                                receivedBytes = 0;
                                dataLength = -7;
                                data = null;
                                receiving = false;
                            }
                            else if (dataLength == -5)
                            {
                                receivedBytes = receiveUntilDone(userNameData, 0, userNameData.Length);
                                decodeUserName(userNameData);
                                receivedBytes = 0;
                                dataLength = -7;
                                data = null;
                                receiving = false;
                            }
                            else if (dataLength == -4)
                            {
                                receivedBytes = receiveUntilDone(length, 0, length.Length);
                                try
                                {
                                    dataLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(length, 0));
                                }
                                catch
                                {
                                    Dispose();
                                    return;
                                }
                                data = new byte[dataLength];
                                receivedBytes = receiveUntilDone(data, 0, data.Length);
                                checkPassword(data);
                                receivedBytes = 0;
                                dataLength = -7;
                                data = null;
                                receiving = false;
                            }
                            else if (dataLength == -3)
                            {
                                receivedBytes = receiveUntilDone(nameData, 0, nameData.Length);
                                decodeRemoteName(nameData);
                                receivedBytes = 0;
                                dataLength = -7;
                                data = null;
                                receiving = false;
                            }
                            else if (dataLength == -2)
                            {
                                receivedBytes = receiveUntilDone(protocolData, 0, protocolData.Length);
                                decodeProtocol(protocolData);
                                receivedBytes = 0;
                                dataLength = -7;
                                data = null;
                                receiving = false;
                            }
                            else if (dataLength == -1)
                            {
                                sendPing = false;
                                receivedBytes = 0;
                                dataLength = -7;
                                data = null;
                                receiving = false;
                            }
                            else if (dataLength == 0)
                            {
                                sendPingAnswer = true;
                                sendARE.Set();
                                receivedBytes = 0;
                                dataLength = -7;
                                data = null;
                                receiving = false;
                            }
                            else if (dataLength < -1 || dataLength > maxDataLength)
                            {
                                Dispose();
                                return;
                            }
                            if (!isDisposed && !eventConnectedEventTriggered && passwordChecked && remoteGuid != null)
                            {
                                eventConnectedEventTriggered = true;
                                ThreadPool.QueueUserWorkItem((obj) =>
                                {
                                    List<device> devList;
                                    lock (connectedDevices)
                                    {
                                        devList = new List<device>(connectedDevices);
                                    }
                                    if (DirectConnectionEstablished != null)
                                    {
                                        DirectConnectionEstablished.Invoke(new List<device>(devList));
                                    }
                                });
                            }
                        }
                        else if (passwordChecked && remoteGuid != null)
                        {
                            data = new byte[dataLength];
                            receivedBytes = receiveUntilDone(data, 0, dataLength);
                            data = postProcessData(data);
                            if (data == null)
                            {
                                Dispose();
                                return;
                            }
                            enqueueReceivedData(data, this);
                            receivedBytes = 0;
                            dataLength = -7;
                            data = null;
                            receiving = false;
                        }
                        else // Attempt to send data without negotiation ended -> protocol error = close connection
                        {
                            Dispose();
                            return;
                        }
                    }
                }
                catch { }
                finally
                {
                    Dispose();
                }
            }

            void sendUntilDone(byte[] data)
            {
                int sentBytes = 0;
                bytesSent = 0;
                bytesToSend = data.Length;
                while (sentBytes != data.Length)
                {
                    SocketError err;
                    int sizeToSend = data.Length - sentBytes;
                    if (sock.SendBufferSize < sizeToSend)
                    {
                        sizeToSend = sock.SendBufferSize;
                    }
                    sentBytes += sock.Send(data, sentBytes, sizeToSend, SocketFlags.None, out err);
                    bytesSent = sentBytes;
                    if (err == SocketError.WouldBlock || err == SocketError.IOPending || err == SocketError.NoBufferSpaceAvailable)
                    {
                        Thread.Sleep(0);
                    }
                    else if (err != SocketError.Success)
                    {
                        throw new SocketException((int)err);
                    }
                }

            }

            void sendProtocolData(protocol prot, byte[] data)
            {
                int minus2 = -2;
                byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(minus2));
                sendUntilDone(length);
                byte[] protData = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)prot));
                sendUntilDone(protData);
                sendUntilDone(data);
            }

            void sendName()
            {
                byte[] nameBytes;
                try
                {
                    nameBytes = Encoding.UTF8.GetBytes(Environment.MachineName);
                }
                catch
                {
                    nameBytes = Encoding.UTF8.GetBytes("Unknown");
                }
                int bytesToCopy = nameBytes.Length;
                if (bytesToCopy > 256) bytesToCopy = 256;
                byte[] nameToSend = new byte[256];
                for (int i = 0; i < bytesToCopy; i++)
                {
                    nameToSend[i] = nameBytes[i];
                }
                nameToSend[bytesToCopy] = 0x00;
                byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(-3));
                sendUntilDone(length);
                sendUntilDone(nameToSend);
            }

            void sendUserName()
            {
                if (userName == null)
                {
                    return;
                }
                int bytesToCopy = userName.Length;
                if (bytesToCopy > 255) bytesToCopy = 255;
                byte[] nameToSend = new byte[256];
                for (int i = 0; i < bytesToCopy; i++)
                {
                    nameToSend[i] = userName[i];
                }
                nameToSend[bytesToCopy] = 0x00;
                byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(-5));
                sendUntilDone(length);
                sendUntilDone(nameToSend);
            }

            void sendGuid()
            {
                byte[] data = instanceGuid.ToByteArray();
                byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(-6));
                sendUntilDone(length);
                sendUntilDone(data);
            }

            bool sendPasswordCheck()
            {
                byte[] passwordCheckData;
                if (security == securityMode.NONE)
                {
                    passwordCheckData = new byte[4];
                    return false;
                }
                else
                {
                    if (userName == null || password == null)
                    {
                        return true;
                    }

                    if (security == securityMode.PASS_SHA1)
                    {
                        passwordCheckData = new byte[20];
                        for (int i = 0; i < passSha1.Length; i++)
                        {
                            passwordCheckData[i] = passSha1[i];
                        }
                    }
                    else if (security == securityMode.AES_ENCRYPTION)
                    {
                        byte[] data = new byte[256];
                        Random rand = new Random((int)DateTime.Now.Ticks);
                        rand.NextBytes(data);
                        long CRC = crc.FullCRC(data, data.Length);
                        byte[] CRCBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(CRC));
                        byte[] dataToUse = new byte[data.Length + 8];
                        data.CopyTo(dataToUse, 0);
                        CRCBytes.CopyTo(dataToUse, data.Length);
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_ENCRYPTION, password);
                        }
                        passwordCheckData = AES.encrypt(dataToUse);
                    }
                    else if (security == securityMode.AES_256KEY_ENCRYPTION)
                    {
                        byte[] data = new byte[256];
                        Random rand = new Random((int)DateTime.Now.Ticks);
                        rand.NextBytes(data);
                        long CRC = crc.FullCRC(data, data.Length);
                        byte[] CRCBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(CRC));
                        byte[] dataToUse = new byte[data.Length + 8];
                        data.CopyTo(dataToUse, 0);
                        CRCBytes.CopyTo(dataToUse, data.Length);
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_256KEY_ENCRYPTION, password);
                        }
                        passwordCheckData = AES.encrypt(dataToUse);
                    }
                    else throw new NotSupportedException("This password method is not supported.");
                }

                byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(-4));
                sendUntilDone(length);
                byte[] realLength = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(passwordCheckData.Length));
                sendUntilDone(realLength);
                sendUntilDone(passwordCheckData);

                return false;
            }

            void preprocessAndSendData(byte[] data)
            {
                if (security == securityMode.NONE)
                {
                    if (checkCRC)
                    {
                        long CRC = crc.FullCRC(data, data.Length);
                        byte[] CRCBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(CRC));
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + CRCBytes.Length));
                        sendUntilDone(length);
                        sendUntilDone(data);
                        sendUntilDone(CRCBytes);
                    }
                    else
                    {
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
                        sendUntilDone(length);
                        sendUntilDone(data);
                    }
                }
                else if (security == securityMode.PASS_SHA1)
                {
                    if (checkCRC)
                    {
                        long CRC = crc.FullCRC(data, data.Length);
                        byte[] CRCBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(CRC));
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + CRCBytes.Length + passSha1.Length));
                        sendUntilDone(length);
                        sendUntilDone(data);
                        sendUntilDone(CRCBytes);
                        sendUntilDone(passSha1);
                    }
                    else
                    {
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length + passSha1.Length));
                        sendUntilDone(length);
                        sendUntilDone(data);
                        sendUntilDone(passSha1);
                    }
                }
                else if (security == securityMode.AES_ENCRYPTION)
                {
                    if (checkCRC)
                    {
                        long CRC = crc.FullCRC(data, data.Length);
                        byte[] CRCBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(CRC));
                        byte[] dataToUse = new byte[data.Length + 8];
                        data.CopyTo(dataToUse, 0);
                        CRCBytes.CopyTo(dataToUse, data.Length);
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_ENCRYPTION, password);
                        }
                        dataToUse = AES.encrypt(dataToUse);
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(dataToUse.Length));
                        sendUntilDone(length);
                        sendUntilDone(dataToUse);
                    }
                    else
                    {
                        if (AES == null || AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_ENCRYPTION, password);
                        }
                        data = AES.encrypt(data);
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
                        sendUntilDone(length);
                        sendUntilDone(data);
                    }
                }
                else if (security == securityMode.AES_256KEY_ENCRYPTION)
                {
                    if (checkCRC)
                    {
                        long CRC = crc.FullCRC(data, data.Length);
                        byte[] CRCBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(CRC));
                        byte[] dataToUse = new byte[data.Length + 8];
                        data.CopyTo(dataToUse, 0);
                        CRCBytes.CopyTo(dataToUse, data.Length);
                        if (AES == null || !AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_256KEY_ENCRYPTION, password);
                        }
                        dataToUse = AES.encrypt(dataToUse);
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(dataToUse.Length));
                        sendUntilDone(length);
                        sendUntilDone(dataToUse);
                    }
                    else
                    {
                        if (AES == null || !AES.key256)
                        {
                            AES = new AESEncryptor(securityMode.AES_256KEY_ENCRYPTION, password);
                        }
                        data = AES.encrypt(data);
                        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
                        sendUntilDone(length);
                        sendUntilDone(data);
                    }
                }
                lastSend = DateTime.Now;
            }

            void sendThread()
            {
                started = true;
                negociate = true;
                if (userName != null)
                {
                    requestSendUserName = true;
                }
                requestRemoteName = true;
                requestCheckPassword = true;
                requestSendGuid = true;
                bool latencyWait = false;
                try
                {
                    while (true)
                    {
                        if (isDisposed)
                        {
                            return;
                        }
                        byte[] data = null;
                        ISendQueue ISQ = null;
                        lock (sendingLock)
                        {
                            sending = true;
                            if (negociate)
                            {
                                byte[] typeData = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)localDeviceType));
                                sendProtocolData(protocol.TYPE, typeData);
                                negociate = false;
                            }
                            if (sendPing)
                            {
                                int zeroLength = 0;
                                byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(zeroLength));
                                sendUntilDone(length);
                                sendPing = false;
                            }
                            if (sendPingAnswer)
                            {
                                int minus1 = -1;
                                byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(minus1));
                                sendUntilDone(length);
                                sendPingAnswer = false;
                            }
                            if (sendShutdown)
                            {
                                byte[] shutDownData = new byte[4];
                                sendProtocolData(protocol.SHUTDOWN, shutDownData);
                                sendShutdown = false;
                                Dispose();
                                return;
                            }
                            if (requestSendUserName)
                            {
                                sendUserName();
                                requestSendUserName = false;
                            }
                            if (requestRemoteName)
                            {
                                sendName();
                                requestRemoteName = false;
                            }
                            if (requestCheckPassword)
                            {
                                requestCheckPassword = sendPasswordCheck();
                            }
                            if (requestSendGuid)
                            {
                                sendGuid();
                                requestSendGuid = false;
                            }
                            if (requestChangeLatency)
                            {
                                byte[] latencyData = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(requestedLatency));
                                sendProtocolData(protocol.CHANGE_DATA_RATE, latencyData);
                                requestChangeLatency = false;
                            }
                            if (latency != -1)
                            {
                                if (latency == 0 || lastSend + TimeSpan.FromMilliseconds(latency) < DateTime.Now)
                                {
                                    lock (dataToSend)
                                    {
                                        if (dataToSend.Count > 0)
                                        {
                                            data = dataToSend.Dequeue();
                                        }
                                        else
                                        {
                                            data = null;
                                        }
                                    }
                                    if (data != null)
                                    {
                                        preprocessAndSendData(data);
                                        lastSend = DateTime.Now;
                                    }
                                    lock (advDataToSend)
                                    {
                                        if (advDataToSend.Count > 0)
                                        {
                                            ISQ = advDataToSend[0];
                                            advDataToSend.RemoveAt(0);
                                        }
                                        else
                                        {
                                            ISQ = null;
                                        }
                                    }
                                    if (ISQ != null)
                                    {
                                        data = ISQ.GetBytes();
                                        preprocessAndSendData(data);
                                        lastSend = DateTime.Now;
                                    }
                                }
                                else
                                {
                                    latencyWait = true;
                                }
                            }
                            else
                            {
                                latencyWait = true;
                            }
                            sending = false;
                        }
                        if (data == null && ISQ == null && !negociate && !sendPing && !sendPingAnswer && !sendShutdown && !requestSendUserName && !requestRemoteName && !requestCheckPassword && !requestSendGuid && !requestChangeLatency)
                        {
                            if (latencyWait)
                            {
                                if (latency == -1)
                                {
                                    sendARE.WaitOne(5000);
                                }
                                else
                                {
                                    sendARE.WaitOne(latency);
                                }
                            }
                            else
                            {
                                sendARE.WaitOne();
                            }
                            data = null;
                            ISQ = null;
                        }
                    }
                }
                catch { }
                finally
                {
                    Dispose();
                }
            }

            public void Dispose()
            {
                if (isDisposed)
                {
                    return;
                }
                isDisposed = true;
                pingTimer.Dispose();
                if (sock != null)
                {
                    try
                    {
                        byte[] shutDownData = new byte[4];
                        try
                        {
                            sendProtocolData(protocol.SHUTDOWN, shutDownData);
                        }
                        catch { }
                        sock.Shutdown(SocketShutdown.Both);
                    }
                    catch { }
                    sock.Close(1000);
                }
                unregisterConnectedDevice(this);
                sendARE.Set();
            }

            public void enqueueDataToSend(byte[] data)
            {
                lock (dataToSend)
                {
                    dataToSend.Enqueue(data);
                }
                sendARE.Set();
            }

            public void enqueueDataToSend(IEnumerable<ISendQueue> dataList)
            {
                lock (advDataToSend)
                {
                    foreach (ISendQueue ISQ in dataList)
                    {
                        if (!ISQ.Fuse(advDataToSend))
                        {
                            advDataToSend.Add(ISQ);
                        }
                    }
                }
                sendARE.Set();
            }

            public void enqueueDataToSend(ISendQueue ISQ)
            {
                lock (advDataToSend)
                {
                    if (!ISQ.Fuse(advDataToSend))
                    {
                        advDataToSend.Add(ISQ);
                    }
                }
                sendARE.Set();
            }

            public void changeLatency(int newLatency)
            {
                if (requestedLatency != newLatency)
                {
                    requestedLatency = newLatency;
                    requestChangeLatency = true;
                    sendARE.Set();
                }
            }
        }

        /// <summary>
        /// The local device type
        /// </summary>
        static public deviceType localDeviceType;

        static List<device> connectedDevices = new List<device>();

        static void registerConnectedDevice(device dev)
        {
            lock (connectedDevices)
            {
                connectedDevices.Add(dev);
            }
        }

        static void unregisterConnectedDevice(device dev)
        {
            bool lastDeviceWork = false;
            lock (connectedDevices)
            {
                connectedDevices.Remove(dev);
                if (connectedDevices.Count == 0)
                {
                    lastDeviceWork = true;
                }
            }
            if (lastDeviceWork)
            {
                if (LastConnectionLost != null)
                {
                    LastConnectionLost(null);
                }
            }
        }

        /// <summary>
        /// Get all connected devices
        /// </summary>
        /// <param name="DT">Return devices of specified type</param>
        /// <returns>List of device returned</returns>
        public static List<device> getDevices(deviceType DT)
        {
            lock (connectedDevices)
            {
                if (DT == deviceType.NULL)
                {
                    return new List<device>(connectedDevices);
                }
                List<device> retList = new List<device>(connectedDevices.Count);
                foreach (device dev in connectedDevices)
                {
                    if (dev.type == DT)
                    {
                        retList.Add(dev);
                    }
                }
                return retList;
            }
        }

        static bool isDeviceAlreadyConnected(IPEndPoint IPEP)
        {
            lock (connectedDevices)
            {
                foreach (device dev in connectedDevices)
                {
                    if (dev.remoteIPEP.Equals(IPEP))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        static device getDevice(IPEndPoint IPEP)
        {
            lock (connectedDevices)
            {
                foreach (device dev in connectedDevices)
                {
                    if (dev.remoteIPEP.Equals(IPEP))
                    {
                        return dev;
                    }
                }
                return null;
            }
        }

        public class dataFlowLatency
        {
            /// <summary>
            /// The step tart at this value
            /// </summary>
            public int from;
            /// <summary>
            /// The step end at this value
            /// </summary>
            public int to;
            /// <summary>
            /// The time to wait in ms before sending data. if -1 the remote device will not send data (excepting protocol related data).
            /// </summary>
            public int latency;
        }

        static List<dataFlowLatency> dataFlowLatencyList = new List<dataFlowLatency>();

        /// <summary>
        /// This function is used to configure handling of local receive buffer saturation. 
        /// Basically the more the local buffer is full, the more the remote device will wait before sending data, and will even stop to send data to prevent local buffer overflow. 
        /// This function will automatically create the steps needed. This function is called automatically by the constructor with standard values.
        /// </summary>
        /// <param name="minLatency">The minimum time the remote device need to wait before sending data (typically 0)</param>
        /// <param name="latencyPerStep">The time increment the remote device need to wait before sending data at each step</param>
        /// <param name="stepWidth">The number of message in buffer before changing step</param>
        /// <param name="numberOfStep">The number of step</param>
        public static void setBasicDataFlowLatency(int minLatency, int latencyPerStep, int stepWidth, int numberOfStep)
        {
            lock (dataFlowLatencyList)
            {
                dataFlowLatencyList.Clear();
                int upperBoundary = stepWidth * numberOfStep;
                dataFlowLatency DFL;
                for (int i = 0; i < numberOfStep; i++)
                {
                    DFL = new dataFlowLatency();
                    DFL.from = i * stepWidth;
                    DFL.to = ((i + 1) * stepWidth) - 1;
                    DFL.latency = (latencyPerStep * i) + minLatency;
                    dataFlowLatencyList.Add(DFL);
                }
                DFL = new dataFlowLatency();
                DFL.from = numberOfStep * stepWidth;
                DFL.to = int.MaxValue;
                DFL.latency = -1;
                dataFlowLatencyList.Add(DFL);
            }
        }

        /// <summary>
        /// This function is used to configure handling of local receive buffer saturation. 
        /// Basically the more the local buffer is full, the more the remote device will wait before sending data, and will even stop to send data to prevent local buffer overflow. 
        /// With this function you'll need to provide your own stepping logic. We discourage the use of this function outside debug purpose.
        /// </summary>
        /// <param name="DFLList">The latency stepping logic.</param>
        public static void setCustomDataFlowLatency(IEnumerable<dataFlowLatency> DFLList)
        {
            lock (dataFlowLatencyList)
            {
                dataFlowLatencyList.Clear();
                dataFlowLatencyList.AddRange(DFLList);
                dataFlowLatencyList.Sort((Comparison<dataFlowLatency>)((x, y) =>
                {
                    return x.from.CompareTo(y.from);
                }));
            }
        }

        static void executeDataFlowLatency(int queueCount)
        {
            int latency = 0;
            lock (dataFlowLatencyList)
            {
                foreach (dataFlowLatency DFL in dataFlowLatencyList)
                {
                    if (queueCount <= DFL.to)
                    {
                        latency = DFL.latency;
                        break;
                    }
                }
            }
            foreach (device dev in getDevices(deviceType.NULL))
            {
                dev.changeLatency(latency);
            }
        }
        
        /// <summary>
        /// Contains the received data, and the remote device that sent it.
        /// </summary>
        public class dataBlock
        {
            /// <summary>
            /// Data from remote
            /// </summary>
            public byte[] data;
            /// <summary>
            /// The remote that sent data
            /// </summary>
            public device from;
        }

        static List<dataBlock> dataList = new List<dataBlock>();

        static void enqueueReceivedData(byte[] dataArg, device fromArg)
        {
            int dataListCount = 0;
            lock (dataList)
            {
                dataBlock DB = new dataBlock();
                DB.data = dataArg;
                DB.from = fromArg;
                dataList.Add(DB);
                dataListCount = dataList.Count;
            }
            executeDataFlowLatency(dataListCount);
            if (DataReceived != null)
            {
                DataReceived();
            }
        }

        /// <summary>
        /// Get all the data blocks from the specified remote type and remove them from the receive buffer
        /// </summary>
        /// <param name="DT">The type of device which we want data. If DT is deviceType.NULL all buffer will be returned.</param>
        /// <returns>A list of data blocks</returns>
        public static List<dataBlock> getDataFrom(deviceType DT)
        {
            List<dataBlock> retList;
            int dataListCount = 0;
            lock (dataList)
            {
                if (DT == deviceType.NULL)
                {
                    retList = new List<dataBlock>(dataList);
                    dataList.Clear();
                    dataListCount = 0;
                }
                else
                {
                    retList = new List<dataBlock>(dataList.Count);
                    for (int i = 0; i < dataList.Count; )
                    {
                        dataBlock DB = dataList[i];
                        if (DB.from.type == DT)
                        {
                            retList.Add(DB);
                            dataList.RemoveAt(i);
                        }
                        else
                        {
                            i++;
                        }
                    }
                    dataListCount = dataList.Count;
                }
            }
            executeDataFlowLatency(dataListCount);
            return retList;
        }

        /// <summary>
        /// Get the first data block from the specified remote type and remove it from the receive buffer
        /// </summary>
        /// <param name="DT">The type of device which we want data. If DT is deviceType.NULL the first block of any device type will be returned.</param>
        /// <returns>A data block</returns>
        public static dataBlock getFirstDataBlock(deviceType DT)
        {
            dataBlock DB = null;
            int dataListCount = 0;
            lock (dataList)
            {
                if (DT == deviceType.NULL && dataList.Count > 0)
                {
                    DB = dataList[0];
                    dataList.RemoveAt(0);
                    dataListCount = dataList.Count;
                }
                else
                {
                    for (int i = 0; i < dataList.Count; i++)
                    {
                        dataBlock curDB = dataList[i];
                        if (curDB.from.type == DT)
                        {
                            dataList.RemoveAt(i);
                            DB = curDB;
                            break;
                        }
                    }
                    dataListCount = dataList.Count;
                }
            }
            executeDataFlowLatency(dataListCount);
            return DB;
        }

        /// <summary>
        /// Enqueue data to send to specified device
        /// </summary>
        /// <param name="data">Array of byte to send to specified device</param>
        /// <param name="to">The type of device to send to. If more than one device of this type is present, the data will be sent to all these devices.
        /// If deviceType.NULL is passed, a broadcast to all connected devices will be send.</param>
        /// <returns>If no devices could be found, return false.</returns>
        public static bool enqueueDataToSend(byte[] data, deviceType to)
        {
            List<device> devList = getDevices(to);
            if (devList.Count == 0) 
            {
                if (NoConnectedDevice != null)
                {
                    NoConnectedDevice.Invoke();
                }
                return false;
            }
            foreach (device dev in devList)
            {
                dev.enqueueDataToSend(data);
            }
            return true;
        }

        /// <summary>
        /// Enqueue data to send to specified device
        /// </summary>
        /// <param name="dataList">List of data to send using the ISendQueue Interface</param>
        /// <param name="to">The type of device to send to. If more than one device of this type is present, the data will be sent to all these devices.
        /// If deviceType.NULL is passed, a broadcast to all connected devices will be send.</param>
        /// <returns></returns>
        public static bool enqueueDataToSend(IEnumerable<ISendQueue> dataList, deviceType to)
        {
            List<device> devList = getDevices(to);
            if (devList.Count == 0)
            {
                if (NoConnectedDevice != null)
                {
                    NoConnectedDevice.Invoke();
                }
                return false;
            }
            foreach (device dev in devList)
            {
                dev.enqueueDataToSend(dataList);
            }
            return true;
        }

        /// <summary>
        /// Enqueue data to send to specified device
        /// </summary>
        /// <param name="data">Data to send using the ISendQueue Interface</param>
        /// <param name="to">The type of device to send to. If more than one device of this type is present, the data will be sent to all these devices.
        /// If deviceType.NULL is passed, a broadcast to all connected devices will be send.</param>
        /// <returns></returns>
        public static bool enqueueDataToSend(ISendQueue data, deviceType to)
        {
            List<device> devList = getDevices(to);
            if (devList.Count == 0)
            {
                if (NoConnectedDevice != null)
                {
                    NoConnectedDevice.Invoke();
                }
                return false;
            }
            foreach (device dev in devList)
            {
                dev.enqueueDataToSend(data);
            }
            return true;
        }

        /// <summary>
        /// Establish a connection with specified destination
        /// </summary>
        /// <param name="IPEP">End point to which to establich connection</param>
        public static void launchConnection(IPEndPoint IPEP)
        {
            lock (connectedDevices)
            {
                bool alreadySet = false;
                foreach (device dev in connectedDevices)
                {
                    if (dev.remoteIPEP.Equals(IPEP))
                    {
                        alreadySet = true;
                    }
                }
                if (!alreadySet)
                {
                    device dev = new device();
                    if (!dev.startAndConnectTo(IPEP, deviceType.NULL))
                    {
                        if (FailToConnect != null)
                        {
                            FailToConnect(new List<device>(connectedDevices));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Shutdown communication with all devices.
        /// </summary>
        public static void shutdownDevices()
        {
            List<device> devList;
            lock (connectedDevices)
            {
                devList = new List<device>(connectedDevices);
            }
            foreach (device dev in devList)
            {
                dev.Dispose();
            }
        }

        /// <summary>
        /// Shutdown communication with all devices.
        /// </summary>
        public static void shutdownAll()
        {
            shutdownDevices();
        }

        static TCPLayerLite()
        {
            setBasicDataFlowLatency(0, 5, 50, 10);
        }

        /// <summary>
        /// Set the password related options
        /// </summary>
        /// <param name="securityArg">The security mode</param>
        /// <param name="passwordArg">The password</param>
        /// <param name="ckeckCRCArg">Is the CRC check is enabled</param>
        public static void setDefaultSecurityOptions(securityMode securityArg, byte[] usernameArg, byte[] passwordArg, bool ckeckCRCArg)
        {
            defaultSecurity = securityArg;
            defaultUserName = usernameArg;
            defaultPassword = passwordArg;
            if (defaultPassword != null)
            {
                using (SHA1Managed sha = new SHA1Managed())
                {
                    defaultPassSha1 = sha.ComputeHash(defaultPassword);
                }
            }
            else
            {
                defaultPassSha1 = null;
            }
            defaultCheckCRC = ckeckCRCArg;
        }

        /// <summary>
        /// The handler of connection related events
        /// </summary>
        /// <param name="devList">The affected device list</param>
        public delegate void ConnectionEventHandler(List<device> devList);

        /// <summary>
        /// TCPLayerLite.launchConnection Failed.
        /// </summary>
        public static event ConnectionEventHandler FailToConnect;

        /// <summary>
        /// TCPLayerLite.launchConnection succeeded.
        /// </summary>
        public static event ConnectionEventHandler DirectConnectionEstablished;

        /// <summary>
        /// All connections have been closed.
        /// </summary>
        public static event ConnectionEventHandler LastConnectionLost;

        /// <summary>
        /// A data block has been received and is waiting in buffer. Use TCPLayerLite.getDataFrom or TCPLayerLite.getFirstDataBlock to get data.
        /// </summary>
        public static event Action DataReceived;

        /// <summary>
        /// Trying to send data but no device of specified type is connected.
        /// </summary>
        public static event Action NoConnectedDevice;

    }
}
