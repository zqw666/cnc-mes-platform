using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace mcOMRON
{
    public class UdpFinsCommand : IFINSCommand
    {

        #region **** properties 属性

        public bool Connected
        {
            get { return this._transport.Connected; }
        }

        public string LastError
        {
            get { return this._lastError; }
        }

        public mcOMRON.ITransport Transport
        {
            get { return this._transport; }
        }

        public Byte[] Response
        {
            get { return this.respFinsData; }
        }


        #region **** FINS Command's fields

        public Byte ICF { get { return this.cmdFins[0]; } set { this.cmdFins[0] = value; } }
        public Byte RSC { get { return this.cmdFins[1]; } set { this.cmdFins[1] = value; } }
        public Byte GTC { get { return this.cmdFins[2]; } set { this.cmdFins[2] = value; } }
        public Byte DNA { get { return this.cmdFins[3]; } set { this.cmdFins[3] = value; } }
        public Byte DA1 { get { return this.cmdFins[4]; } set { this.cmdFins[4] = value; } }
        public Byte DA2 { get { return this.cmdFins[5]; } set { this.cmdFins[5] = value; } }
        public Byte SNA { get { return this.cmdFins[6]; } set { this.cmdFins[6] = value; } }
        public Byte SA1 { get { return this.cmdFins[7]; } set { this.cmdFins[7] = value; } }
        public Byte SA2 { get { return this.cmdFins[8]; } set { this.cmdFins[8] = value; } }
        public Byte SID { get { return this.cmdFins[9]; } set { this.cmdFins[9] = value; } }
        public Byte MC  { get { return this.cmdFins[10]; } set { this.cmdFins[10] = value; } }
        public Byte SC  { get { return this.cmdFins[11]; } set { this.cmdFins[11] = value; } }

        #endregion



        #region **** frame send command & response fields

        public UInt16 FS_LEN
        {
            get { return BTool.BytesToUInt16(this.cmdFS[6], this.cmdFS[7]); }
            set
            {
                this.cmdFS[6] = (Byte)((value >> 8) & 0xFF);
                this.cmdFS[7] = (Byte)(value & 0xFF);
            }
        }

        public UInt16 FSR_LEN
        {
            get { return BTool.BytesToUInt16(this.respFS[6], this.respFS[7]); }
        }

        public string FSR_ERR
        {
            get
            {
                return respFS[8].ToString()
                        + respFS[9].ToString()
                        + respFS[10].ToString()
                        + respFS[11].ToString();
            }
        }

        /// <summary>
        /// FINS response main code error (from respFS buffer)
        /// </summary>
        public Byte FSR_MER
        {
            get { return respFS[12]; }
        }

        /// <summary>
        /// FINS response subcode error (from respFS buffer)
        /// </summary>
        public Byte FSR_SER
        {
            get { return respFS[13]; }
        }

        #endregion

        #endregion



        #region **** constructor 构造函数

        public UdpFinsCommand(Byte ServiceID = 0x01)
        {
            this._transport = new mcOMRON.udpTransport();
            this.SID = ServiceID;
        }


        /// <summary>
        /// set ip and port. DA1 = PLC IP last octet.
        /// </summary>
        public void SetTCPParams(IPAddress ip, int port)
        {
            this._transport.SetTCPParams(ip, port);
            this.DA1 = Convert.ToByte(ip.ToString().Split('.')[3]);
            IPAddress[] adresslist = Dns.GetHostAddresses(Dns.GetHostName());//获取本机的ip地址集合（包括IPV4\IPV6）
            if (adresslist.Length < 1)
            {
                return;
            }
            IPAddress pcip = null;
            foreach (IPAddress IP in adresslist)
            {
                if (IP.AddressFamily == AddressFamily.InterNetwork)
                {
                    pcip = IP;
                    if (IP.GetAddressBytes()[0] == ip.GetAddressBytes()[0]
                        && IP.GetAddressBytes()[1] == ip.GetAddressBytes()[1]
                        && IP.GetAddressBytes()[2] == ip.GetAddressBytes()[2])
                    {
                        break;
                    }
                }

            }
            if (pcip == null)
            {
                throw new Exception("No IPv4 address found for local host.");
            }
            this.SA1 = Convert.ToByte(pcip.ToString().Split('.')[3]);
        }

        #endregion



        #region **** connect & close

        public bool Connect()
        {
            try
            {
                this._transport.Connect();
                return true;
            }
            catch (Exception ex)
            {
                this._lastError = ex.Message;
                return false;
            }
        }

        public void Close()
        {
            this._transport.Close();
        }

        #endregion



        #region **** methods 方法

        #region **** implemented fins commands

        /// <summary>
        /// MEMORY AREA READ [1,1]
        /// </summary>
        public bool MemoryAreaRead(MemoryArea area, UInt16 address, Byte bit_position, UInt16 count)
        {
            try
            {
                this.MC = 0x01;
                this.SC = 0x01;

                this.cmdFins[F_PARAM] = (byte)area;
                this.cmdFins[F_PARAM + 1] = (Byte)((address >> 8) & 0xFF);
                this.cmdFins[F_PARAM + 2] = (Byte)(address & 0xFF);
                this.cmdFins[F_PARAM + 3] = bit_position;
                this.cmdFins[F_PARAM + 4] = (Byte)((count >> 8) & 0xFF);
                this.cmdFins[F_PARAM + 5] = (Byte)(count & 0xFF);

                list_cmdfins = new List<byte>();
                list_cmdfins.AddRange(cmdFins);

                return FrameSend(null);
            }
            catch (Exception ex)
            {
                this._lastError = ex.Message;
                return false;
            }
        }


        /// <summary>
        /// MEMORY AREA WRITE [1,2]
        /// </summary>
        public bool MemoryAreaWrite(MemoryArea area, UInt16 address, Byte bit_position, UInt16 count, ref Byte[] data)
        {
            try
            {
                this.MC = 0x01;
                this.SC = 0x02;

                this.cmdFins[F_PARAM] = (byte)area;
                this.cmdFins[F_PARAM + 1] = (Byte)((address >> 8) & 0xFF);
                this.cmdFins[F_PARAM + 2] = (Byte)(address & 0xFF);
                this.cmdFins[F_PARAM + 3] = bit_position;
                this.cmdFins[F_PARAM + 4] = (Byte)((count >> 8) & 0xFF);
                this.cmdFins[F_PARAM + 5] = (Byte)(count & 0xFF);

                list_cmdfins = new List<byte>();
                list_cmdfins.AddRange(cmdFins);
                list_cmdfins.AddRange(data);

                return FrameSend(data);
            }
            catch (Exception ex)
            {
                this._lastError = ex.Message;
                return false;
            }
        }


        /// <summary>
        /// CONNECTION DATA READ [5,1]
        /// </summary>
        public bool ConnectionDataRead(Byte area)
        {
            try
            {
                this.MC = 0x05;
                this.SC = 0x01;

                this.cmdFins[F_PARAM] = (Byte)area;
                this.finsCommandLen = 13;

                return FrameSend(null);
            }
            catch (Exception ex)
            {
                this._lastError = ex.Message;
                return false;
            }
        }

        #endregion


        #region **** frame send

        /// <summary>
        /// UDP FRAME SEND - sends raw FINS command (no FS header needed for UDP)
        /// and receives response into respFS buffer, then extracts data to respFinsData.
        /// </summary>
        private bool FrameSend(Byte[] data)
        {
            // 组装并发送 FINS 命令
            newcmdfins = new Byte[list_cmdfins.Count];
            for (int i = 0; i < list_cmdfins.Count; i++)
            {
                newcmdfins[i] = list_cmdfins[i];
            }
            this.finsCommandLen = (UInt16)this.newcmdfins.Length;
            this.Transport.Send(ref this.newcmdfins, this.finsCommandLen);

            // 清空响应缓冲区
            for (int x = 0; x < this.respFS.Length; respFS[x++] = 0) ;
            for (int x = 0; x < this.respFinsData.Length; respFinsData[x++] = 0) ;

            // 接收 FINS 响应
            int responseLen = this.Transport.Receive(ref respFS, respFS.Length);
            this.lastResponseLen = responseLen;
            if (responseLen < 14)
            {
                this._lastError = string.Format("FINS response too short: {0} bytes", responseLen);
                return false;
            }

            // 提取响应数据（头部14字节之后的数据）
            if (responseLen > 14)
            {
                Array.Copy(this.respFS, 14, this.respFinsData, 0, responseLen - 14);
            }

            // 检查 FINS 响应码
            if (this.FSR_MER != 0 || this.FSR_SER != 0)
            {
                this._lastError += string.Format("Response Code error: (Code: {0}  Subcode: {1})",
                                                    this.FSR_MER, this.FSR_SER);
                return false;
            }

            return true;
        }

        #endregion

        #endregion



        #region **** debug dialog 

        public string LastDialog(string Caption)
        {
            StringBuilder dialog = new StringBuilder(Caption + Environment.NewLine);
            if (this.lastResponseLen >= 16)
            {
                dialog.Append(Convert.ToString(Convert.ToInt64(respFS[14].ToString(), 16), 2)
                    + Convert.ToString(Convert.ToInt64(respFS[15].ToString(), 16), 2) + Environment.NewLine);
            }
            dialog.Append(BitConverter.ToString(this.respFS, 0, this.lastResponseLen) + Environment.NewLine);

            return dialog.ToString();
        }


        /// <summary>
        /// 返回 FINS 响应二进制数据
        /// </summary>
        public string ReturnMess()
        {
            string resp = BitConverter.ToString(this.respFS, 0, this.lastResponseLen);
            resp = resp.Substring(resp.Length - 5, 5);
            string result = resp.Replace("-", "");
            string hex = string.Format("{0:x}", result);
            string data = Convert.ToString(Convert.ToInt64(hex, 16), 2);
            return data;
        }

        #endregion



        #region **** variables 变量

        private string _lastError = "";
        mcOMRON.udpTransport _transport = null;

        List<Byte> list_cmdfins;
        Byte[] newcmdfins;

        // FRAME SEND header array (not used in UDP, kept for reference)
        Byte[] cmdFS = new Byte[]
        {
            0x46, 0x49, 0x4E, 0x53,     // 'F' 'I' 'N' 'S'
            0x00, 0x00, 0x00, 0x00,     // Expected response length
            0x00, 0x00, 0x00, 0x02,     // Command FS: Sending=2
            0x00, 0x00, 0x00, 0x00      // Error code
        };

        // FINS response buffer (2KB)
        Byte[] respFS = new Byte[2048];
        int lastResponseLen = 0;

        // FINS response header (14 bytes)
        Byte[] respFins = new Byte[2048];

        // FINS response data (extracted from respFS after 14-byte header)
        Byte[] respFinsData = new Byte[2048];

        // FINS command frame
        Byte[] cmdFins = new Byte[]
        {
            0x80,               // 00 ICF
            0x00,               // 01 RSC
            0x02,               // 02 GTC
            0x00,               // 03 DNA
            0x00,               // 04 DA1
            0x00,               // 05 DA2
            0x00,               // 06 SNA
            0x0A,               // 07 SA1
            0x00,               // 08 SA2
            0x00,               // 09 SID
            0x00,               // 10 MC
            0x00,               // 11 SC
            0x00,               // 12 PARAM area
            0x00,               // 13
            0x00,               // 14
            0x00,               // 15
            0x00,               // 16
            0x00,               // 17
        };

        UInt16 finsCommandLen = 0;
        const int F_PARAM = 12;

        #endregion
    }
}
