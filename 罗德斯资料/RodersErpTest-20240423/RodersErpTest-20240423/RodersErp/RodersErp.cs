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

namespace RodersNameSpace
{
    /// <summary>
    /// 罗德斯Erp类
    /// </summary>
    public class Roders_Erp
    {
        #region 属性

        /// <summary>
        /// 通信版本
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// 数据时间戳
        /// </summary>
        public DateTime UpdateTime { get; }

        /// <summary>
        /// 机床编号
        /// </summary>
        public string MachineName { get; }

        /// <summary>
        /// 机床系统版本号
        /// </summary>
        /// <returns></returns>
        public string Ver { get; }

        /// <summary>
        /// 机床模式：MANUAL, AUTOMATIC
        /// </summary>
        public string Mode { get; }

        /// <summary>
        /// 机床状态IDLE, OFF, ECO, RUNNING, ERROR
        /// </summary>
        public string State { get; }

        /// <summary>
        /// 侧门状态：CLOSED, OPEN
        /// </summary>
        public string DoorState { get; }

        /// <summary>
        /// 自动门状态：CLOSED, OPEN，NOT_CONNECTED
        /// </summary>
        public string AutoDoorState { get; }

        /// <summary>
        /// 实际进给
        /// </summary>
        public int Feed { get; } = 0;

        /// <summary>
        /// 倍率
        /// </summary>
        public int Ov_Feed { get; } = 0;

        /// <summary>
        /// 刀号
        /// </summary>
        public int Tool { get; }

        /// <summary>
        /// 刀具类型
        /// </summary>
        public string ToolType { get; }

        /// <summary>
        /// 主轴转速
        /// </summary>
        public int SpinSpeed { get; } = 0;

        /// <summary>
        /// 主轴倍率
        /// </summary>
        public int Ov_SpinSpeed { get; } = 0;

        /// <summary>
        /// 当前机床加工信息
        /// </summary>
        public RodersJob Job { get; }




        #endregion


        #region 子类

        /// <summary>
        /// 机床加工信息类
        /// </summary>
        public class RodersJob
        {
            /// <summary>
            /// 名称
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// 当前运行
            /// </summary>
            public string NcPgm { get; }

            /// <summary>
            /// 主程序开始时间
            /// </summary>
            public DateTime Start { get; }

            /// <summary>
            /// 当前运行子程序
            /// </summary>
            public string SubPgm { get; }

            /// <summary>
            /// 子程序开始时间
            /// </summary>
            public DateTime SubStart { get; }

            /// <summary>
            /// 最后加工结束的时间
            /// </summary>
            public DateTime LastJobEnd { get; }

            /// <summary>
            /// 程序运行百分比， 如同有子程序表示当前子程序进度，如果没有子程序表示当前主程序
            /// </summary>
            public int Percent { get; }

            /// <summary>
            /// 程序运行行号
            /// </summary>
            public int Line { get; }

            /// <summary>
            /// 
            /// </summary>
            public object Chuck { get; }

            /// <summary>
            /// 
            /// </summary>
            public object Pallet { get; }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Name">名称</param>
            /// <param name="NcPgm">当前运行</param>
            /// <param name="Start">主程序开始时间</param>
            /// <param name="SubPgm">当前运行子程序</param>
            /// <param name="SubStart">子程序开始时间</param>
            /// <param name="Percent">程序运行百分比</param>
            /// <param name="Line">程序运行行号</param>
            /// <param name="Chuck"></param>
            /// <param name="Pallet"></param>
            public RodersJob(string Name, string NcPgm, DateTime Start, string SubPgm, DateTime SubStart, int Percent, int Line,DateTime LastJobEnd, object Chuck=null, object Pallet=null)
            {
                this.Name = Name;
                this.NcPgm = NcPgm;
                this.Start = Start;
                this.SubPgm = SubPgm;
                this.SubStart = SubStart;
                this.Percent = Percent;
                this.Line = Line;
                this.Chuck = Chuck;
                this.Pallet = Pallet;
                this.LastJobEnd = LastJobEnd;
            }
        }

