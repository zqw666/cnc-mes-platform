using Opc.Ua;
using Siemens.OpcUA;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinSCP;

namespace MacCommunicate
{
    public class Siemensoperate
    {
        #region 数据类

        /// <summary>
        /// 机床坐标
        /// </summary>
        public class Position
        {
            /// <summary>
            /// 获取x轴坐标
            /// </summary>
            public string x { get; }

            /// <summary>
            /// 获取y轴坐标
            /// </summary>
            public string y { get; }

            /// <summary>
            /// 获取z轴坐标
            /// </summary>
            public string z { get; }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="x">x轴坐标</param>
            /// <param name="y">y轴坐标</param>
            /// <param name="z">z轴坐标</param>
            public Position(string x, string y, string z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }

        /// <summary>
        /// 机床状态
        /// </summary>
        public class MachineStatus
        {
            public int tooltype { get; }

            /// <summary>
            /// 机床状态枚举值：0未连接、1中断、2停止、3运行、4等待、5完成
            /// </summary>
            public int Stus { get; }

            /// <summary>
            /// 机床状态
            /// </summary>
            public string Description { get; }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Stus">机床状态枚举值</param>
            /// <param name="Description">机床状态</param>
            public MachineStatus(int Stus, string Description)
            {
                this.Stus = Stus;
                this.Description = Description;
            }
        }


        #endregion

        /// <summary>
        /// 用于连接机床的IP地址
        /// </summary>
        public string Michine_IP { get; } = "";

        /// <summary>
        /// 用于连接机床登录的用户名
        /// </summary>
        public string Michine_UserName { get; } = "";
        /// <summary>
        /// 用于连接机床登录的用户密码
        /// </summary>
        public string Michine_PassWord { get; } = "";

        /// <summary>
        /// 连接机床的WinScp密钥
        /// </summary>
        public string WinScp_HostKey { get; }

        /// <summary>
        /// 获取当前机床连接状态
        /// </summary>
        public bool Connected { get; private set; } = false;

        /// <summary>
        /// 获取通讯连接状态
        /// </summary>
        public bool SessionConnected { get { return this.Session.Opened; } }

        /// <summary>
        /// 获取或设置当前机床加工程序的总行数
        /// </summary>
        public int AllLine { get; set; } = 0;

        /// <summary>
        /// 获取或设置当前机床加工的行数
        /// </summary>
        public int NowLine
        {
            get
            {
                if (!Connected)
                    return 0;
                try
                {
                    return Convert.ToInt32(Read("/Channel/ProgramInfo/actLineNumber"));
                }
                catch { return 0; }

            }
        }

        /// <summary>
        /// 获取当前机床加工剩余行数
        /// </summary>
        public int OverLine
        {
            get
            {
                if (AllLine <= 0)
                    return 0;
                return this.AllLine - this.NowLine;
            }
        }

        /// <summary>
        /// 获取当前加工进度
        /// </summary>
        public float PressBar
        {
            get
            {
                if (AllLine <= 0)
                    return 0;
                return (float)(this.NowLine * 100.0 / this.AllLine);
            }
        }

