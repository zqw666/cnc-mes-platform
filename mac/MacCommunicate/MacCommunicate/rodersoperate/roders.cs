using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Xml;
using System.IO;
using static MacCommunicate.Roders;
using static MacCommunicate.RodersData;

namespace MacCommunicate
{
    /// <summary>
    /// 罗德斯服务器类
    /// </summary>
    public class Roders_Server
    {
        /// <summary>
        /// 机床连接委托
        /// </summary>
        /// <param name="Machine"></param>
        public delegate void Machine_Connected(Roders Machine);

        /// <summary>
        /// 机床连接成功后触发事件
        /// </summary>
        public Machine_Connected MachineConnected;

        /// <summary>
        /// 服务器Socket
        /// </summary>
        private Socket ServerSocket = null;
        /// <summary>
        /// 状态端口
        /// </summary>
        private Socket ClientSocket = null;

        /// <summary>
        /// 获取当前服务器IP
        /// </summary>
        public string Server_IP { get; }

        /// <summary>
        /// 获取当前服务器命令socket端口
        /// </summary>
        public int Cmd_Port { get; }

        /// <summary>
        /// 获取当前服务器状态socket端口
        /// </summary>
        public int Stu_Port { get; }

        /// <summary>
        /// 获取或设置通信编码方式
        /// </summary>
        public Encoding Encoding { get; set; } = Encoding.GetEncoding("ISO-8859-1");

        /// <summary>
        /// 获取已连接成功的所有机床
        /// </summary>
        public Roders[] Machines { get { if (Connect_Machines != null && Connect_Machines.Count > 0) return this.Connect_Machines.ToArray(); else return new Roders[0]; } }

        /// <summary>
        /// 记录已连接成功的机床
        /// </summary>
        private List<Roders> Connect_Machines = new List<Roders> { };


        /// <summary>
        /// 移除指定机床
        /// </summary>
        /// <param name="Index">机床下标</param>
        public bool RemoveMachine(int Index)
        {
            if (Index < 0 || Index >= Connect_Machines.Count)
                return false;
            Connect_Machines[Index].Close();
            Connect_Machines.RemoveAt(Index);
            return true;
        }

        /// <summary>
        /// 移除指定机床
        /// </summary>
        /// <param name="Index">机床下标</param>
        public bool RemoveMachine(Roders Machine)
        {
            try
            {
                int index = Connect_Machines.IndexOf(Machine);
                Connect_Machines[index].Close();
                Connect_Machines.RemoveAt(index);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 监听机床标志
        /// </summary>
        private bool Listen_Flag = true;
        /// <summary>
        /// 监听机床线程
        /// </summary>
        private Thread Listen_Thread = null;

        /// <summary>
        /// 定时发送WDREQ命令的时间，单位ms
        /// </summary>
        public UInt32 WDREQ_Time { get { return WDREQ_Time_Value; } set { if (value <= 0) return; WDREQ_Time_Value = value; } }
        private UInt32 WDREQ_Time_Value = 20000;//默认值20秒

        /// <summary>
        /// 创建服务器
        /// </summary>
        /// <param name="IP">服务器绑定本地IP，即机床需要连接的服务器IP</param>
        /// <param name="CmdPort">命令socket绑定本地端口</param>
        /// <param name="StuPort">状态socket绑定本地端口，默认为CmdPort+1</param>
        /// <param name="Encoding">xml的编码方式，默认为ISO-8859-1</param>
        public Roders_Server(string IP, int CmdPort, int StuPort = 0, Encoding Encoding = null)
        {
            if (Encoding != null)
                this.Encoding = Encoding;
            Server_IP = IP;
            Cmd_Port = CmdPort;
            if (StuPort <= 0)
                Stu_Port = CmdPort + 1;
            else
                Stu_Port = StuPort;
            ServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);//实例化socket对象
            ServerSocket.Bind(new IPEndPoint(IPAddress.Parse(IP), CmdPort));//向系统申请一个可用的IP地址和端口号
            ServerSocket.Listen(0);//最大客户端连接数
            ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);//实例化socket对象
            ClientSocket.Bind(new IPEndPoint(IPAddress.Parse(Server_IP), Stu_Port));//向系统申请一个可用的IP地址和端口号
            ClientSocket.Listen(10);//最大客户端连接数
        }


