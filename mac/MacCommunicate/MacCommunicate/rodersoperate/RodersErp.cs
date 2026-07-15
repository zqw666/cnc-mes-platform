using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace MacCommunicate
{
    public class RodersErp
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
            public RodersJob(string Name, string NcPgm, DateTime Start, string SubPgm, DateTime SubStart, int Percent, int Line, DateTime LastJobEnd, object Chuck = null, object Pallet = null)
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
        public RodersErp(string Version, DateTime UpdateTime, string MachineName, string Ver, string Mode, string State,
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
        private static string Get_XmlNode_Text(XmlDocument xml, string TagName, int index)
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
        private static int Get_XmlNode_Int(XmlDocument xml, string TagName, int index, int Default = -1)
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
        public static RodersErp Erp_Load(string path)
        {
            if (!File.Exists(path))
                return null;//文件不存在
            DateTime UpdateTime = DateTime.Now;
            string Version = null, MachineName = null, Ver = null, Mode = null, State = null, DoorState = null, AutoDoorState = null, ToolType = null;
            int Feed = -1, Ov_Feed = -1, Tool = -1, SpinSpeed = -1, Ov_SpinSpeed = -1;
            RodersJob Job = null;
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
            return new RodersErp(Version, UpdateTime, MachineName, Ver, Mode, State,
            DoorState, AutoDoorState, Feed, Ov_Feed, Tool, ToolType, SpinSpeed, Ov_SpinSpeed, Job);
        }
    }
}