        /// <summary>
        /// 根据获取的字符串状态转换为指定的机床状态
        /// </summary>
        /// <param name="Stu">当前机床状态代号</param>
        /// <returns></returns>
        private MachineStatus Get_Stu(string Stu)
        {
            switch (Stu)
            {
                case "0": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "未连接");
                case "1": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "中断");
                case "2": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "停止");
                case "3": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "运行");
                case "4": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "等待");
                case "5": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "完成");
                case "-1": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "连接失败");
                default: return new Siemensoperate.MachineStatus(-2, "未知");
            }
        }

        /// <summary>
        /// 转换机床状态
        /// </summary>
        /// <param name="Stu">当前机床状态代号</param>
        /// <returns></returns>
        public static MachineStatus Get_MachineStu(string Stu)
        {
            switch (Stu)
            {
                case "0": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "未连接");
                case "1": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "中断");
                case "2": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "停止");
                case "3": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "运行");
                case "4": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "等待");
                case "5": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "完成");
                case "-1": return new Siemensoperate.MachineStatus(Convert.ToInt32(Stu), "连接失败");
                default: return new Siemensoperate.MachineStatus(-2, "未知");
            }
        }

        /// <summary>
        /// 获取当前机床状态
        /// </summary>
        public MachineStatus Status
        {
            get
            {
                if (Connected)
                {
                    try
                    {
                        return Get_Stu(Read("/Channel/State/progStatus"));
                    }
                    catch (Exception ex)
                    { return Get_Stu("0"); }
                }
                else
                    return Get_Stu("0");//返回未连接状态
            }
        }

        #region 西门子机床连接属性

        /// <summary>
        /// 西门子机床
        /// </summary>
        private Server Server = new Server();

        /// <summary>
        /// 连接机床的命名空间
        /// </summary>
        private ushort m_NameSpaceIndex = 0;

        /// <summary>
        /// 
        /// </summary>
        private Siemens.OpcUA.Subscription Subscription = null;

        #endregion

        #region 西门子机床服务器类事件

        void m_Server_subscriptionInfo(string message)
        {
        }

        void m_Server_CertificateEvent(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            e.Accept = true;
        }

        #endregion

        #region 机床内程序操作相关

        /// <summary>
        /// 和机床通信，读取程序存储卡
        /// </summary>
        private Session Session = new Session();

        #endregion

        /// <summary>
        /// 获取本地程序文件的行数
        /// </summary>
        /// <param name="LocalFile">本地程序文件的完整路径</param>
        /// <returns></returns>
        public int Get_Program_Line(string LocalFile)
        {
            if (!File.Exists(LocalFile))
                return 0;
            StreamReader sr = new StreamReader(LocalFile);
            string dat = sr.ReadToEnd().Trim();
            sr.Dispose();//释放使用的资源
            sr.Close();//关闭
            return dat.Split('\n').Length;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="IP">连接机床的IP地址</param>
        /// <param name="UserName">连接机床的登录用户名</param>
        /// <param name="PassWord">连接机床的登录用户密码</param>
        /// <param name="WinScpHostKey">连接机床的WinScp密钥</param>
        public Siemensoperate(string IP, string UserName, string PassWord, string WinScpHostKey)
        {
            Michine_IP = IP;//设置连接机床的IP
            Michine_UserName = UserName;//设置连接机床的登录用户名
            Michine_PassWord = PassWord;//设置连接机床的登录用户密码
            WinScp_HostKey = WinScpHostKey;//连接机床的WinScp密钥
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~Siemensoperate()
        {
            try
            {
                Server.Disconnect();
                Session.Close();
            }
            catch { }
        }

        /// <summary>
        /// 连接机床函数
        /// </summary>
        /// <returns></returns>
        public bool Connect()
        {
            if (Connected)
                return true;
            try
            {
                Server.CertificateEvent -= new certificateValidation(m_Server_CertificateEvent);
                Server.subscriptionInfo -= new Server.SubscriptionInfo(m_Server_subscriptionInfo);
                Server.CertificateEvent += new certificateValidation(m_Server_CertificateEvent);
                Server.subscriptionInfo += new Server.SubscriptionInfo(m_Server_subscriptionInfo);
                string url = "opc.tcp://" + Michine_IP + ":4840";
                Server.Connect(url, Michine_UserName, Michine_PassWord);

                NodeIdCollection nodesToRead = new NodeIdCollection();
                DataValueCollection results;
                nodesToRead.Add(Variables.Server_NamespaceArray);
                Server.ReadValues(nodesToRead, out results);
                string[] nameSpaceArray = (string[])results[0].Value;
                if (results[0].StatusCode.ToString() == "Good")
                {
                    ushort i;
                    //读取空间变量
                    for (i = 0; i < nameSpaceArray.Length; i++)
                    {
                        if (nameSpaceArray[i].ToString() == "SinumerikVarProvider")
                        {
                            m_NameSpaceIndex = i;
                            SessionOptions sess = new SessionOptions
                            {
                                Protocol = Protocol.Scp,
                                HostName = Michine_IP,
                                UserName = "user",
                                Password = "CUSTOMER",
                                SshHostKeyFingerprint = WinScp_HostKey,
                            };
                            Session.Open(sess);//该函数连接的时间较长
                            if (Session.Opened == true)
                            {
                                Connected = true;
                                Subscription = Server.AddSubscription(300);
                            }
                        }
                    }
                }
            }
            catch { Connected = false; }
            return Connected;//返回当前的机床连接状态
        }

        /// <summary>
        /// 断开机床连接
        /// </summary>
        public void Close()
        {
            Server.Disconnect();
            Server.CertificateEvent -= new certificateValidation(m_Server_CertificateEvent);
            Server.subscriptionInfo -= new Server.SubscriptionInfo(m_Server_subscriptionInfo);
            try
            {
                Session.Close();
            }
            catch { }
            Connected = false;
        }

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="Cmd"></param>
        /// <returns></returns>
        public string Read(string Command)
        {
            lock (this)
            {
                Server = new Server();
                Server.CertificateEvent -= new certificateValidation(m_Server_CertificateEvent);
                Server.subscriptionInfo -= new Server.SubscriptionInfo(m_Server_subscriptionInfo);
                Server.CertificateEvent += new certificateValidation(m_Server_CertificateEvent);
                Server.subscriptionInfo += new Server.SubscriptionInfo(m_Server_subscriptionInfo);
                string url = "opc.tcp://" + Michine_IP + ":4840";
                Server.Connect(url, Michine_UserName, Michine_PassWord);
                NodeIdCollection nodes = new NodeIdCollection();
                nodes.Add(new NodeId(Command, m_NameSpaceIndex));
                Opc.Ua.DataValueCollection dataValues = new Opc.Ua.DataValueCollection();
                Server.ReadValues(nodes, out dataValues);
                Server.Disconnect();
                return dataValues[0].Value.ToString();
            }
        }

        /// <summary>
        /// 获取当前机床内执行程序的名称
        /// </summary>
        /// <returns></returns>
        public string Get_ProgramName()
        {
            lock (this)
            {
                try
                {
                    return Read("/Channel/ProgramInfo/progName");
                }
                catch { return ""; }
            }
        }

        /// <summary>
        /// 获取机床操作模式
        /// </summary>
        /// <returns></returns>
        public string Get_Mode()
        {
            lock (this)
            {
                string value = Read("/Bag/State/opMode");
                switch (value)
                {
                    case "0": return "JOG";
                    case "1": return "MDI";
                    case "2": return "AUTO";
                    default: return "UKNOW";//未知模式
                }
            }
        }

        /// <summary>
        /// 上传程序到机床
        /// </summary>
        /// <param name="LocalPath">本地文件完整路径</param>
        /// <param name="RemotePath">机床内文件夹路径：/card/user/sinumerik/data/mnt/usercf/</param>
        /// <returns></returns>
        public bool UploadProgram(string LocalPath, string RemotePath)
        {
            lock (this)
            {
                try
                {
                    this.AllLine = Get_Program_Line(LocalPath);//给当前对象的程序行数赋值
                    if (!Session.Opened)//连接已断开
                    {
                        Session = new Session();
                        SessionOptions sess = new SessionOptions
                        {
                            Protocol = Protocol.Scp,
                            HostName = Michine_IP,
                            UserName = "user",
                            Password = "CUSTOMER",
                            SshHostKeyFingerprint = WinScp_HostKey,
                        };
                        Session.Open(sess);//重新连接
                    }
                    TransferOptions transferOptions = new TransferOptions();
                    transferOptions.TransferMode = TransferMode.Binary;
                    TransferOperationResult transferResult;
                    transferResult = Session.PutFiles(LocalPath, RemotePath, false, transferOptions);
                    if (transferResult.IsSuccess)
                        return true;
                }
                catch { return false; }
                return false;
            }
        }

        /// <summary>
        /// 删除机床内文件
        /// </summary>
        /// <param name="RemotePath">机床内文件完整路径：/card/user/sinumerik/data/mnt/usercf/NCPROGRAM.mpf</param>
        /// <returns></returns>
        public bool DeleteProgram(string RemotePath)
        {
            lock (this)
            {
                try
                {
                    RemovalOperationResult transferResult;
                    transferResult = Session.RemoveFiles(RemotePath);
                    if (transferResult.IsSuccess)
                        return true;
                }
                catch { return false; }
                return false;
            }
        }

        /// <summary>
        /// 下载机床内程序到本地
        /// </summary>
        /// <param name="LocalPath">保存到本地文件夹路径</param>
        /// <param name="RemotePath">机床内程序文件完整路径：/card/user/sinumerik/data/mnt/usercf/NCPROGRAM.mpf</param>
        /// <returns></returns>
        public bool DownLoadProgram(string LocalPath, string RemotePath)
        {
            lock (this)
            {
                try
                {
                    TransferOptions transferOptions = new TransferOptions();
                    transferOptions.TransferMode = TransferMode.Binary;
                    TransferOperationResult transferResult;
                    transferResult = Session.GetFiles(RemotePath, LocalPath.TrimEnd('\\') + "\\", false, transferOptions);
                    if (transferResult.IsSuccess)
                        return true;
                }
                catch { return false; }
                return false;
            }
        }
    }
}
