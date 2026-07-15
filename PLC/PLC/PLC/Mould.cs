using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLC
{
    public class Mould
    {
        #region 常用变量

        /// <summary>
        /// 扫描总数
        /// </summary>
        public int scannum = 0;

        /// <summary>
        /// 检测OK电极数
        /// </summary>
        public int OKnum = 0;

        /// <summary>
        /// 检测NG电极数
        /// </summary>
        public int NGnum = 0;

        public string GetMacStatusHeader = "IPLCSTAT>";

        public string ChoiseProgramHeader = "IINSPECT>";

        public string ReqMacWorkModeHeader = "RCMMSTAT>";

        public string ReqMacStartHeader = "RSTRTCNC>";

        public string ReqMacStopHeader = "RSTOPCNC>";

        public string ReqMacContinueHeader = "RCONTCNC>";

        public string CmmFinishHeader = "IMEASFIN>";

        public string CmmStatusHeader = "ICMMSTAT>";

        public List<string> BigPos = new List<string> { "41", "42", "43", "44", "45", "46", "47", "48", "49", "50", "51", "52", "53", "54", "55", "56", "57", "58", "59", "60", "121", "122", "123", "124", "125", "126", "127", "128", "129", "130", "131", "132", "133", "134", "135", "136", "137", "138", "139", "140" };

        public string[] UrgentPos = new string[] { "121", "122", "123", "124", "125", "126", "127", "128", "129", "130", "131", "132", "133", "134", "135", "136", "137", "138", "139", "140" };

        public List<string> NGBigPos = new List<string> { "28", "29", "30", "31", "32", "33", "34", "35", "36", "55", "56", "57", "58", "59", "60", "61", "62", "63" };

        public List<string> unablepos = new List<string> { "61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,141,142,143,144,145,146,147,148,149,150,151,152,153,154,155,156,157,158,160" };

        #endregion

        #region eman自动化软件通讯指令

        /// <summary>
        /// 生成检测程序指令
        /// </summary>
        /// <param name="sn">模号</param>
        /// <param name="partfilename">电极图档名称</param>
        /// <param name="cuprumsn">电极名称</param>
        /// <param name="cnch">电极的测高</param>
        /// <param name="cardnum">卡盘编号</param>
        /// <param name="barcode">监控编号</param>
        /// <returns></returns>
        public string CreatProgramZhiLing(string sn, string partfilename, string cuprumsn, string cnch, string cardnum, string barcode)
        {
            return string.Format("GLDJPROGRAM#{0}#{1}#{2}#{3}#{4}#{5}#SendEnd", sn, partfilename, cuprumsn, cnch, cardnum, barcode);
        }

        /// <summary>
        /// 生成发送结果文件成功指令
        /// </summary>
        /// <param name="cuprumsn">电极名称</param>
        /// <param name="barcode">监控编号</param>
        /// <returns></returns>
        public string SendReportZhiLing(string cuprumsn, string barcode)
        {
            return string.Format("GLDJREPORT#{0}#{1}#SendEnd", cuprumsn, barcode);
        }

        /// <summary>
        /// 获取生成检测程序结果
        /// </summary>
        /// <param name="mes">eman服务器返回的信息</param>
        /// <returns>解析出的结果</returns>
        public string GetCreateProgramResult(string mes)
        {
            string result = null;
            string[] meslist = mes.Split('#');
            switch (meslist[1])
            {
                case "1":
                    result = "生成成功！";
                    break;
                case "-1":
                    result = string.Format("没有发现监控编号{0}的点文件目录，没有生成对应取点文件,错误码为-1", meslist[2]);
                    break;
                case "-2":
                    result = string.Format("FTP上没有发现电极{0}的取点文件,错误码为-2", meslist[2]);
                    break;
                case "-3":
                    result = string.Format("电极{0}取点文件解析出错,错误码为-3", meslist[2]);
                    break;
                case "-4":
                    result = string.Format("初始化编程参数出现问题,错误码为-4");
                    break;
                case "-5":
                    result = string.Format("程序编制错误,点文件中基准点信息缺少或其他原因,错误码为-5");
                    break;
            }
            return result;
        }

        /// <summary>
        /// 获取检测结果
        /// </summary>
        /// <param name="mes">eman服务器返回的信息</param>
        /// <returns>解析出的结果</returns>
        public string GetResult(string mes)
        {
            string result = null;
            if (mes != null)
            {
                string[] meslist = mes.Split('#');
                switch (meslist[1])
                {
                    case "1":
                        result = "NG";
                        break;
                    case "2":
                        result = "OK";
                        break;
                    default:
                        result = meslist[1];
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// 获取检测结果
        /// </summary>
        /// <param name="mes"></param>
        /// <returns></returns>
        public string GetResult2(string mes)
        {
            string result = null;
            switch (mes)
            {
                case "2":
                    result = "OK";
                    break;
                case "3":
                    result = "NG";
                    break;
            }
            return result;
        }

        #endregion

        #region 静态变量  

        public static bool scanliefinish = false;

        public static bool scanrackfinish = false;

        public static bool robotexecute360task = false;

        public static string operatorName;

        public static bool SystemPause;

        public static bool SystemStop;

        public static bool roboterror;

        public static bool ConnectError;

        public static int RackTypeIndex;

        public static int posImage;

        public static string MonitorID;

        public static int Pos_id;

        public static bool TurnLoadIsAuto;

        public static string checkresult;

        /// <summary>
        /// 超规格电极监控编号
        /// </summary>
        public static string specialrfid = null;

        /// <summary>
        /// 超规格电极名称
        /// </summary>
        public static string specialverifyid = null;

        /// <summary>
        /// 超规格电极检测开始标志位
        /// </summary>
        public static bool SpecialStart = false;

        /// <summary>
        /// 超规格电极处理错误
        /// </summary>
        public static string SpecialError = null;

        /// <summary>
        /// 超规格电极检测结果
        /// </summary>
        public static string SpecialResult = null;

        /// <summary>
        /// 允许三坐标开始动作标志位
        /// </summary>
        public static bool StartCmm = false;

        public static bool ProgramClose = false;

        #endregion

        #region 枚举

        /// <summary>
        /// 日志类型
        /// </summary>
        public enum LogType
        {
            /// <summary>
            /// 任务日志
            /// </summary>
            tasklog = 1,

            /// <summary>
            /// 运行异常日志
            /// </summary>
            softerror = 2,

            /// <summary>
            /// 机器人异常日志
            /// </summary>
            roboterror = 3
        }


        /// <summary>
        /// 料架编号
        /// </summary>
        public enum RackNum
        {
            /// <summary>
            /// NG料架
            /// </summary>
            NgRack = 0,

            /// <summary>
            /// A夹爪NG料架
            /// </summary>
            ArackNg = 1,

            /// <summary>
            /// B夹爪NG料架
            /// </summary>
            BrackNg = 2,

            /// <summary>
            ///A爪旋转料架
            /// </summary>
            ArotateRack = 3,

            /// <summary>
            /// B爪旋转料架
            /// </summary>
            BrotateRack = 4,

            /// <summary>
            ///A夹爪CMM 
            /// </summary>
            ACMM = 5,

            /// <summary>
            /// B夹爪CMM
            /// </summary>
            BCMM = 6
        }

        /// <summary>
        /// 动作类型
        /// </summary>
        public enum MoveType
        {
            /// <summary>
            /// 抓取
            /// </summary>
            Catch = 1,

            /// <summary>
            /// 放置
            /// </summary>
            put = 2,

            /// <summary>
            /// 扫描
            /// </summary>
            scan = 3,

            /// <summary>
            /// 移动
            /// </summary>
            run = 4
        }

        /// <summary>
        /// 电极的工作状态
        /// </summary>
        public enum WorkState
        {
            /// <summary>
            /// 等待检测
            /// </summary>
            WaitProcess,
            /// <summary>
            /// 检测中
            /// </summary>
            Working,
            /// <summary>
            /// 检测合格
            /// </summary>
            ResultOk,
            /// <summary>
            /// 检测不合格
            /// </summary>
            ResultNg,
            /// <summary>
            /// 特采
            /// </summary>
            UAI,
            /// <summary>
            /// 程序异常
            /// </summary>
            ProgramError,
            /// <summary>
            /// 检测完成
            /// </summary>
            ProcessFinish,
            /// <summary>
            /// 找不到公差
            /// </summary>
            NotFindDev = -3,
            /// <summary>
            /// 找不到取点文件
            /// </summary>
            NotFindPointFile = -4,
            /// <summary>
            /// 找不到结果文件
            /// </summary>
            NotFindResultFile = -5,
            /// <summary>
            /// 结果文件分析出错
            /// </summary>
            ResultFileAnaylsError = -6,
            /// <summary>
            /// 结果分析错误
            /// </summary>
            ResultAnaylsError = -7,
            /// <summary>
            /// 被取消电极
            /// </summary>
            isremove = 8
        }

        /// <summary>
        /// 检测结果
        /// </summary>
        public enum CheckResult
        {
            /// <summary>
            /// 检测合格
            /// </summary>
            ResultOk,
            /// <summary>
            /// 检测不合格
            /// </summary>
            ResultNg,
        }

        /// <summary>
        /// 电极加工优先级
        /// </summary>
        public enum Priority
        {
            /// <summary>
            /// 普通件
            /// </summary>
            normal,
            /// <summary>
            /// 急件
            /// </summary>
            urgent,
            /// <summary>
            /// 置顶
            /// </summary>
            thetop

        }

        /// <summary>
        /// 料架料位状态
        /// </summary>
        public enum PosStatus
        {
            /// <summary>
            /// 禁用
            /// </summary>
            forbid,
            /// <summary>
            /// 无料
            /// </summary>
            wuliao,
            /// <summary>
            /// 有料
            /// </summary>
            youliao
        }

        /// <summary>
        /// 扫描状态
        /// </summary>
        public enum ScanStatus
        {
            /// <summary>
            /// 待扫描
            /// </summary>
            waitscan,
            /// <summary>
            /// 扫描成功
            /// </summary>
            scansuccess,
            /// <summary>
            /// 扫描失败
            /// </summary>
            scanfail,
            /// <summary>
            /// 程序错误
            /// </summary>
            programerror
        }

        /// <summary>
        /// 图片索引
        /// </summary>
        public enum ImageIndex
        {
            Forbid,
            WaitScan,
            nulltool,
            NG,
            waitprocess,
            urgent,
            working,
            ok,
            thetop,
            UAI,
            error
        }

        /// <summary>
        /// 将从数据库获取到的工作状态转化为对应枚举值
        /// </summary>
        /// <param name="workstate">工作状态</param>
        /// <returns></returns>
        public WorkState GetWorkState(string workstate)
        {
            WorkState state = new WorkState();
            switch (workstate)
            {
                case "0":
                    state = WorkState.WaitProcess;
                    break;
                case "1":
                    state = WorkState.Working;
                    break;
                case "2":
                    state = WorkState.ResultOk;
                    break;
                case "3":
                    state = WorkState.ResultNg;
                    break;
                case "4":
                    state = WorkState.UAI;
                    break;
                case "5":
                    state = WorkState.ProgramError;
                    break;
                case "6":
                    state = WorkState.ProcessFinish;
                    break;
                case "-3":
                    state = WorkState.NotFindDev;
                    break;
                case "-4":
                    state = WorkState.NotFindPointFile;
                    break;
                case "-5":
                    state = WorkState.NotFindResultFile;
                    break;
                case "-6":
                    state = WorkState.ResultFileAnaylsError;
                    break;
                case "-7":
                    state = WorkState.ResultAnaylsError;
                    break;
            }
            return state;
        }

        /// <summary>
        /// 获取加工优先级
        /// </summary>
        /// <param name="priority">优先级</param>
        /// <returns></returns>
        public Priority GetPriority(string priority)
        {
            Priority prorty = new Priority();
            switch (priority)
            {
                case "0":
                    prorty = Priority.normal;
                    break;
                case "1":
                    prorty = Priority.urgent;
                    break;
                case "2":
                    prorty = Priority.thetop;
                    break;
            }
            return prorty;
        }

        /// <summary>
        /// 获取扫描状态
        /// </summary>
        /// <param name="scanstatus">扫描状态</param>
        /// <returns></returns>
        public ScanStatus GetScanStatus(string scanstatus)
        {
            ScanStatus status = new ScanStatus();
            switch (scanstatus)
            {
                case "0":
                    status = ScanStatus.waitscan;
                    break;
                case "1":
                    status = ScanStatus.scansuccess;
                    break;
                case "2":
                    status = ScanStatus.scanfail;
                    break;
                case "3":
                    status = ScanStatus.programerror;
                    break;
            }
            return status;
        }

        /// <summary>
        /// 获取料位状态
        /// </summary>
        /// <param name="status">料位状态</param>
        /// <returns></returns>
        public PosStatus GetPosStatus(string status)
        {
            PosStatus sta = new PosStatus();
            switch (status)
            {
                case "0":
                    sta = PosStatus.forbid;
                    break;
                case "1":
                    sta = PosStatus.wuliao;
                    break;
                case "2":
                    sta = PosStatus.youliao;
                    break;
            }
            return sta;
        }

        #endregion

        #region 电极基础信息

        public string projectName { set; get; }

        public string projectInteriorID { set; get; }

        public string mouldName { get; set; }

        public string mouldInteriorID { get; set; }

        public string specifications { get; set; }

        public string remark { get; set; }

        public string resourceName { get; set; }

        public string serialID { set; get; }

        public string verifyID { get; set; }

        public string name { get; set; }

        #endregion

        #region listview更新相关

        //更新到listview数据是否是错误的

        public static int Iserror { get; set; }

        //要更新到listview中的内容

        public static string Lvmessage { get; set; }

        #endregion

        #region 用户相关信息

        //用户名

        public static string User { get; set; }

        #endregion

        #region 系统状态相关属性

        //系统开始标志位

        public static bool Systemstart { get; set; }

        /// <summary>
        /// 急停标志位
        /// </summary>
        public static bool EmerGencyStop { get; set; }

        #endregion

        #region 查询界面DataGridView列头名

        /// <summary>
        /// 加工记录列名表
        /// </summary>
        /// <returns></returns>
        public List<string> ProcessLog()
        {
            List<string> columnsname = new List<string>
            {
                "监控编号",
                "项目名称",
                "项目编号",
                "模具名称",
                "工装编号",
                "电极名称",
                "加工机台",
                "操作员",
                "上机时间",
                "下机时间",
                "检测结果"
            };
            return columnsname;
        }


        /// <summary>
        /// 机器日志列名表
        /// </summary>
        /// <returns></returns>
        public List<string> MachineLog()
        {
            List<string> columnsname = new List<string>
            {
                "序号",
                "执行内容",
                "操作员",
                "操作时间",
                "日志类型"
            };
            return columnsname;
        }

        #endregion

        #region PLC变量

        /// <summary>
        /// 任务号写入区1
        /// </summary>
        public int DM_TASKNUM1 = 150;

        /// <summary>
        /// 任务号写入区2
        /// </summary>
        public int DM_TASKNUM2 = 160;

        public int RBTasknumFB = 130;

        /// <summary>
        /// 任务开始信号
        /// </summary>
        public string TaskStart = "200.00";

        /// <summary>
        /// 任务完成信号
        /// </summary>
        public string TaskFinish = "200.01";


        /// <summary>
        /// 机器人状态信号
        /// </summary>
        public string RobotStatus = "200.02";

        /// <summary>
        /// 线体急停信号
        /// </summary>
        public string LineStop = "200.03";

        /// <summary>
        /// 旋转料架发送开门请求信号
        /// </summary>
        public string TransferRackDoorOpenRequest = "200.10";

        /// <summary>
        /// PC允许旋转料架门打开信号
        /// </summary>
        public string PcAllowTransferRackDoorOpen = "200.11";

        /// <summary>
        /// 旋转料架门已打开信号
        /// </summary>
        public string TransferRackDoorIsClose = "200.12";

        /// <summary>
        /// 旋转料架门状态
        /// </summary>
        public string TransferRackDoorStatus = "253";

        /// <summary>
        /// 缓存料架门打开请求信号
        /// </summary>
        public string RackDoorOpenRequest = "200.10";

        /// <summary>
        /// 检测房门第一次被打开
        /// </summary>
        public string RoomDoorIsOpen = "200.15";

        /// <summary>
        /// 检测房门第二次被打开
        /// </summary>
        public string RoomDoorIsOpenTwo = "201.01";

        /// <summary>
        /// 软件允许电极缓存料架门打开信号
        /// </summary>
        public string PcAllowRackDoorOpen = "254";

        /// <summary>
        /// 缓存料架门已打开信号
        /// </summary>
        public string RackDoorIsOpen = "200.11";

        /// <summary>
        ///缓存料架门关闭信号 
        /// </summary>
        public string RackIsReady = "200.12";

        /// <summary>
        ///控制蜂鸣器信号
        /// </summary>
        public string BeepControl = "203.06";

        /// <summary>
        /// 控制红灯信号
        /// </summary>
        public string RedControl = "201.13";
        /// <summary>
        /// 三坐标光电检测信号
        /// </summary>
        public string CMMEleteric = "203.03";

        /// <summary>
        /// 机器人光电
        /// </summary>
        public string RobEleteric = "203.04";

        /// <summary>
        /// 线体急停信号
        /// </summary>
        public string Estop = "200.03";

        /// <summary>
        /// NG料架门开反馈
        /// </summary>
        public string NGRackDoorFB = "200.05";

        /// <summary>
        /// 旋转料架光电信号列表
        /// </summary>
        public string[] TransferRackLightList = new string[] { "237.07", "237.06", "237.05", "237.04", "237.03", "237.02", "237.01", "237.00" };

        /// <summary>
        /// 串口列表
        /// </summary>
        public string[] COMList = new string[] { "COM5", "COM10", "COM9", "COM4", "COM2", "COM6", "COM8", "COM7" };

        #endregion

        #region 扫码枪串行指令


        //打开扫码枪指令
        public static Byte[] OpenScan()
        {
            Byte[] order = new Byte[3];
            order[0] = 0x01;//<SOH>的16进制表示
            order[1] = 0x54;//T的16进制表示
            order[2] = 0x04;//<EOT>的16进制表示
            return order;
        }

        //关闭扫码枪指令
        public static Byte[] CloseScan()
        {
            Byte[] order = new Byte[3];
            order[0] = 0x01;//<SOH>的16进制表示
            order[1] = 0x50;//P的16进制表示
            order[2] = 0x04;//<EOT>的16进制表示
            return order;
        }

        #endregion
    }
}