        #endregion

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="Version">通信版本</param>
        /// <param name="UpdateTime">数据时间戳</param>
        /// <param name="MachineName">机床编号</param>
        /// <param name="Ver">机床系统版本号</param>
        /// <param name="Mode">机床模式：MANUAL, AUTOMATIC</param>
        /// <param name="State">机床状态IDLE, OFF, ECO, RUNNING, ERROR</param>
        /// <param name="DoorState">侧门状态：CLOSED, OPEN</param>
        /// <param name="AutoDoorState">自动门状态：CLOSED, OPEN，NOT_CONNECTED</param>
        /// <param name="Feed">实际进给</param>
        /// <param name="Ov_Feed">倍率</param>
        /// <param name="Tool">刀号</param>
        /// <param name="ToolType">刀具类型</param>
        /// <param name="SpinSpeed">主轴转速</param>
        /// <param name="Ov_SpinSpeed">主轴倍率</param>
        /// <param name="Job">当前机床加工信息</param>
        public Roders_Erp(string Version, DateTime UpdateTime, string MachineName, string Ver, string Mode, string State,
            string DoorState, string AutoDoorState, int Feed, int Ov_Feed, int Tool, string ToolType, int SpinSpeed, int Ov_SpinSpeed, RodersJob Job)
        {
            this.Version = Version;
            this.UpdateTime = UpdateTime;
            this.MachineName = MachineName;
            this.Ver = Ver;
            this.Mode = Mode;
            this.State = State;
            this.DoorState = DoorState;
            this.AutoDoorState = AutoDoorState;
            this.Feed = Feed;
            this.Ov_Feed = Ov_Feed;
            this.Tool = Tool;
            this.ToolType = ToolType;
            this.SpinSpeed = SpinSpeed;
            this.Ov_SpinSpeed = Ov_SpinSpeed;
            this.Job = Job;
        }
        
        /// <summary>
        /// 获取xml指定节点的文本内容
        /// </summary>
        /// <param name="xml">xml文档对象</param>
        /// <param name="TagName">节点名称</param>
        /// <param name="index">获取顺序的下标</param>
        /// <returns></returns>
        private static string Get_XmlNode_Text(XmlDocument xml,string TagName,int index)
        {
            string val = null;
            try
            {
                val = xml.GetElementsByTagName(TagName)[index].InnerText;
            }
            catch { }
            return val;
        }

        /// <summary>
        /// 获取xml指定节点的整型数据
        /// </summary>
        /// <param name="xml">xml文档对象</param>
        /// <param name="TagName">节点名称</param>
        /// <param name="index">获取顺序的下标</param>
        /// <param name="index">获取失败返回的默认值，默认为-1</param>
        /// <returns></returns>
        private static int Get_XmlNode_Int(XmlDocument xml, string TagName, int index,int Default=-1)
        {
            int val = Default;
            try
            {
                val = Convert.ToInt32(xml.GetElementsByTagName(TagName)[index].InnerText);
            }
            catch { }
            return val;
        }

        /// <summary>
        /// 获取xml指定节点的文本的时间对象，无效则返回当前时间
        /// </summary>
        /// <param name="xml">xml文档对象</param>
        /// <param name="TagName">节点名称</param>
        /// <param name="index">获取顺序的下标</param>
        /// <returns></returns>
        private static DateTime Get_XmlNode_Datetime(XmlDocument xml, string TagName, int index)
        {
            DateTime val = DateTime.Now;
            try
            {
                val = DateTime.Parse(xml.GetElementsByTagName(TagName)[index].InnerText);
            }
            catch { }
            return val;
        }


        /// <summary>
        /// 从xml文档中加载
        /// </summary>
        /// <param name="path">xml文档路径</param>
        /// <returns></returns>
        public static Roders_Erp Erp_Load(string path)
        {
            if (!File.Exists(path))
                return null;//文件不存在
            DateTime UpdateTime = DateTime.Now;
            string Version=null, MachineName = null, Ver = null, Mode = null, State = null, DoorState = null, AutoDoorState = null, ToolType = null;
            int Feed=-1, Ov_Feed = -1, Tool = -1, SpinSpeed = -1, Ov_SpinSpeed = -1;
            RodersJob Job=null;
            try
            {
                StreamReader sr = new StreamReader(path);
                string document = sr.ReadToEnd();
                sr.Close();
                XmlDocument xml = new XmlDocument();
                xml.LoadXml(document);
                Version = Get_XmlNode_Text(xml, "version", 0);
                UpdateTime = Get_XmlNode_Datetime(xml, "time", 0);
                MachineName = Get_XmlNode_Text(xml, "name", 0);
                Ver = Get_XmlNode_Text(xml, "ver", 0);
                Mode = Get_XmlNode_Text(xml, "mode", 0);
                State = Get_XmlNode_Text(xml, "state", 0);
                DoorState = Get_XmlNode_Text(xml, "doorstate", 0);
                AutoDoorState = Get_XmlNode_Text(xml, "doorstate", 1);
                Feed = Get_XmlNode_Int(xml, "feed", 0);
                Ov_Feed = Get_XmlNode_Int(xml, "ov_feed", 0);
                Tool = Get_XmlNode_Int(xml, "tool", 0);
                ToolType = Get_XmlNode_Text(xml, "tooltype", 0);
                SpinSpeed = Get_XmlNode_Int(xml, "spinspeed", 0);
                Ov_SpinSpeed = Get_XmlNode_Int(xml, "ov_spinspeed", 0);
                string name = Get_XmlNode_Text(xml, "name", 1);
                string ncpgm = Get_XmlNode_Text(xml, "ncpgm", 0);
                DateTime start = Get_XmlNode_Datetime(xml, "start", 0);
                string subpgm = Get_XmlNode_Text(xml, "subpgm", 0);
                DateTime substart = Get_XmlNode_Datetime(xml, "substart", 0);
                int percent = -1;
                try
                {
                    percent = Convert.ToInt32(Get_XmlNode_Text(xml, "percent", 0).Replace("%", ""));
                }
                catch { }
                int line = Get_XmlNode_Int(xml, "line", 0);
                DateTime LastJobEnd = Get_XmlNode_Datetime(xml, "lastjobend", 0);
                Job = new RodersJob(name, ncpgm, start, subpgm, substart, percent, line, LastJobEnd);

            }
            catch
            {
                return null;
            }
            return new Roders_Erp(Version, UpdateTime, MachineName, Ver, Mode, State,
            DoorState, AutoDoorState, Feed, Ov_Feed, Tool, ToolType, SpinSpeed, Ov_SpinSpeed, Job);
        }
    }
    