        /// <summary>
        /// 监听机床连接线程
        /// 注：命令Socket已连接，如果状态Socket一直不连接将导致无限等待，请调用Close方法
        /// </summary>
        /// <param name="TelegramID">发送xml命令报的id</param>
        /// <param name="Version">xml命令报的版本，默认为1.0</param>
        /// <param name="ReceiveTimeout">连接状态端口命令超时时间，建议大于0，默认为2秒，详细参考System.Net.Sockets.ReceiveTimeout</param>
        /// <returns></returns>
        public void Listen_Machine_Thread(int TelegramID, string Version = "1.0", int ReceiveTimeout = 2000)
        {
            Listen_Flag = true;
            Listen_Thread = new Thread(() => {
                int telid = TelegramID;
                while (Listen_Flag)
                {
                    try
                    {
                        byte[] cmdbuff = new byte[2048];
                        //需要先绑定本地端口，再发送连接命令，最后Accept获取连接请求的机床
                        if (ClientSocket != null)
                        {
                            ClientSocket.Close();
                            ClientSocket.Dispose();
                        }
                        ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);//实例化socket对象
                        ClientSocket.Bind(new IPEndPoint(IPAddress.Parse(Server_IP), Stu_Port));//向系统申请一个可用的IP地址和端口号
                        ClientSocket.Listen(10);//最大客户端连接数
                        Socket stat = null;
                        Socket cmd = ServerSocket.Accept();//接收机床客户端的异步连接
                        cmd.ReceiveTimeout = ReceiveTimeout;
                        //发送连接状态端口命令
                        cmd.Send(Encoding.GetBytes(Roders_Cmd.Get_CON(telid, Version)));
                        //接收反馈
                        try
                        {
                            cmd.Receive(cmdbuff);
                        }
                        catch (Exception ec)
                        {
                            Console.WriteLine(Server_IP + "," + ec.Message);
                            cmd.Close();
                            cmd.Dispose();
                            continue;
                        }
                        string Rec = Encoding.GetString(cmdbuff).TrimEnd('\0');//去掉结尾的空白和xml报的结尾符0x03
                        Rec = Rec.Replace((char)0x03, '\n');
                        XmlDocument xml = new XmlDocument();
                        xml.LoadXml(Rec);
                        XmlNodeList list = xml.GetElementsByTagName("ret");
                        //判断客户端是否已连接到本地的状态端口
                        if (list[0].InnerText == "OK")
                        {
                            stat = ClientSocket.Accept();
                            stat.ReceiveTimeout = 500;
                            byte[] stubuff = new byte[2048];
                            stat.Receive(stubuff);//把连接状态报告获取

                            string MachineName = xml.GetElementsByTagName("name")[0].InnerText;
                            string MachineType = xml.GetElementsByTagName("type")[0].InnerText;
                            foreach (Roders r in Connect_Machines)
                            {
                                try
                                {
                                    IPEndPoint ipo = (IPEndPoint)cmd.RemoteEndPoint;
                                    if (r.MachineEndPoint.Address.ToString() == ipo.Address.ToString())//IP相同，也移除
                                    {
                                        r.Close();
                                        Connect_Machines.Remove(r);
                                        break;
                                    }
                                }
                                catch
                                {
                                    r.Close();
                                    Connect_Machines.Remove(r);
                                    break;
                                }
                            }
                            Roders machine = new Roders(MachineName, MachineType, cmd, stat, telid, Version);
                            telid++;
                            Connect_Machines.Add(machine);
                            Thread t1 = new Thread(() => {
                                if (MachineConnected != null)
                                    MachineConnected.Invoke(machine);
                            });
                            t1.Start();
                        }
                        ClientSocket.Close();
                        ClientSocket.Dispose();
                    }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }
                }
            });
            Listen_Thread.Start();
        }

        /// <summary>
        /// 仅关闭服务器
        /// </summary>
        public void Close_Server()
        {
            Listen_Flag = false;
            if (ServerSocket != null)
            {
                ServerSocket.Close();
                ServerSocket.Dispose();
                ServerSocket = null;
            }
            if (ClientSocket != null)
            {
                ClientSocket.Close();
                ClientSocket.Dispose();
                ClientSocket = null;
            }
            if (Listen_Thread != null)
            {
                Listen_Thread.Abort();
                Listen_Thread = null;
            }
        }

        /// <summary>
        /// 关闭服务器和机床
        /// </summary>
        public void Close()
        {
            Listen_Flag = false;
            foreach (Roders r in Connect_Machines)
            {
                r.Close();
            }
            Connect_Machines.Clear();
            if (ServerSocket != null)
            {
                ServerSocket.Close();
                ServerSocket.Dispose();
                ServerSocket = null;
            }
            if (ClientSocket != null)
            {
                ClientSocket.Close();
                ClientSocket.Dispose();
                ClientSocket = null;
            }
            if (Listen_Thread != null)
            {
                Listen_Thread.Abort();
                Listen_Thread = null;
            }
        }
    }

    /// <summary>
    /// 获取罗德斯命令类
    /// </summary>
    public static class Roders_Cmd
    {
        /// <summary>
        /// 获取连接状态端口命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_CON(int TelegramID, string Version)
        {
            return "<?xml version=\"1.0\"?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>CON</cmd>\r\n\t\t<ver>" +
                            Version + "</ver>\r\n\t\t<id>" +
                            TelegramID.ToString() + "</id>\r\n\t</header>\r\n</msg>";
        }

        /// <summary>
        /// 获取断开连接状态端口命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_DIS(int TelegramID, string Version)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>DIS</cmd>\r\n\t\t<ver>" +
                 Version + "</ver>\r\n\t\t<id>" +
                 TelegramID.ToString() + "</id>\r\n\t</header>\r\n</msg>";
        }

        /// <summary>
        /// 获取WDREQ命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_WDREQ(int TelegramID, string Version)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>WDREQ</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n</msg>";
        }

        /// <summary>
        /// 获取PTREQ命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_PTREQ(int TelegramID, string Version)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>PTREQ</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n</msg>";
        }

        /// <summary>
        /// 获取COREQ命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="Chuck"></param>
        /// <returns></returns>
        public static string Get_COREQ(int TelegramID, string Version, int Chuck)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>COREQ</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>\r\n\t\t<chuck>" +
                Chuck.ToString() + "</chuck>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取TTREQ命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="Magazine"></param>
        /// <returns></returns>
        public static string Get_TTREQ(int TelegramID, string Version, TTREQ_Magazine Magazine)
        {
            string mag = null;
            switch (Magazine)
            {
                case TTREQ_Magazine.INTERNAL:
                    mag = "INTERNAL";
                    break;
                case TTREQ_Magazine.EXTERNAL:
                    mag = "EXTERNAL";
                    break;
            }
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>TTREQ</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>\r\n\t\t<magazine>" +
                mag + "</magazine>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取PLREP命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_PLREP(int TelegramID, string Version, Pallet[] Pallet)
        {
            string value = "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>PLREP</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>{0}\r\n\t</data>\r\n</msg>";
            string pa_Value = "";
            foreach (Pallet pa in Pallet)
            {
                string pa_xml = "\r\n\t\t<pallet>\r\n\t\t\t <id> " +
                pa.ChipID.ToString() + "</id>\r\n\t\t\t<objects>{0}\r\n\t\t\t</objects>\r\n\t\t</pallet>";
                string obj = "";
                foreach (Cmd_Object part in pa.Objects)
                {
                    obj += "\r\n\t\t\t\t<obj>\r\n\t\t\t\t\t" +
                "<name>" + part.Name + "</name>\r\n\t\t\t\t\t" +
                "<comment>" + part.Comment + "</comment>\r\n\t\t\t\t\t" +
                "<id>" + part.ID.ToString() + "</id>\r\n\t\t\t\t\t" +
                "<x>" + part.x.ToString() + "</x>\r\n\t\t\t\t\t" +
                "<y>" + part.y.ToString() + "</y>\r\n\t\t\t\t\t" +
                "<z>" + part.z.ToString() + "</z>\r\n\t\t\t\t\t" +
                "<c>" + part.c.ToString() + "</c>\r\n\t\t\t\t</obj>";
                }
                pa_Value += string.Format(pa_xml, obj);
            }
            return string.Format(value, pa_Value);
        }

        /// <summary>
        /// 获取PTREP命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="Chucks"></param>
        /// <param name="Places"></param>
        /// <returns></returns>
        public static string Get_PTREP(int TelegramID, string Version, Chuck[] Chucks, Places[] Places)
        {
            string value = "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>PTREP</cmd> \r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" + "{0}{1}" +
                "\r\n\t</data>\r\n</msg>";
            string chuck = "";
            foreach (Chuck ch in Chucks)
            {
                string sta = "";
                switch (ch.State)
                {
                    case State.EMPTY: sta = "EMPTY"; break;
                    case State.FULL: sta = "FULL"; break;
                    case State.LOCKED: sta = "LOCKED"; break;
                }
                string chu = "\r\n\t\t<chuck>" +
                            "\r\n\t\t\t<pos>" + ch.Pos + "</pos>" +
                            "\r\n\t\t\t<state>" + sta + "</state>" +
                            "\r\n\t\t\t<subpos>" + Convert.ToInt32(ch.SubPos).ToString() + "</subpos>" +
                            "\r\n\t\t\t<pos_org>" + ch.Pos_Org + "</pos_org>" +
                            "\r\n\t\t\t<id>" + ch.ChipID + "</id>" +
                            "\r\n\t\t\t<objects>" +
                                "{0}" +
                            "\r\n\t\t\t</objects>" +
                    "\r\n\t\t</chuck>";
                string obj = "";
                foreach (Cmd_Object ot in ch.Objects)
                {
                    obj += "\r\n\t\t\t\t<obj>" +
                                    "\r\n\t\t\t\t\t<name>" + ot.Name + "</name>" +
                                    "\r\n\t\t\t\t\t<comment>" + ot.Comment + "</comment>" +
                                    "\r\n\t\t\t\t\t<id>" + ot.ID + "</id>" +
                                    "\r\n\t\t\t\t\t<x>" + ot.x + "</x>" +
                                    "\r\n\t\t\t\t\t<y>" + ot.y + "</y>" +
                                    "\r\n\t\t\t\t\t<z>" + ot.z + "</z>" +
                                    "\r\n\t\t\t\t\t<c>" + ot.c + "</c>" +
                                "\r\n\t\t\t\t</obj>";
                }
                chuck += string.Format(chu, obj);
            }
            string place = "";
            foreach (Places pla in Places)
            {
                string sta = "";
                switch (pla.State)
                {
                    case State.EMPTY: sta = "EMPTY"; break;
                    case State.FULL: sta = "FULL"; break;
                    case State.LOCKED: sta = "LOCKED"; break;
                }
                string pla_str = "\r\n\t\t<place>" +
                    "\r\n\t\t\t<pos>" + pla.Pos.ToString() + "</pos>" +
                    "\r\n\t\t\t<state>" + sta + "</state>" +
                    "\r\n\t\t\t<id>" + pla.ChipID + "</id>" +
                    "\r\n\t\t\t<objects>" + "{0}" +
                    "\r\n\t\t\t</objects>\r\n\t\t</place>";
                string obj = "";
                foreach (Cmd_Object ot in pla.Objects)
                {
                    obj += "\r\n\t\t\t\t<obj>" +
                        "\r\n\t\t\t\t\t<name>" + ot.Name + "</name>" +
                        "\r\n\t\t\t\t\t<comment>" + ot.Comment + "</comment>" +
                        "\r\n\t\t\t\t\t<id>" + ot.ID + "</id>" +
                        "\r\n\t\t\t\t\t<x>" + ot.x + "</x>" +
                        "\r\n\t\t\t\t\t<y>" + ot.y + "</y>" +
                        "\r\n\t\t\t\t\t<z>" + ot.z + "</z>" +
                        "\r\n\t\t\t\t\t<c>" + ot.c + "</c>" +
                        "\r\n\t\t\t\t</obj>";
                }
                place += string.Format(pla_str, obj);
            }
            return string.Format(value, chuck, place);
        }

        /// <summary>
        /// 获取TTREP命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="Places"></param>
        /// <returns></returns>
        public static string GetTTREP(int TelegramID, string Version, Place[] Places)
        {
            string value = "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>TTREP</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" + "{0}" + "\r\n\t</data>\r\n</msg>";
            string pl_str = "";
            foreach (Place pl in Places)
            {
                string sts = "";
                switch (pl.State)
                {
                    case State.LOCKED: sts = "LOCKED"; break;
                    case State.FULL: sts = "FULL"; break;
                    case State.EMPTY: sts = "EMPTY"; break;
                }
                pl_str += "\r\n\t\t <place>\r\n\t\t\t" +
                "<pos>" + pl.Pos.ToString() + "</pos>\r\n\t\t\t" +
                "<state>" + sts + "</state>\r\n\t\t\t" +
                "<id>" + pl.ChipID.ToString() + "</id>\r\n\t\t</place>";
            }
            return string.Format(value, pl_str);
        }

        /// <summary>
        /// 获取运行命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="TitleName"></param>
        /// <param name="Assembly"></param>
        /// <param name="Component"></param>
        /// <param name="UserText"></param>
        /// <param name="File"></param>
        /// <param name="NcLine"></param>
        /// <param name="Pieces"></param>
        /// <returns></returns>
        public static string Get_RUN(int TelegramID, string Version, string File)
        {
            string value = "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>RUN</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<file>" + File + "</file>\r\n\t</data>\r\n</msg>";
            return string.Format(value);
        }

        /// <summary>
        /// 获取ECO命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="State"></param>
        /// <returns></returns>
        public static string Get_ECO(int TelegramID, string Version, Eco_State State)
        {
            string sta = "MON";
            if (State == Eco_State.MON)
                sta = "MOFF";
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>ECO</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<state>" + sta + "</state>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取上传NC文件命令(不推荐方法，此方法为机床主动去目标路径拿取程序)
        /// </summary>
        /// <param name="TelegramID">xml报id</param>
        /// <param name="Version">版本信息</param>
        /// <param name="Source">UNC-path with source file name</param>
        /// <param name="Dest">rel. path and name of destination file name</param>
        /// <returns></returns>
        public static string Get_NCLOAD(int TelegramID, string Version, string Source, string Dest)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>NCLOAD</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<source>" + Source + "</source>" +
                "\r\n\t\t<dest>" + Dest + "</dest>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取获取NC文件命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="Source">rel. path and name of destination file name</param>
        /// <param name="Dest">UNC-path with source file name</param>
        /// <returns></returns>
        public static string Get_NCGET(int TelegramID, string Version, string Source, string Dest)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>NCGET</cmd>\r\n\t\t<ver>" +
                Version + "1.000</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<source>" + Source + "</source>" +
                "\r\n\t\t<dest>" + Dest + "</dest>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取删除NC文件命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Dest"></param>
        /// <returns></returns>
        public static string Get_NCDEL(int TelegramID, string Version, string Dest)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>NCDEL</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<dest>" + Dest + "</dest>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取获取NC文件列表命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_NCLIST(int TelegramID, string Version)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>NCLIST</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取CHANGEPOS的两个命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="FirstCmd_Chuck">第一命令的chuck</param>
        /// <param name="FirstCmd_Spindle">第一命令的Spindle</param>
        /// <param name="SecondCmd_State">第二命令State</param>
        /// <returns>第一命令：改变pos命令；第二命令：改变后状态</returns>
        public static string[] Get_CHANGEPOS(int TelegramID, string Version, int FirstCmd_Chuck, int FirstCmd_Spindle, CHANGEPOS_State SecondCmd_State)
        {
            string state = "FINISHED";
            if (SecondCmd_State == CHANGEPOS_State.ERROR)
                state = "ERROR";
            string[] Value = new string[2] { "", "" };
            Value[0] = "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>CHANGEPOS</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<chuck>" + FirstCmd_Chuck + "</chuck>" +
                "\r\n\t\t<spindle>" + FirstCmd_Spindle + "</spindle>\r\n\t</data>\r\n</msg>";
            Value[1] = "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>CHANGEPOS</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<changestate>" + state + "</changestate>\r\n\t</data>\r\n</msg>";
            return Value;
        }

        /// <summary>
        /// 获取PDOPEN命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="Chuck">chuck number for positioning (default: 1)</param>
        /// <returns></returns>
        public static string Get_PDOPEN(int TelegramID, string Version, int Chuck = 1)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>PDOPEN</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<chuck>" + Chuck.ToString() + "</chuck>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取PDCLOSE命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_PDCLOSE(int TelegramID, string Version)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>PDCLOSE</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取TDOPEN命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_TDOPEN(int TelegramID, string Version)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>TDOPEN</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取TDCLOSE命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="State"></param>
        /// <returns></returns>
        public static string Get_TDCLOSE(int TelegramID, string Version, TDCLOSE_State State)
        {
            string sta = "COMPLETE";
            if (State == TDCLOSE_State.INCOMPLETE)
                sta = "INCOMPLETE";
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>TDCLOSE</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<changestate>" + sta + "</changestate>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取TTLA命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="ToolType"></param>
        /// <returns></returns>
        public static string Get_TTLA(int TelegramID, string Version, string ToolType)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>TTLA</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<tooltype>" + ToolType + "</tooltype>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取CHANGE命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="ObjectType"></param>
        /// <param name="Magget"></param>
        /// <param name="Magput"></param>
        /// <param name="Macget"></param>
        /// <param name="Chuckget"></param>
        /// <param name="SubChuckget"></param>
        /// <param name="CleanCycles"></param>
        /// <param name="Macput"></param>
        /// <param name="Chuckput"></param>
        /// <param name="SubChuckput"></param>
        /// <returns></returns>
        public static string Get_CHANGE(int TelegramID, string Version, CHANGE_ObjType ObjectType, int Magget, int Magput, int Macget, int Chuckget, SubChuck SubChuckget, int CleanCycles, int Macput, int Chuckput, SubChuck SubChuckput)
        {
            string objtype = "PAL";
            if (ObjectType == CHANGE_ObjType.TOOL)
                objtype = "TOOL";
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>CHANGE</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<objtype>" + objtype + "</objtype>" +
                "\r\n\t\t<magget>" + Magget.ToString() + "</magget>" +
                "\r\n\t\t<magput>" + Magput.ToString() + "</magput>" +
                "\r\n\t\t<macget>" + Macget.ToString() + "</macget>" +
                "\r\n\t\t<chuckget>" + Chuckget.ToString() + "</chuckget>" +
                "\r\n\t\t<subchuckget>" + Convert.ToInt16(SubChuckget).ToString() + "</subchuckget>" +
                "\r\n\t\t<cleancycles>" + CleanCycles.ToString() + "</cleancycles>" +
                "\r\n\t\t<macput>" + Macput.ToString() + "</macput>" +
                "\r\n\t\t<chuckput>" + Chuckput.ToString() + "</chuckput>" +
                "\r\n\t\t<subchuckput>" + Convert.ToInt16(SubChuckput).ToString() + "</subchuckput>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取LOCK命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <param name="SetLock">auto: TRUE, active: FALSE</param>
        /// <returns></returns>
        public static string Get_LOCK(int TelegramID, string Version, bool SetLock)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>LOCK</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>" +
                "\r\n\t\t<setlock>" + SetLock.ToString().ToUpper() + "</setlock>\r\n\t</data>\r\n</msg>";
        }

        /// <summary>
        /// 获取ABORT命令
        /// </summary>
        /// <param name="TelegramID"></param>
        /// <param name="Version"></param>
        /// <returns></returns>
        public static string Get_ABORT(int TelegramID, string Version)
        {
            return "<?xml version=“1.0“?>\r\n<msg>\r\n\t<header>\r\n\t\t<cmd>ABORT</cmd>\r\n\t\t<ver>" +
                Version + "</ver>\r\n\t\t<id>" +
                TelegramID.ToString() + "</id>\r\n\t</header>\r\n\t<data>\r\n\t</data>\r\n</msg>";
        }
    }

    /// <summary>
    /// 外部刀具请求类
    /// </summary>
    public class Roders_ToolRequest
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="MagposGet"></param>
        /// <param name="MagposPut"></param>
        /// <param name="InternalPos"></param>
        public Roders_ToolRequest(int MagposGet, int MagposPut, int InternalPos)
        {
            this.MagposGet = MagposGet;
            this.MagposPut = MagposPut;
            this.InternalPos = InternalPos;
        }

        /// <summary>
        /// 获取的外部刀号
        /// </summary>
        public int MagposGet { get; } = -1;

        /// <summary>
        /// 放置的外部刀号
        /// </summary>
        public int MagposPut { get; } = -1;

        /// <summary>
        /// 内部刀号
        /// </summary>
        public int InternalPos { get; } = -1;
    }

    /// <summary>
    /// 错误信息类
    /// </summary>
    public class Roders_ErrMsg
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="Code">错误码</param>
        /// <param name="Text">错误信息</param>
        public Roders_ErrMsg(string Code, string Text)
        {
            this.Code = Code;
            this.Text = Text;
            switch (Code)
            {
                case "0":
                    {
                        SoftError = false;
                        Self_Scratching = false;
                        Message = "内部错误";
                        HowToDo = "重启设备软件";
                        ToDo = "Machine control has to be restarted.";
                    }
                    break;
                case "1":
                    {
                        SoftError = false;
                        Self_Scratching = true;
                        Message = "无法打开托盘自动门";
                        HowToDo = "自锁机床以便自动操作";
                        ToDo = "Lock machine for further automatic operation.";
                    }
                    break;
                case "2":
                    {
                        SoftError = false;
                        Self_Scratching = true;
                        Message = "无法关闭托盘自动门";
                        HowToDo = "自锁机床以便自动操作";
                        ToDo = "Lock machine for further automatic operation.";
                    }
                    break;
                case "3":
                    {
                        SoftError = false;
                        Self_Scratching = true;
                        Message = "无法打开刀具自动门";
                        HowToDo = "自锁机床以便自动操作";
                        ToDo = "Lock machine for further automatic operation.";
                    }
                    break;
                case "4":
                    {
                        SoftError = false;
                        Self_Scratching = true;
                        Message = "无法关闭刀具自动门";
                        HowToDo = "自锁机床以便自动操作";
                        ToDo = "Lock machine for further automatic operation.";
                    }
                    break;
                case "5":
                    {
                        SoftError = true;
                        Self_Scratching = true;
                        Message = "加工意外停止（软件停止）";
                        HowToDo = "可以开始下一步加工，操作员需检查最后一个加工内容";
                        ToDo = "Further jobs can be started,operator has to check last job.";
                    }
                    break;
                case "6":
                    {
                        SoftError = false;
                        Self_Scratching = true;
                        Message = "加工意外停止（硬件停止）";
                        HowToDo = "机床改为手动操作模式，操作员需检查最后一个加工内容";
                        ToDo = "Machine in manual mode,operator has to check last job.";
                    }
                    break;
                case "7":
                    {
                        SoftError = false;
                        Self_Scratching = true;
                        Message = "文件执行失败";
                        HowToDo = "自锁机床以便自动操作";
                        ToDo = "Lock machine for further automatic operation.";
                    }
                    break;
                case "8":
                    {
                        SoftError = false;
                        Self_Scratching = true;
                        Message = "倍率为0，请求加工无法启动";
                        HowToDo = "增加倍率";
                        ToDo = "Increase override.";
                    }
                    break;
                case "9":
                    {
                        SoftError = false;
                        Self_Scratching = false;
                        Message = "机床处于错误状态";
                        HowToDo = "机床改为手动操作模式，操作员需检查机床";
                        ToDo = "Machine in manual mode,operator has to check last job.";
                    }
                    break;
                default:
                    {
                        SoftError = false;
                        Self_Scratching = false;
                        Message = "未知错误";
                        HowToDo = "请检查机床";
                        ToDo = "Uknow error,please check machine.";
                    }
                    break;
            }
        }

        /// <summary>
        /// 错误码
        /// </summary>
        public string Code { get; } = "";

        /// <summary>
        /// 软件错误
        /// </summary>
        public bool SoftError { get; } = false;

        /// <summary>
        /// 为True则错误会发送一次，内部会重置
        /// </summary>
        public bool Self_Scratching { get; } = false;

        /// <summary>
        /// 错误提示
        /// </summary>
        public string Text { get; } = "";

        /// <summary>
        /// 错误信息
        /// </summary>
        public string Message { get; } = "";

        /// <summary>
        /// 处理方法
        /// </summary>
        public string ToDo { get; } = "";

        /// <summary>
        /// 处理方法
        /// </summary>
        public string HowToDo { get; } = "";

        /// <summary>
        /// 发生错误的时间
        /// </summary>
        public DateTime ErrTime { get; } = DateTime.Now;
    }

    /// <summary>
    /// MDC类
    /// </summary>
    public class Roders_Result
    {
        /// <summary>
        /// 状态
        /// </summary>
        public string State { get; }

        /// <summary>
        /// MDC文件路径
        /// </summary>
        public string File { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="State"></param>
        /// <param name="File"></param>
        public Roders_Result(string State, string File)
        {
            this.State = State;
            this.File = File;
        }
    }

    /// <summary>
    /// 罗德斯机床类
    /// </summary>
    public class Roders
    {
        #region 属性

        /// <summary>
        /// Socket通信最大接收字节
        /// </summary>
        private int BuffSize { get; } = 2048;

        /// <summary>
        /// 获取或设置通信编码方式
        /// </summary>
        public Encoding Encoding { get; set; } = System.Text.Encoding.GetEncoding("ISO-8859-1");

        /// <summary>
        /// 获取命令Socket本地终端
        /// </summary>
        public IPEndPoint CmdEndPoint { get { return CmdSocket != null ? (IPEndPoint)CmdSocket.LocalEndPoint : null; } }

        /// <summary>
        /// 获取当前机床的终端信息
        /// </summary>
        public IPEndPoint MachineEndPoint { get; }

        /// <summary>
        /// 命令Socket是否连接
        /// </summary>
        public bool CmdAlive
        {
            get
            {
                return this.CmdSocket.Connected;
            }
        }

        /// <summary>
        /// 状态Socket是否连接
        /// </summary>
        public bool StateAlive
        {
            get
            {
                return this.StateSocket.Connected;
            }
        }

        /// <summary>
        /// 获取命令Socket的IPAddress
        /// </summary>
        public IPAddress CmdAddress { get { return CmdEndPoint != null ? CmdEndPoint.Address : null; } }

        /// <summary>
        /// 获取命令Socket的端口号
        /// </summary>
        public int CmdPort { get { return CmdEndPoint != null ? CmdEndPoint.Port : -1; } }

        /// <summary>
        /// 获取当前机床命令Socket连接状态
        /// </summary>
        public bool CmdConnected { get { return CmdSocket != null ? CmdSocket.Connected : false; } }

        /// <summary>
        /// 获取当前机床状态Socket连接状态
        /// </summary>
        public bool StateConnected { get { return StateSocket != null ? StateSocket.Connected : false; } }

        /// <summary>
        /// 获取或设置通信数据报中的ID
        /// </summary>
        public int TelegramID { get; set; } = 3540;

        /// <summary>
        /// 获取当前命令版本信息
        /// </summary>
        public string Version { get; } = "1.00";

        /// <summary>
        /// 获取当前连接的机床名称
        /// </summary>
        public string MachineName { get; } = "";

        /// <summary>
        /// 获取当前连接机床的类型
        /// </summary>
        public string MachineType { get; } = "";

        /// <summary>
        /// 获取最后发送的命令
        /// </summary>
        public string LastCmd { get; private set; }

        /// <summary>
        /// 获取最后一次命令返回的信息内容
        /// </summary>
        public string LastAcknowledges { get; private set; }

        /// <summary>
        /// 获取最后一次返回的报告内容
        /// </summary>
        public string LastReportStr { get; private set; }

        /// <summary>
        /// 获取最后一次报告信息
        /// </summary>
        public StatuReport LastReport { get; private set; } = new StatuReport(null, "", false, false, false, false, false, false, false, 0, null, null, null, null, "NOT_INSTALLED", "NOT_INSTALLED");

        #endregion

        #region 变量

        /// <summary>
        /// 已和机床建立连接，并用于和单台机床命令通信的套接字
        /// </summary>
        private Socket CmdSocket = null;

        /// <summary>
        /// 已和机床建立连接，并用于和单台机床命令通信的套接字
        /// </summary>
        private Socket StateSocket = null;

        /// <summary>
        /// 接收状态报告标识
        /// </summary>
        private bool ReceiveFlag = true;

        /// <summary>
        /// 接收状态报告线程，需要不断地接收数据并解析
        /// </summary>
        private Thread ReceiveThread = null;

        #endregion

        #region 触发事件

        /// <summary>
        /// 状态端口接收事件
        /// </summary>
        /// <param name="Report"></param>
        public delegate void _ReportRecieved(string strReport, StatuReport Report, Roders Machine);

        /// <summary>
        /// 状态端口接收数据触发事件
        /// </summary>
        public _ReportRecieved ReportRecieved = null;

        /// <summary>
        /// 状态端口断开连接委托
        /// </summary>
        /// <param name="Machine"></param>
        public delegate void _MachineDisConnect(Roders Machine);

        /// <summary>
        /// 机床断开连接触发事件
        /// </summary>
        public _MachineDisConnect MachineDisConnect = null;

        /// <summary>
        /// 请求外部刀委托事件
        /// </summary>
        /// <param name="Machine"></param>
        /// <param name="ToolRequest"></param>
        public delegate void _ToolRequest(Roders Machine, Roders_ToolRequest ToolRequest);

        /// <summary>
        /// 请求外部刀触发事件
        /// </summary>
        public _ToolRequest ToolRequest = null;

        /// <summary>
        /// 发生错误委托事件
        /// </summary>
        /// <param name="Machine"></param>
        /// <param name="ErrMsg"></param>
        public delegate void _ErrorEvent(Roders Machine, Roders_ErrMsg[] ErrMsg);

        /// <summary>
        /// 发生错误触发事件
        /// </summary>
        public _ErrorEvent Error_Event = null;

        /// <summary>
        /// 加工结束事件
        /// </summary>
        /// <param name="Machine"></param>
        /// <param name="Result"></param>
        public delegate void _ResultEvent(Roders Machine, Roders_Result Result);

        /// <summary>
        /// 加工结束触发事件
        /// </summary>
        public _ResultEvent ResultEvent = null;

        #endregion

        #region 构造函数和析构函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="MachineName">机床名称</param>
        /// <param name="MachineType">机床类型</param>
        /// <param name="Cmd">本地和机床已连接的命令Socket</param>
        /// <param name="State">本地和机床已连接的状态Socket</param>
        /// <param name="TelegramID">本Socket通信连接的数据报ID</param>
        /// <param name="Version">数据报通信xml版本信息，默认为1.00</param>
        public Roders(string MachineName, string MachineType, Socket Cmd, Socket State, int TelegramID, string Version = "1.00")
        {
            this.MachineName = MachineName;
            this.MachineType = MachineType;
            this.CmdSocket = Cmd;
            this.TelegramID = TelegramID;
            this.Version = Version;
            this.StateSocket = State;
            this.CmdSocket.ReceiveTimeout = 500;//定义接收超时
            IPEndPoint endPoint = (IPEndPoint)Cmd.RemoteEndPoint;
            this.MachineEndPoint = new IPEndPoint(endPoint.Address, endPoint.Port);
            ReceiveThread = new Thread(() => {
                byte[] reportbuff = new byte[BuffSize];
                StateSocket.ReceiveTimeout = 0;
                while (ReceiveFlag)
                {
                    string document = "";
                    Header header = null;
                    Roders_ErrMsg[] ErrMsg = null;
                    bool Auto = false, AutoChange = false, Active = false, ECO = false, FileTransfer = false, NewmAintenance = false, NewPaldata = false;
                    int Override = 0;
                    string Handl_Interface = null, Handl_Mode = null, Handl_State = null, Errors = null, PalDoor = null, ToolDoor = null, cmd = null, ver = null, id = null;
                    Roders_ToolRequest toolRequest = null;
                    Roders_Result result = null;
                    try
                    {
                        try
                        {
                            if (this.StateSocket.Receive(reportbuff) <= 0)
                                throw new Exception("客户端已断开连接");
                        }
                        catch
                        {
                            this.CmdSocket.Close();//断开连接
                            this.StateSocket.Close();//断开连接
                            Thread t1 = new Thread(() => {
                                if (MachineDisConnect != null)
                                    MachineDisConnect.Invoke(this);//异步委托执行事件
                                MachineDisConnect = null;//清除事件
                            });
                            t1.Start();
                            break;
                        }
                        document = Encoding.GetString(reportbuff).TrimEnd('\0');//去掉结尾的空白和xml报的结尾符0x03
                        Array.Clear(reportbuff, 0, reportbuff.Length);//清空缓存
                        document = document.Replace((char)0x03, '\n');
                        if (document == "")
                            continue;
                        XmlDocument xml = new XmlDocument();
                        int index = document.IndexOf((char)0x03);
                        if (index > 0)//带有上一次遗留的报告内容
                            document = document.Substring(index + 1, document.Length - index);
                        this.LastReportStr = document;
                        xml.LoadXml(document);
                        Auto = Analyze_Xml_Bool(xml, "auto", 0);
                        AutoChange = Analyze_Xml_Bool(xml, "autochange", 0);
                        Active = Analyze_Xml_Bool(xml, "active", 0);
                        ECO = Analyze_Xml_Bool(xml, "eco", 0);
                        FileTransfer = Analyze_Xml_Bool(xml, "filetransfer", 0);
                        NewmAintenance = Analyze_Xml_Bool(xml, "newmaintenance", 0);
                        NewPaldata = Analyze_Xml_Bool(xml, "newpaldata", 0);
                        Override = Analyze_Xml_Int(xml, "override", 0, 0);
                        Handl_Interface = Analyze_Xml_String(xml, "handl_interface", 0, "OFF");
                        Handl_Mode = Analyze_Xml_String(xml, "handl_mode", 0, "DISCONNECTED");
                        Handl_State = Analyze_Xml_String(xml, "handl_state", 0, "IDLE");
                        XmlNodeList Nodes = xml.GetElementsByTagName("error");
                        if (Nodes.Count > 0)
                        {
                            ErrMsg = new Roders_ErrMsg[Nodes.Count];
                            int num = 0;
                            foreach (XmlNode node in Nodes)
                            {
                                string code = node.SelectSingleNode(".//num") == null ? "" : node.SelectSingleNode(".//num").InnerText;
                                string msg = node.SelectSingleNode(".//text") == null ? "" : node.SelectSingleNode(".//text").InnerText;
                                ErrMsg[num] = new Roders_ErrMsg(code, msg);
                                num++;
                            }
                        }
                        PalDoor = Analyze_Xml_String(xml, "paldoor", 0, "NOT_INSTALLED");
                        ToolDoor = Analyze_Xml_String(xml, "tooldoor", 0, "NOT_INSTALLED");
                        cmd = Analyze_Xml_String(xml, "cmd", 0);
                        ver = Analyze_Xml_String(xml, "ver", 0);
                        id = Analyze_Xml_String(xml, "id", 0);
                        header = new Header(cmd, ver, id);
                        toolRequest = Analyze_Xml_ToolReq(xml);
                        result = Analyze_Xml_Result(xml);
                    }
                    catch (Exception ex)
                    {
                        this.CmdSocket.Close();
                        this.CmdSocket.Dispose();
                        this.StateSocket.Close();
                        this.StateSocket.Dispose();
                        Thread t1 = new Thread(() => {
                            if (MachineDisConnect != null && ex.Message != "正在中止线程。")
                                MachineDisConnect.Invoke(this);
                            MachineDisConnect = null;
                        });
                        t1.Start();
                        break;
                    }
                    finally
                    {
                        LastReport = new StatuReport(header, DateTime.Now.ToString(), Auto, AutoChange, Active, ECO, FileTransfer, NewmAintenance, NewPaldata, Override, Handl_Interface, Handl_Mode, Handl_State, Errors, PalDoor, ToolDoor);
                        Thread t1 = new Thread(() => {
                            if (ReportRecieved != null)
                                ReportRecieved.Invoke(document, LastReport, this);
                        });
                        t1.Start();
                        Thread t2 = new Thread(() => {
                            if (ToolRequest != null && toolRequest != null)
                                ToolRequest.Invoke(this, toolRequest);
                        });
                        t2.Start();
                        Thread t3 = new Thread(() => {
                            if (Error_Event != null && ErrMsg != null)
                                Error_Event.Invoke(this, ErrMsg);
                        });
                        t3.Start();
                        Thread t4 = new Thread(() => {
                            if (ResultEvent != null && result != null)
                                ResultEvent.Invoke(this, result);
                        });
                        t4.Start();
                    }
                }
            });
            ReceiveThread.Start();
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~Roders()
        {
            ReceiveFlag = false;
        }

        /// <summary>
        /// 关闭所有连接
        /// </summary>
        public void Close()
        {
            //MachineDisConnect = null;//清除事件
            ResultEvent = null;//清除事件
            Error_Event = null;//清除事件
            ReportRecieved = null;//清除事件
            ToolRequest = null;//清除事件
            ReceiveFlag = false;//允许线程循环跳出
            CmdSocket.Close();
            CmdSocket.Dispose();
            StateSocket.Close();
            StateSocket.Dispose();
            try
            {
                ReceiveThread.Abort();
            }
            catch { }
        }

        #endregion

        #region 解析xml函数


        /// <summary>
        /// 获取指定节点的文本内容
        /// </summary>
        /// <param name="Xml">已实例化的XmlDocument对象</param>
        /// <param name="TagName">标签名称</param>
        /// <param name="Index">如果多个节点，选择指定下标节点</param>
        /// <param name="Default">默认值，如果节点不存在返回的值</param>
        /// <returns></returns>
        private string Analyze_Xml_String(XmlDocument Xml, string TagName, int Index, string Default = "")
        {
            string val = Default;
            try
            {
                XmlNodeList Nodes = Xml.GetElementsByTagName(TagName);
                val = Nodes[Index].InnerText;
            }
            catch { }
            return val;
        }

        /// <summary>
        /// 获取指定节点的内容并转换为对应的布尔值
        /// </summary>
        /// <param name="Xml">已实例化的XmlDocument对象</param>
        /// <param name="TagName">标签名称</param>
        /// <param name="Index">如果多个节点，选择指定下标节点</param>
        /// <param name="Default">默认值，如果节点不存在返回的值</param>
        /// <returns></returns>
        private bool Analyze_Xml_Bool(XmlDocument Xml, string TagName, int Index, bool Default = false)
        {
            bool val = Default;
            try
            {
                XmlNodeList Nodes = Xml.GetElementsByTagName(TagName);
                val = Nodes[Index].InnerText.ToUpper() == "TRUE" ? true : false;
            }
            catch { }
            return val;
        }

        /// <summary>
        /// 获取指定节点的内容并转换为对应的整型值
        /// </summary>
        /// <param name="Xml">已实例化的XmlDocument对象</param>
        /// <param name="TagName">标签名称</param>
        /// <param name="Index">如果多个节点，选择指定下标节点</param>
        /// <param name="Default">默认值</param>
        /// <returns></returns>
        private int Analyze_Xml_Int(XmlDocument Xml, string TagName, int Index, int Default = -1)
        {
            int val = Default;
            try
            {
                XmlNodeList Nodes = Xml.GetElementsByTagName(TagName);
                val = Convert.ToInt32(Nodes[Index].InnerText);
            }
            catch { }
            return val;
        }

        /// <summary>
        /// 获取指定节点的内容并转换为对应的整型值
        /// </summary>
        /// <param name="Xml"></param>
        /// <param name="TagName"></param>
        /// <param name="Index"></param>
        /// <param name="Default"></param>
        /// <returns></returns>
        private double Analyze_Xml_Double(XmlDocument Xml, string TagName, int Index, double Default = 0)
        {
            double val = Default;
            try
            {
                XmlNodeList Nodes = Xml.GetElementsByTagName(TagName);
                val = Convert.ToDouble(Nodes[Index].InnerText);
            }
            catch { }
            return val;
        }

        /// <summary>
        /// 解析请求外部刀具信息
        /// </summary>
        /// <param name="Xml"></param>
        /// <returns></returns>
        private Roders_ToolRequest Analyze_Xml_ToolReq(XmlDocument Xml)
        {
            XmlNodeList nodeList = Xml.GetElementsByTagName("toolchangereq");
            if (nodeList.Count <= 0)
                return null;
            try
            {
                int MagposGet = Convert.ToInt32(nodeList[0].SelectSingleNode(".//magposget").InnerText);
                int MagposPut = Convert.ToInt32(nodeList[0].SelectSingleNode(".//magposput").InnerText);
                int InternalPos = Convert.ToInt32(nodeList[0].SelectSingleNode(".//internalpos").InnerText);
                return new Roders_ToolRequest(MagposGet, MagposPut, InternalPos);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析Result
        /// </summary>
        /// <param name="Xml"></param>
        /// <returns></returns>
        private Roders_Result Analyze_Xml_Result(XmlDocument Xml)
        {
            XmlNodeList nodeList = Xml.GetElementsByTagName("result");
            if (nodeList.Count <= 0)
                return null;
            try
            {
                string state = nodeList[0].SelectSingleNode(".//state").InnerText;
                string file = nodeList[0].SelectSingleNode(".//file").InnerText;
                return new Roders_Result(state, file);
            }
            catch
            {
                return null;
            }
        }



        #endregion

        #region 命令函数


        /// <summary>
        /// 命令socket发送命令函数
        /// </summary>
        /// <param name="Cmd"></param>
        /// <returns></returns>
        public string Cmd_Send(string[] Cmd)
        {
            lock (this)
            {
                foreach (string c in Cmd)
                {
                    LastCmd = c;
                    byte[] cmd = Encoding.GetBytes(LastCmd);
                    byte[] sendCmd = new byte[cmd.Length + 1];
                    cmd.CopyTo(sendCmd, 0);
                    sendCmd[sendCmd.Length - 1] = 0x03;
                    this.CmdSocket.Send(sendCmd);
                }
                byte[] buff = new byte[BuffSize];
                this.CmdSocket.Receive(buff);
                LastAcknowledges = Encoding.GetString(buff).TrimEnd('\0').TrimEnd((char)0x03);
                return LastAcknowledges;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <returns></returns>
        private Rec_DIS Send_DIS()
        {
            Header Header = null;
            string ret = "NOK";
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_DIS(TelegramID, Version) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
            }
            catch { }
            return new Rec_DIS(ret, Header);
        }

        /// <summary>
        /// 发送看门狗命令，状态报告端口将接收新的报告
        /// 当超过30秒无命令发送，机床会自动断开，本命令必须定时发送
        /// </summary>
        /// <returns></returns>
        public Rec_WDREQ Send_WDREQ()
        {
            Header Header = null;
            string ret = "NOK";
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_WDREQ(TelegramID, Version) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
            }
            catch { }
            return new Rec_WDREQ(ret, Header);
        }

        /// <summary>
        /// 获取机床所有托盘信息
        /// </summary>
        /// <returns></returns>
        public Rec_PTREQ Send_PTREQ()
        {
            Header Header = null;
            string ret = "NOK";
            PtreqData_Chuck[] chucks = null;
            Place[] places = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_PTREQ(TelegramID, Version) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                XmlNodeList list_chuck = xml.GetElementsByTagName("chuck");
                XmlNodeList list_place = xml.GetElementsByTagName("place");
                int cnt = 0;
                if (list_chuck.Count > 0)
                {
                    chucks = new PtreqData_Chuck[list_chuck.Count];
                    foreach (XmlNode node in list_chuck)
                    {
                        string mac = node.SelectSingleNode(".//mac") == null ? "" : node.SelectSingleNode(".//mac").InnerText;
                        string pos = node.SelectSingleNode(".//pos") == null ? "" : node.SelectSingleNode(".//pos").InnerText;
                        string state = node.SelectSingleNode(".//state") == null ? "" : node.SelectSingleNode(".//state").InnerText;
                        string pos_org = node.SelectSingleNode(".//pos_org") == null ? "" : node.SelectSingleNode(".//pos_org").InnerText;
                        string chipid = node.SelectSingleNode(".//id") == null ? "" : node.SelectSingleNode(".//id").InnerText;
                        chucks[cnt] = new PtreqData_Chuck(mac, pos, state, pos_org, chipid);
                        cnt++;
                    }
                }
                if (list_place.Count > 0)
                {
                    cnt = 0;
                    places = new Place[list_place.Count];
                    foreach (XmlNode node in list_place)
                    {
                        string pos = node.SelectSingleNode(".//pos") == null ? "" : node.SelectSingleNode(".//pos").InnerText;
                        string state = node.SelectSingleNode(".//state") == null ? "" : node.SelectSingleNode(".//state").InnerText;
                        string chipid = node.SelectSingleNode(".//id") == null ? "" : node.SelectSingleNode(".//id").InnerText;
                        string type = node.SelectSingleNode(".//type") == null ? "" : node.SelectSingleNode(".//type").InnerText;
                        string objecttype = node.SelectSingleNode(".//objecttype") == null ? "" : node.SelectSingleNode(".//objecttype").InnerText;
                        string toolstate = node.SelectSingleNode(".//toolstate") == null ? "" : node.SelectSingleNode(".//toolstate").InnerText;
                        string length = node.SelectSingleNode(".//length") == null ? "" : node.SelectSingleNode(".//length").InnerText;
                        string diameter = node.SelectSingleNode(".//diameter") == null ? "" : node.SelectSingleNode(".//diameter").InnerText;
                        places[cnt] = new Place(pos, state, chipid, type, objecttype, toolstate, length, diameter);
                        cnt++;
                    }
                }
            }
            catch { }
            return new Rec_PTREQ(ret, Header, chucks, places);
        }

        /// <summary>
        /// 获取指定托盘信息，active模式为false有效
        /// </summary>
        /// <param name="chuck">chuck number</param>
        /// <returns></returns>
        public Rec_COREQ Send_COREQ(int chuck)
        {
            Header Header = null;
            string ret = "NOK";
            string chuckname = "";
            Offset offset = null;
            Error error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_COREQ(TelegramID, Version, chuck) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    //获取错误信息
                    error = new Error(xml.GetElementsByTagName("errnum")[0].InnerText, xml.GetElementsByTagName("error")[0].InnerText);
                }
                else
                {
                    chuckname = xml.GetElementsByTagName("chuck")[0].InnerText;
                    XmlNode off = xml.GetElementsByTagName("offset")[0];
                    double x = Convert.ToDouble(off.SelectSingleNode(".//x").InnerText);
                    double y = Convert.ToDouble(off.SelectSingleNode(".//y").InnerText);
                    double z = Convert.ToDouble(off.SelectSingleNode(".//z").InnerText);
                    double c = Convert.ToDouble(off.SelectSingleNode(".//c").InnerText);
                    offset = new Offset(x, y, z, c);
                }
            }
            catch { }
            return new Rec_COREQ(ret, Header, chuckname, offset, error);
        }

        /// <summary>
        /// 
        /// </summary>
        public enum TTREQ_Magazine
        {
            INTERNAL,
            EXTERNAL
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="magazine">RMS6: INTERNAL, EXTERNAL</param>
        /// <returns></returns>
        public Rec_TTREQ Send_TTREQ(TTREQ_Magazine magazine)
        {
            Header Header = null;
            string ret = "NOK";
            Place[] places = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_TTREQ(TelegramID, Version, magazine) });
                //解析反馈
                if (xmlDocument != null)
                {
                    Thread.Sleep(3000);
                    XmlDocument xml = new XmlDocument();
                    xml.LoadXml(xmlDocument);
                    string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                    string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                    string id = xml.GetElementsByTagName("id")[0].InnerText;
                    ret = xml.GetElementsByTagName("ret")[0].InnerText;
                    //添加header节点
                    Header = new Header(ack, ver, id);
                    XmlNodeList places_list = xml.GetElementsByTagName("place");
                    if (places_list.Count > 0)
                    {
                        places = new Place[places_list.Count];
                        int cnt = 0;
                        foreach (XmlNode node in places_list)
                        {
                            string pos = node.SelectSingleNode(".//pos") == null ? "" : node.SelectSingleNode(".//pos").InnerText;
                            string state = node.SelectSingleNode(".//state") == null ? "" : node.SelectSingleNode(".//state").InnerText;
                            string chipid = node.SelectSingleNode(".//id") == null ? "" : node.SelectSingleNode(".//id").InnerText;
                            string type = node.SelectSingleNode(".//type") == null ? "" : node.SelectSingleNode(".//type").InnerText;
                            string objecttype = node.SelectSingleNode(".//objecttype") == null ? "" : node.SelectSingleNode(".//objecttype").InnerText;
                            string toolstate = node.SelectSingleNode(".//toolstate") == null ? "" : node.SelectSingleNode(".//toolstate").InnerText;
                            string length = node.SelectSingleNode(".//length") == null ? "" : node.SelectSingleNode(".//length").InnerText;
                            string diameter = node.SelectSingleNode(".//diameter") == null ? "" : node.SelectSingleNode(".//diameter").InnerText;
                            places[cnt++] = new Place(pos, state, chipid, type, objecttype, toolstate, length, diameter);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string[] errorposmes = ex.StackTrace.Split(':');
                if (errorposmes.Length > 1)
                { }
            }
            return new Rec_TTREQ(ret, Header, places);
        }

        public Rec_TTREQ Send_TTREQ2(TTREQ_Magazine magazine)
        {
            Header Header = null;
            string ret = "NOK";
            Place[] places = null;
            try
            {
                // 尝试发送命令并解析，最多重试 2 次
                int retryCount = 0;
                bool success = false;
                while (retryCount < 2 && !success)
                {
                    // 发送命令获取数据
                    string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_TTREQ(TelegramID, Version, magazine) });

                    if (xmlDocument == null)
                    {
                        retryCount++;
                        continue; // 数据为空则重试
                    }


                    // 延长等待时间（首次 3 秒，重试 5 秒）
                    int waitTime = retryCount == 0 ? 3000 : 5000;
                    Thread.Sleep(waitTime);

                    try
                    {
                        // 验证 XML 有效性（关键修复）
                        if (!IsValidXml(xmlDocument))
                        {
                            throw new Exception("Invalid XML structure");
                        }

                        XmlDocument xml = new XmlDocument();
                        xml.LoadXml(xmlDocument);

                        // 解析逻辑（保持不变）
                        string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                        string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                        string id = xml.GetElementsByTagName("id")[0].InnerText;
                        ret = xml.GetElementsByTagName("ret")[0].InnerText;
                        Header = new Header(ack, ver, id);

                        XmlNodeList places_list = xml.GetElementsByTagName("place");
                        if (places_list.Count > 0)
                        {
                            places = new Place[places_list.Count];
                            int cnt = 0;
                            foreach (XmlNode node in places_list)
                            {
                                string pos = node.SelectSingleNode(".//pos")?.InnerText ?? "";
                                string state = node.SelectSingleNode(".//state")?.InnerText ?? "";
                                string chipid = node.SelectSingleNode(".//id")?.InnerText ?? "";
                                string type = node.SelectSingleNode(".//type")?.InnerText ?? "";
                                string objecttype = node.SelectSingleNode(".//objecttype")?.InnerText ?? "";
                                string toolstate = node.SelectSingleNode(".//toolstate")?.InnerText ?? "";
                                string length = node.SelectSingleNode(".//length")?.InnerText ?? "";
                                string diameter = node.SelectSingleNode(".//diameter")?.InnerText ?? "";
                                places[cnt++] = new Place(pos, state, chipid, type, objecttype, toolstate, length, diameter);
                            }
                        }
                        success = true; // 解析成功
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        // 记录详细错误（保留原错误日志格式）
                        string[] errorposmes = ex.StackTrace.Split(':');
                        if (errorposmes.Length > 1)
                        { }
                    }
                }

                // 两次重试均失败
                if (!success)
                {
                    ret = "NOK"; // 保持默认错误状态
                }
            }
            catch (Exception ex)
            {
                string[] errorposmes = ex.StackTrace.Split(':');
                if (errorposmes.Length > 1)
                { }
                    //mould.LvMessage = "sendttreq" + "," + ex.Message + "," + errorposmes[errorposmes.Length - 1] + ',' + errorposmes[errorposmes.Length - 2];
            }
            return new Rec_TTREQ(ret, Header, places);
        }

        private bool IsValidXml(string xml)
        {
            try
            {
                using (var reader = new StringReader(xml))
                {
                    var xmlReader = XmlReader.Create(reader, new XmlReaderSettings { CheckCharacters = true });
                    while (xmlReader.Read()) { }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Pallets"></param>
        /// <returns></returns>
        public Rec_PLREP Send_PLREP(Pallet[] Pallets)
        {
            Header Header = null;
            string ret = "NOK";
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_PLREP(TelegramID, Version, Pallets) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
            }
            catch { }
            return new Rec_PLREP(ret, Header);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Chucks"></param>
        /// <param name="Places"></param>
        /// <returns></returns>
        public Rec_PTREP Send_PTREP(Chuck[] Chucks, Places[] Places)
        {
            Header Header = null;
            string ret = "NOK";
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_PTREP(TelegramID, Version, Chucks, Places) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
            }
            catch { }
            return new Rec_PTREP(ret, Header);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Places">Place数据包含：pos、state、id</param>
        /// <returns></returns>
        public Rec_TTREP Send_TTREP(Place[] Places)
        {
            Header Header = null;
            string ret = "NOK";
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.GetTTREP(TelegramID, Version, Places) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
            }
            catch { }
            return new Rec_TTREP(ret, Header);
        }

        /// <summary>
        /// 发送启动加工命令，auto模式为true，active模式为false有效
        /// </summary>
        /// <param name="File"></param>
        /// <returns></returns>
        public Rec_RUN Send_Run(string File)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_RUN(TelegramID, Version, File) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                    //mould.LvMessage = Error;
                }
            }
            catch (Exception ex)
            {
               // mould.LvMessage = ex.Message;
            }
           // mould.LvMessage = ret + "," + Header + "," + Error;
            return new Rec_RUN(ret, Header, Error);
        }

        /// <summary>
        /// ECO命令枚举
        /// </summary>
        public enum Eco_State
        {
            MOFF,// MOFF: request eco-mode
            MON  // MON: request automatic-on
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="state">执行命令状态枚举</param>
        /// <returns></returns>
        public Rec_ECO Send_ECO(Eco_State state)
        {
            Header Header = null;
            string ret = "NOK";
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_ECO(TelegramID, Version, state) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                //mould.LvMessage = ret;
            }
            catch { }
            return new Rec_ECO(ret, Header);
        }

        /// <summary>
        /// 获取NC文件
        /// </summary>
        /// <param name="source">rel. path and name of destination file name</param>
        /// <param name="dest">UNC-path with source file name</param>
        /// <returns></returns>
        public Rec_NCGET Send_NCGET(string Source, string Dest)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_NCGET(TelegramID, Version, Source, Dest) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                }
            }
            catch { }
            return new Rec_NCGET(ret, Header, Error);
        }

        /// <summary>
        /// 删除NC文件
        /// </summary>
        /// <param name="dest">rel. path and name of destination file name</param>
        /// <returns></returns>
        public Rec_NCDEL Send_NCDEL(string Dest)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_NCDEL(TelegramID, Version, Dest) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                }
            }
            catch { }
            return new Rec_NCDEL(ret, Header, Error);
        }

        /// <summary>
        /// 获取NC文件列表,该命令仅在auto模式为true，filetransfer为false下有效
        /// </summary>
        /// <returns></returns>
        public Rec_NCLIST Send_NCLIST()
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            List<string> Files = new List<string> { };
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_NCLIST(TelegramID, Version) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                }
                else
                {
                    XmlNodeList file_list = xml.GetElementsByTagName("dest");
                    foreach (XmlNode node in file_list)
                    {
                        Files.Add(node.InnerText);
                    }
                }
            }
            catch { }
            return new Rec_NCLIST(ret, Header, Files, Error);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="chuck">opt.: pallet chuck number for positioning</param>
        /// <param name="spindle">opt.: spindle chuck number for positioning,only one position is allowed, either chuck or spindle</param>
        /// <returns></returns>
        public Rec_CHANGEPOS Send_CHANGEPOS(int FirstCmd_Chuck, int FirstCmd_Spindle, CHANGEPOS_State SecondCmd_State)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(Roders_Cmd.Get_CHANGEPOS(TelegramID, Version, FirstCmd_Chuck, FirstCmd_Spindle, SecondCmd_State));
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                    //mould.LvMessage = Error;
                }
                //mould.LvMessage = ret + "," + Header;
            }
            catch { }
            return new Rec_CHANGEPOS(ret, Header);
        }

        /// <summary>
        /// 打开机床侧门命令，前提：Auto自动模式为true，机床处于非加工状态Active为false，PalDoor侧门处于CLOSED状态
        /// </summary>
        /// <param name="Chuck">chuck number for positioning (default: 1)</param>
        /// <returns></returns>
        public Rec_PDOPEN Send_PDOPEN(int Chuck = 1)
        {

            Header Header = null;
            string ret = "NOK";
            string Error = "";
            bool flag = true;
            if (LastReport == null)
            {
                Error = "自动模式未开启；机床正在加工；侧门处于非CLOSED状态；";
                //mould.LvMessage = Error;
                flag = false;
            }
            else
            {
                if (!LastReport.Auto)//必须保证auto为true
                {
                    Error += "自动模式未开启，请开启；";
                    //mould.LvMessage = Error;
                    flag = false;
                }
                if (LastReport.Active)//必须保证active为false
                {
                    Error += "机床正在加工，请停止加工；";
                    //mould.LvMessage = Error;
                    flag = false;
                }
                if (LastReport.PalDoor != "CLOSED")//必须保证paldoor为CLOSED状态
                {
                    Error += "侧门处于非CLOSED状态：" + LastReport.PalDoor + "；";
                    //mould.LvMessage = Error;
                    flag = false;
                }
            }
            if (flag)
            {
                try
                {
                    //发送命令并获取反馈内容
                    string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_PDOPEN(TelegramID, Version, Chuck) });
                    //解析反馈
                    XmlDocument xml = new XmlDocument();
                    xml.LoadXml(xmlDocument);
                    string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                    string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                    string id = xml.GetElementsByTagName("id")[0].InnerText;
                    ret = xml.GetElementsByTagName("ret")[0].InnerText;
                    //添加header节点
                    Header = new Header(ack, ver, id);
                    if (ret == "NOK")
                    {
                        Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                        //mould.LvMessage = Error;
                    }
                }
                catch { }
                //mould.LvMessage = ret + "," + Header + "," + Error;
            }
            return new Rec_PDOPEN(ret, Header, Error);
        }

        /// <summary>
        /// 关闭机床侧门命令，前提：Auto自动模式为true，机床处于非加工状态Active为false，PalDoor侧门处于OPEN状态
        /// </summary>
        /// <returns></returns>
        public Rec_PDCLOSE Send_PDCLOSE()
        {
            Header Header = null;
            string ret = "NOK";
            string Error = "";
            bool flag = true;
            if (LastReport == null)
            {
                Error = "自动模式未开启；机床正在加工；侧门未处于关闭状态";
                flag = false;
            }
            else
            {
                if (!LastReport.Auto)//必须保证auto为true
                {
                    Error += "自动模式未开启，请开启；";
                    //mould.LvMessage = Error;
                    flag = false;
                }
                if (LastReport.Active)//必须保证active为fasle
                {
                    Error += "机床正在加工，请停止加工；";
                    //mould.LvMessage = Error;
                    flag = false;
                }
                if (LastReport.PalDoor != "OPEN")//必须保证paldoor为OPEN状态
                {
                    Error += "侧门处于非OPEN状态：" + LastReport.PalDoor + "；";
                    //mould.LvMessage = Error;
                    flag = false;
                }
            }
            if (flag)
            {
                try
                {
                    //发送命令并获取反馈内容
                    string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_PDCLOSE(TelegramID, Version) });
                    //解析反馈
                    XmlDocument xml = new XmlDocument();
                    xml.LoadXml(xmlDocument);
                    string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                    string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                    string id = xml.GetElementsByTagName("id")[0].InnerText;
                    ret = xml.GetElementsByTagName("ret")[0].InnerText;
                    //添加header节点
                    Header = new Header(ack, ver, id);
                    if (ret == "NOK")
                    {
                        Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                        //mould.LvMessage = Error;
                    }
                }
                catch { }
                //mould.LvMessage = ret + "," + Header + "," + Error;
            }
            return new Rec_PDCLOSE(ret, Header, Error);
        }

        /// <summary>
        /// 打开刀具门命令，前提：ToolDoor状态为CLOSED，不区分内外刀情况，请在Machine.ToolRequest触发事件中调用
        /// </summary>
        /// <returns></returns>
        public Rec_TDOPEN Send_TDOPEN()
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            bool flag = true;
            if (LastReport == null)
            {
                Error = "机床侧门处于非CLOSED状态";
                flag = false;
            }
            else
            {
                if (LastReport.ToolDoor != "CLOSED")
                {
                    Error = "机床侧门处于非CLOSED状态：" + LastReport.ToolDoor;
                    flag = false;
                }
            }
            if (flag)
            {
                try
                {
                    //发送命令并获取反馈内容
                    string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_TDOPEN(TelegramID, Version) });
                    //解析反馈
                    XmlDocument xml = new XmlDocument();
                    xml.LoadXml(xmlDocument);
                    string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                    string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                    string id = xml.GetElementsByTagName("id")[0].InnerText;
                    ret = xml.GetElementsByTagName("ret")[0].InnerText;
                    //添加header节点
                    Header = new Header(ack, ver, id);
                    if (ret == "NOK")
                    {
                        Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                    }
                }
                catch { }
            }
            return new Rec_TDOPEN(ret, Header, Error);
        }

        /// <summary>
        /// 加载状态
        /// </summary>
        public enum State
        {
            LOCKED,
            EMPTY,
            FULL
        }

        /// <summary>
        /// SubChuck
        /// </summary>
        public enum SubChuck
        {
            sc_0 = 0,
            sc_90 = 90,
            sc_180 = 180,
            sc_270 = 270
        }

        /// <summary>
        /// Zdir
        /// </summary>
        public enum Zdir
        {
            z_0 = 0,
            z_90 = 90
        }

        /// <summary>
        /// Zrot
        /// </summary>
        public enum Zrot
        {
            z_0 = 0,
            z_90 = 90,
            z_180 = 180,
            z_270 = 270
        }

        /// <summary>
        ///刀具门关闭状态枚举
        /// </summary>
        public enum TDCLOSE_State
        {
            INCOMPLETE,
            COMPLETE,
        }



        /// <summary>
        /// CHANGEPOS命令改变状态枚举
        /// </summary>
        public enum CHANGEPOS_State
        {
            FINISHED,
            ERROR
        }


        /// <summary>
        /// 
        /// </summary>
        public enum CHANGE_ObjType
        {
            PAL,
            TOOL
        }

        /// <summary>
        /// 关闭刀具门命令，前提：ToolDoor状态为OPEN，不区分内外刀情况，请在调用Send_TDOPEN并处理换刀后调用该函数
        /// </summary>
        /// <param name="state">action state: COMPLETE, INCOMPLETE</param>
        /// <returns></returns>
        public Rec_PDCLOSE Send_TDCLOSE(TDCLOSE_State State)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            bool flag = true;
            if (LastReport == null)
            {
                Error = "机床侧门处于非OPEN状态";
                flag = false;
            }
            else
            {
                if (LastReport.ToolDoor != "OPEN")
                {
                    Error = "机床侧门处于非OPEN状态：" + LastReport.ToolDoor;
                    flag = false;
                }
            }
            if (flag)
            {
                try
                {
                    //发送命令并获取反馈内容
                    string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_TDCLOSE(TelegramID, Version, State) });
                    //解析反馈
                    XmlDocument xml = new XmlDocument();
                    xml.LoadXml(xmlDocument);
                    string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                    string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                    string id = xml.GetElementsByTagName("id")[0].InnerText;
                    ret = xml.GetElementsByTagName("ret")[0].InnerText;
                    //添加header节点
                    Header = new Header(ack, ver, id);
                    if (ret == "NOK")
                    {
                        Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                    }
                }
                catch { }
            }
            return new Rec_PDCLOSE(ret, Header, Error);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tooltype">tooltype name</param>
        /// <returns></returns>
        public Rec_TTLA Send_TTLA(string ToolType)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_TTLA(TelegramID, Version, ToolType) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                }
            }
            catch { }
            return new Rec_TTLA(ret, Header, Error);
        }

        //参数较多
        public Rec_CHANGE Send_CHANGE(CHANGE_ObjType ObjectType, int Magget, int Magput, int Macget, int Chuckget, SubChuck SubChuckget, int CleanCycles, int Macput, int Chuckput, SubChuck SubChuckput)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_CHANGE(TelegramID, Version, ObjectType, Magget, Magput, Macget, Chuckget, SubChuckget, CleanCycles, Macput, Chuckput, SubChuckput) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                }
            }
            catch { }
            return new Rec_CHANGE(ret, Header, Error);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="SetLook"></param>
        /// <returns></returns>
        public Rec_LOCK Send_LOCK(bool SetLook)
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_LOCK(TelegramID, Version, SetLook) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                }
            }
            catch { }
            return new Rec_LOCK(ret, Header, Error);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Rec_ABORT Send_ABORT()
        {
            Header Header = null;
            string ret = "NOK";
            string Error = null;
            try
            {
                //发送命令并获取反馈内容
                string xmlDocument = Cmd_Send(new string[] { Roders_Cmd.Get_ABORT(TelegramID, Version) });
                //解析反馈
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(xmlDocument);
                string ack = xml.GetElementsByTagName("ack")[0].InnerText;
                string ver = xml.GetElementsByTagName("ver")[0].InnerText;
                string id = xml.GetElementsByTagName("id")[0].InnerText;
                ret = xml.GetElementsByTagName("ret")[0].InnerText;
                //添加header节点
                Header = new Header(ack, ver, id);
                if (ret == "NOK")
                {
                    Error = xml.GetElementsByTagName("error")[0].InnerText;//获取错误信息
                }
            }
            catch { }
            return new Rec_ABORT(ret, Header, Error);
        }

        #endregion
    }
}