    /// <summary>
    /// 罗德斯Oee类
    /// </summary>
    public class Roders_Oee
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="Content">Machine.BDE的文件路径</param>
        public Roders_Oee(string FilePath)
        {
            if (!File.Exists(FilePath))
                return;
            StreamReader sr = new StreamReader(FilePath);
            string Content = sr.ReadToEnd();
            sr.Close();
            Content = Content.Replace("************************************************************\r\n", "").Replace(">", "").Replace("\r", "").Replace("°\n", "").Replace("°", "").Replace(" ", "").Replace("\t", "");
            string[] value = Content.Split('\n');
            foreach (string s in value)
            {
                try
                {
                    if (s.StartsWith("加工时间:"))
                    {
                        string[] lis = s.Replace("加工时间:", "").Replace("h", "").Split(':');
                        this.ProTime = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("ProTime", this.ProTime);
                    }
                    else if (s.StartsWith("Speed0(machineOFF):"))
                    {
                        string[] lis = s.Replace("Speed0(machineOFF):", "").Replace("h", "").Split(':');
                        this.Speed_0_Off = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_0_Off", this.Speed_0_Off);
                    }
                    else if (s.StartsWith("Speed0(machineON):"))
                    {
                        string[] lis = s.Replace("Speed0(machineON):", "").Replace("h", "").Split(':');
                        this.Speed_0_On = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_0_On", this.Speed_0_On);
                    }
                    else if (s.StartsWith("TotaltimeSpeed0:"))
                    {
                        string[] lis = s.Replace("TotaltimeSpeed0:", "").Replace("h", "").Split(':');
                        this.Speed_0_Total = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_0_Total", this.Speed_0_Total);
                    }
                    else if (s.StartsWith("Speedrange0%to10%:"))
                    {
                        string[] lis = s.Replace("Speedrange0%to10%:", "").Replace("h", "").Split(':');
                        this.Speed_0_To_10 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_0_To_10", this.Speed_0_To_10);
                    }
                    else if (s.StartsWith("Speedrange10%to20%:"))
                    {
                        string[] lis = s.Replace("Speedrange10%to20%:", "").Replace("h", "").Split(':');
                        this.Speed_10_To_20 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_10_To_20", this.Speed_10_To_20);
                    }
                    else if (s.StartsWith("Speedrange20%to30%:"))
                    {
                        string[] lis = s.Replace("Speedrange20%to30%:", "").Replace("h", "").Split(':');
                        this.Speed_20_To_30 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_20_To_30", this.Speed_20_To_30);
                    }
                    else if (s.StartsWith("Speedrange30%to40%:"))
                    {
                        string[] lis = s.Replace("Speedrange30%to40%:", "").Replace("h", "").Split(':');
                        this.Speed_30_To_40 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_30_To_40", this.Speed_30_To_40);
                    }
                    else if (s.StartsWith("Speedrange40%to50%:"))
                    {
                        string[] lis = s.Replace("Speedrange40%to50%:", "").Replace("h", "").Split(':');
                        this.Speed_40_To_50 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_40_To_50", this.Speed_40_To_50);
                    }
                    else if (s.StartsWith("Speedrange50%to60%:"))
                    {
                        string[] lis = s.Replace("Speedrange50%to60%:", "").Replace("h", "").Split(':');
                        this.Speed_50_To_60 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_50_To_60", this.Speed_50_To_60);
                    }
                    else if (s.StartsWith("Speedrange60%to70%:"))
                    {
                        string[] lis = s.Replace("Speedrange60%to70%:", "").Replace("h", "").Split(':');
                        this.Speed_60_To_70 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_60_To_70", this.Speed_60_To_70);
                    }
                    else if (s.StartsWith("Speedrange70%to80%:"))
                    {
                        string[] lis = s.Replace("Speedrange70%to80%:", "").Replace("h", "").Split(':');
                        this.Speed_70_To_80 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_70_To_80", this.Speed_70_To_80);
                    }
                    else if (s.StartsWith("Speedrange80%to90%:"))
                    {
                        string[] lis = s.Replace("Speedrange80%to90%:", "").Replace("h", "").Split(':');
                        this.Speed_80_To_90 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1]) / 60.0;
                        Values.Add("Speed_80_To_90", this.Speed_80_To_90);
                    }
                    else if (s.StartsWith("Speedrange90%to100%:"))
                    {
                        string[] lis = s.Replace("Speedrange90%to100%:", "").Replace("h","").Split(':');
                        this.Speed_90_To_100 = Convert.ToInt32(lis[0]) + Convert.ToInt32(lis[1])/60.0;
                        Values.Add("Speed_90_To_100", this.Speed_90_To_100);
                    }
                    else if (s.StartsWith("换刀:"))
                    {
                        this.ToolChange = Convert.ToInt32(s.Replace("换刀:", ""));
                        Values.Add("ToolChange", this.ToolChange);
                    }
                    else if (s.StartsWith("Manualtoolchange:"))
                    {
                        this.Manual_ToolChange = Convert.ToInt32(s.Replace("Manualtoolchange:", ""));
                        Values.Add("Manual_ToolChange", this.Manual_ToolChange);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 获取的所有的值
        /// </summary>
        public Dictionary<string, double> Values = new Dictionary<string, double>();

        public string[] Keys { get; } = new string[] { "ProTime","Speed_0_Off","Speed_0_On","Speed_0_Total","Speed_0_To_10","Speed_10_To_20","Speed_20_To_30",
                "Speed_30_To_40","Speed_40_To_50","Speed_50_To_60","Speed_60_To_70","Speed_70_To_80","Speed_80_To_90","Speed_90_To_100","Manual_ToolChange","ToolChange"};

        /// <summary>
        /// 加工时间
        /// </summary>
        public double ProTime { get; } = 0;

        /// <summary>
        /// 倍率为0关机时间
        /// </summary>
        public double Speed_0_Off { get; } = 0;

        /// <summary>
        /// 倍率为0开机时间
        /// </summary>
        public double Speed_0_On { get; } = 0;

        /// <summary>
        /// 倍率为0的总时间
        /// </summary>
        public double Speed_0_Total { get; } = 0;

        /// <summary>
        /// 倍率为0-10的运行总时间
        /// </summary>
        public double Speed_0_To_10 { get; } = 0;

        /// <summary>
        /// 倍率为10-20的运行总时间
        /// </summary>
        public double Speed_10_To_20 { get; } = 0;

        /// <summary>
        /// 倍率为20-30的运行总时间
        /// </summary>
        public double Speed_20_To_30 { get; } = 0;

        /// <summary>
        /// 倍率为30-40的运行总时间
        /// </summary>
        public double Speed_30_To_40 { get; } = 0;

        /// <summary>
        /// 倍率为40-50的运行总时间
        /// </summary>
        public double Speed_40_To_50 { get; } = 0;

        /// <summary>
        /// 倍率为50-60的运行总时间
        /// </summary>
        public double Speed_50_To_60 { get; } = 0;

        /// <summary>
        /// 倍率为60-70的运行总时间
        /// </summary>
        public double Speed_60_To_70 { get; } = 0;

        /// <summary>
        /// 倍率为70-80的运行总时间
        /// </summary>
        public double Speed_70_To_80 { get; } = 0;

        /// <summary>
        /// 倍率为80-90的运行总时间
        /// </summary>
        public double Speed_80_To_90 { get; } = 0;

        /// <summary>
        /// 倍率为90-100的运行总时间
        /// </summary>
        public double Speed_90_To_100 { get; } = 0;

        /// <summary>
        /// 手动换刀次数
        /// </summary>
        public int Manual_ToolChange { get; } = 0;

        /// <summary>
        /// 总换刀次数
        /// </summary>
        public int ToolChange { get; } = 0;
    }
}
