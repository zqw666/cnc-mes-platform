using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MacCommunicate
{
    public class Focus
    {
        /// <summary>
        /// 发那科系统通信执行结果返回
        /// </summary>
        public class FocusMsg
        {
            /// <summary>
            /// 获取返回值
            /// </summary>
            public short Value { get; }

            /// <summary>
            /// 获取返回值详细信息
            /// </summary>
            public string Msg { get; }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Value">返回错误值</param>
            /// <param name="Msg">详细错误信息</param>
            public FocusMsg(short Value, string Msg)
            {
                this.Value = Value;
                this.Msg = Msg;
            }
        }

        /// <summary>
        /// 坐标类
        /// </summary>
        public class Position
        {
            /// <summary>
            /// 获取X坐标
            /// </summary>
            public double X { get; } = 0;

            /// <summary>
            /// 获取Y坐标
            /// </summary>
            public double Y { get; } = 0;

            /// <summary>
            /// 获取Z坐标
            /// </summary>
            public double Z { get; } = 0;

            /// <summary>
            /// 构造函数
            /// </summary>
            public Position(double X, double Y, double Z)
            {
                this.X = X;
                this.Y = Y;
                this.Z = Z;
            }
        }

        /// <summary>
        /// 机床的所有坐标：相对坐标、绝对坐标、机械坐标、剩余移动量
        /// </summary>
        public class FocusPosition
        {
            /// <summary>
            /// 获取相对坐标
            /// </summary>
            public Position RelativePos { get; }

            /// <summary>
            /// 获取绝对坐标
            /// </summary>
            public Position AbsPos { get; }

            /// <summary>
            /// 获取机械坐标
            /// </summary>
            public Position MachinePos { get; }

            /// <summary>
            /// 获取剩余移动量
            /// </summary>
            public Position DistancePos { get; }

            /// <summary>
            /// 构造函数
            /// </summary>
            public FocusPosition()
            {

            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="fos">获取机床坐标结构体</param>
            public FocusPosition(ODBPOS fos)
            {
                if (fos != null)
                {
                    RelativePos = new Position(fos.p1.rel.data * Math.Pow(10, -fos.p1.rel.dec), fos.p2.rel.data * Math.Pow(10, -fos.p2.rel.dec), fos.p3.rel.data * Math.Pow(10, -fos.p3.rel.dec));
                    AbsPos = new Position(fos.p1.abs.data * Math.Pow(10, -fos.p1.abs.dec), fos.p2.abs.data * Math.Pow(10, -fos.p2.abs.dec), fos.p3.abs.data * Math.Pow(10, -fos.p3.abs.dec));
                    MachinePos = new Position(fos.p1.mach.data * Math.Pow(10, -fos.p1.mach.dec), fos.p2.mach.data * Math.Pow(10, -fos.p2.mach.dec), fos.p3.mach.data * Math.Pow(10, -fos.p3.mach.dec));
                    DistancePos = new Position(fos.p1.dist.data * Math.Pow(10, -fos.p1.dist.dec), fos.p2.dist.data * Math.Pow(10, -fos.p2.dist.dec), fos.p3.dist.data * Math.Pow(10, -fos.p3.dist.dec));
                }
            }
        }

        /// <summary>
        /// 机床伺服/主轴速度
        /// </summary>
        public class FocusSpeed
        {
            /// <summary>
            /// 获取伺服轴速度
            /// </summary>
            public int Actf_Speed { get; } = -1;

            /// <summary>
            /// 获取主轴速度
            /// </summary>
            public int Acts_Speed { get; } = -1;

            /// <summary>
            /// 构造函数
            /// </summary>
            public FocusSpeed()
            {

            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Actf_Speed">伺服轴速度</param>
            /// <param name="Acts_Speed">主轴速度</param>
            public FocusSpeed(int Actf_Speed, int Acts_Speed)
            {
                this.Actf_Speed = Actf_Speed;
                this.Acts_Speed = Acts_Speed;
            }
        }

        /// <summary>
        /// 机床主轴进给速率
        /// </summary>
        public class FocusRate
        {
            public int ActRate { get; } = -1;

            public FocusRate()
            {

            }

            public FocusRate(int actrate)
            {
                this.ActRate = actrate;
            }
        }

        /// <summary>
        /// 机床主轴转速
        /// </summary>
        public class FocusSpindle
        {
            public int Act_spindle { get; } = -1;

            public FocusSpindle()
            {

            }

            public FocusSpindle(int actspindle)
            {
                this.Act_spindle = actspindle;
            }
        }

        /// <summary>
        /// 主轴负载
        /// </summary>
        public class FocusSpMeter
        {
            public int Spload { get; } = -1;

            public int Spspeed { get; } = -1;

            public FocusSpMeter()
            { }

            public FocusSpMeter(int spmeter, int spspeed)
            {
                this.Spload = spmeter;
                this.Spspeed = spspeed;
            }
        }

        /// <summary>
        /// 主轴电流
        /// </summary>
        public class FocusCurrent
        {
            public short current { get; } = -1;
            public FocusCurrent()
            {

            }

            public FocusCurrent(short curent)
            {
                this.current = curent;
            }
        }

        /// <summary>
        /// 伺服负载
        /// </summary>
        public class FocusSvMeter
        {
            public int svload { get; } = -1;
            public FocusSvMeter()
            {

            }
            public FocusSvMeter(int loaddata)
            {
                this.svload = loaddata;
            }
        }

        /// <summary>
        /// 获取状态信息
        /// </summary>
        public class FocusStateInfo
        {
            public int aut { get; } = -1;

            public int manual { get; } = -1;

            public int run { get; } = -1;

            public int alarm { get; } = -1;

            public FocusStateInfo()
            {

            }

            public FocusStateInfo(int aut, int manual, int run, int alarm)
            {
                this.aut = aut;
                this.manual = manual;
                this.run = run;
                this.alarm = alarm;
            }
        }

        /// <summary>
        /// 设备状态
        /// </summary>
        public class FocusParam
        {
            public byte data { get; set; }

            public FocusParam()
            {

            }

            public FocusParam(byte data)
            {
                this.data = data;
            }
        }

        /// <summary>
        /// 刀具半径补偿
        /// </summary>
        public class FocusRadius
        {
            public long specifyRadius { get; set; }

            public FocusRadius()
            {

            }

            public FocusRadius(long radius)
            {
                this.specifyRadius = radius;
            }
        }

        /// <summary>
        /// 刀具寿命信息
        /// </summary>
        public class FocusToolLife
        {
            public long ToolInfo { get; set; }

            public FocusToolLife()
            {

            }

            public FocusToolLife(long toolinfo)
            {
                this.ToolInfo = toolinfo;
            }
        }

        public class FocusToolNum
        {
            public long nextTool { get; set; }

            public long usingTool { get; set; }

            public long selectTool { get; set; }

            public FocusToolNum()
            { }

            public FocusToolNum(long next, long useing, long select)
            {
                this.nextTool = next;
                this.usingTool = useing;
                this.selectTool = select;
            }
        }

        public class FocusToolGeom
        {
            public int lpos { get; set; }

            public int rpos { get; set; }

            public int upos { get; set; }

            public int gemopos { get; set; }

            public FocusToolGeom()
            {

            }

            public FocusToolGeom(int lpos, int rpos, int upos, int gemopos)
            {
                this.lpos = lpos;
                this.rpos = rpos;
                this.upos = upos;
                this.gemopos = gemopos;
            }
        }

        public class FocusProgramName
        {
            public string prgname { get; set; }

            public long Onum { get; set; }

            public FocusProgramName()
            {

            }

            public FocusProgramName(string name, long num)
            {
                this.prgname = name;
                this.Onum = num;
            }
        }

        public class FocusPmcRng
        {
            public char PmcData { get; set; }

            public short type_a { get; set; }

            public short type_d { get; set; }

            public short data_s { get; set; }

            public short data_e { get; set; }

            public FocusPmcRng()
            {

            }

            public FocusPmcRng(short type_a, short type_d, short data_s, short data_e, char pmcdata)
            {
                this.PmcData = pmcdata;
                this.type_a = type_a;
                this.type_d = type_d;
                this.data_s = data_s;
                this.data_e = data_e;
            }
        }

        #region 属性

        /// <summary>
        /// 获取当前机床的连接IP
        /// </summary>
        public string Machine_IP { get; }

        /// <summary>
        /// 获取当前机床的连接端口
        /// </summary>
        public ushort Machine_Port { get; }

        /// <summary>
        /// 获取当前机床的连接状态
        /// </summary>
        public bool Connected { get; private set; }

        /// <summary>
        /// 连接机床的句柄
        /// </summary>
        private ushort MachineHandle = 0;

        #endregion

        #region C++的DLL函数

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class SPEEDELM
        {
            public int data;       /* speed data */
            public short dec;        /* decimal position */
            public short unit;       /* data unit */
            public short disp;       /* display flag */
            public byte name;       /* name of data */
            public byte suff;       /* suffix */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class ODBSPEED
        {
            public SPEEDELM actf = new SPEEDELM();   /* actual feed rate */
            public SPEEDELM acts = new SPEEDELM();   /* actual spindle speed */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]

        public class ODBACT
        {
            public short[] dummy;    /* dummy */
            public int data;       /* the actual feed rate(F) */
        }

        public class LOADELM
        {
            public int data;
            public short dec;
            public short unit;
            public byte name;
            public byte suff1;
            public byte suff2;
            public byte reserve;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]

        public class ODBSPLOAD
        {
            public LOADELM spload;  /* spindle load meter data  载重计、载荷计*/
            public LOADELM spspeed; /* spindle motor data 主轴电机数据*/
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]

        public class ODBSVLOAD
        {
            public LOADELM svload;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]

        public class ODBST
        {
            public short dummy;
            public short aut;
            public short manual;
            public short run;
            public short edit;
            public short motion;
            public short mstb;
            public short emergency;
            public short write;
            public short labelskip;
            public short alarm;
            public short warning;
            short bettery;
        }



        /* Axis define */
#if FS30D
    public const short MAX_AXIS = 32;
#elif M_AXIS2
    public const short MAX_AXIS = 24;
#elif FS15D
    public const short MAX_AXIS = 10;
#else
        public const short MAX_AXIS = 8;
#endif

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class IODBPSD
        {
            public short data_no;              /* parameter number [存储要读取的参数数量] */
            public short type;                 /* upper byte:type [高位字节代表type] */
                                               /* lower byte:axis [低位字节代表轴号]*/
            public char cdata;                 /* bit/byte parameter  */
            public short idata;                /* word parameter */
            public long ldata;                 /* 2-word parameter */
            public char[] cdatas;              /* bit/byte parameter with axis*/
            public short[] idatas;             /* word parameter with axis */
            public long[] ldatas;              /* 2-word parameter with axis */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class POSELM
        {
            public int data;         /* position data */
            public short dec;        /* place of decimal point of position data */
            public short unit;       /* unit of position data */
            public short disp;       /* status of display */
            public char name;        /* axis name */
            public char suff;        /* axis name preffix */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class POSELMALL
        {
            public POSELM abs = new POSELM();
            public POSELM mach = new POSELM();
            public POSELM rel = new POSELM();
            public POSELM dist = new POSELM();
        }

#if M_AXIS2
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    public class ODBPOS
    {
        public POSELMALL p1=new POSELMALL();
        public POSELMALL p2=new POSELMALL();
        public POSELMALL p3=new POSELMALL();
        public POSELMALL p4=new POSELMALL();
        public POSELMALL p5=new POSELMALL();
        public POSELMALL p6=new POSELMALL();
        public POSELMALL p7=new POSELMALL();
        public POSELMALL p8=new POSELMALL();
        public POSELMALL p9=new POSELMALL();
        public POSELMALL p10=new POSELMALL();
        public POSELMALL p11=new POSELMALL();
        public POSELMALL p12=new POSELMALL();
        public POSELMALL p13=new POSELMALL();
        public POSELMALL p14=new POSELMALL();
        public POSELMALL p15=new POSELMALL();
        public POSELMALL p16=new POSELMALL();
        public POSELMALL p17=new POSELMALL();
        public POSELMALL p18=new POSELMALL();
        public POSELMALL p19=new POSELMALL();
        public POSELMALL p20=new POSELMALL();
        public POSELMALL p21=new POSELMALL();
        public POSELMALL p22=new POSELMALL();
        public POSELMALL p23=new POSELMALL();
        public POSELMALL p24=new POSELMALL();
        // In case of 24 axes.
        // if you need the more information, you must be add the member.
    }
#elif FS15D
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    public class ODBPOS
    {
        public POSELMALL p1=new POSELMALL();
        public POSELMALL p2=new POSELMALL();
        public POSELMALL p3=new POSELMALL();
        public POSELMALL p4=new POSELMALL();
        public POSELMALL p5=new POSELMALL();
        public POSELMALL p6=new POSELMALL();
        public POSELMALL p7=new POSELMALL();
        public POSELMALL p8=new POSELMALL();
        public POSELMALL p9=new POSELMALL();
        public POSELMALL p10=new POSELMALL();
        // In case of 10 axes.
        // if you need the more information, you must be add the member.
    }
#else
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class ODBPOS
        {
            public POSELMALL p1 = new POSELMALL();
            public POSELMALL p2 = new POSELMALL();
            public POSELMALL p3 = new POSELMALL();
            public POSELMALL p4 = new POSELMALL();
            public POSELMALL p5 = new POSELMALL();
            public POSELMALL p6 = new POSELMALL();
            public POSELMALL p7 = new POSELMALL();
            public POSELMALL p8 = new POSELMALL();
            // In case of 8 axes.
            // if you need the more information, you must be add the member.
        }
#endif
        public enum focus_ret
        {
            EW_PROTOCOL = (-17),           /* protocol error */
            EW_SOCKET = (-16),           /* Windows socket error */
            EW_NODLL = (-15),           /* DLL not exist error */
            EW_BUS = (-11),           /* bus error */
            EW_SYSTEM2 = (-10),           /* system error */
            EW_HSSB = (-9),           /* hssb communication error */
            EW_HANDLE = (-8),           /* Windows library handle error */
            EW_VERSION = (-7),           /* CNC/PMC version missmatch */
            EW_UNEXP = (-6),           /* abnormal error */
            EW_SYSTEM = (-5),           /* system error */
            EW_PARITY = (-4),           /* shared RAM parity error */
            EW_MMCSYS = (-3),           /* emm386 or mmcsys install error */
            EW_RESET = (-2),           /* reset or stop occured error */
            EW_BUSY = (-1),           /* busy error */
            EW_OK = 0,           /* no problem */
            EW_FUNC = 1,           /* command prepare error */
            EW_NOPMC = 1,           /* pmc not exist */
            EW_LENGTH = 2,           /* data block length error */
            EW_NUMBER = 3,           /* data number error */
            EW_RANGE = 3,           /* address range error */
            EW_ATTRIB = 4,           /* data attribute error */
            EW_TYPE = 4,           /* data type error */
            EW_DATA = 5,           /* data error */
            EW_NOOPT = 6,           /* no option error */
            EW_PROT = 7,           /* write protect error */
            EW_OVRFLOW = 8,           /* memory overflow error */
            EW_PARAM = 9,           /* cnc parameter not correct error */
            EW_BUFFER = 10,           /* buffer error */
            EW_PATH = 11,           /* path error */
            EW_MODE = 12,           /* cnc mode error */
            EW_REJECT = 13,           /* execution rejected error */
            EW_DTSRVR = 14,           /* data server error */
            EW_ALARM = 15,           /* alarm has been occurred */
            EW_STOP = 16,           /* CNC is not running */
            EW_PASSWD = 17,           /* protection data error */
                                      /*
                                          Result codes of DNC operation
                                      */
            DNC_NORMAL = (-1),           /* normal completed */
            DNC_CANCEL = (-32768),           /* DNC operation was canceled by CNC */
            DNC_OPENERR = (-514),           /* file open error */
            DNC_NOFILE = (-516),           /* file not found */
            DNC_READERR = (-517)              /* read error */
        };

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class FolderInfo
        {
            /// <summary>
            /// 文件夹数目
            /// </summary>
            public short dir_num;    /* directory */
            /// <summary>
            /// 文件数目
            /// </summary>
            public short file_num;   /* file */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class ODBTLIFE2
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public short[] dummy;  /* dummy */
            public int data;   /* data */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class IODBTGI4
        {
            /// <summary>
            /// 刀组号
            /// </summary>
            public short grp_no;

            /// <summary>
            /// 刀具号
            /// </summary>
            public int n_tool;

            /// <summary>
            /// 刀具寿命
            /// </summary>
            public int count_value;

            /// <summary>
            /// 刀具总寿命
            /// </summary>
            public int counter;

            /// <summary>
            /// 刀具寿命类型
            /// </summary>
            public int count_type;

            /// <summary>
            /// 可选择组号
            /// </summary>
            public int opt_grp_no;

            /// <summary>
            /// 复位寿命
            /// </summary>
            public int life_rest;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class ODBTLIFE4
        {
            public short data_no; /* Tool group number. [刀具组号]*/
            public short type;   /* Tool number. [刀具编号]*/
            public long data;    /* Cutter radius compensation number. (always 0 in T Series)[刀具半径补偿值(T Series一直为0)] */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class ODBTG
        {
            public short grp_num;
            public short[] dummy;
            public long n_tool;
            public long life;
            public long count;
            public class data
            {
                long tuse_num;
                long tool_num;
                long length_num;
                long radius_num;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class ODBUSEGR
        {
            public short data_no;       /* Not used */
            public short type;         /* Not used */
            public long next;          /* Number of tool group to be used next [下一个使用的刀具组号]*/
            public long use;           /* Number of tool group currently in use [当前使用的刀具组号]*/
            public long slc_t;          /* Number of tool group currently selected [当前选择的刀具组号]*/
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class IODBTLGEOM
        {
            public byte l_pot_num;         /* Left pot occupation unit */
            public byte r_pot_num;         /* Right pot occupation unit */
            public byte u_pot_num;         /* Upper pot occupation unit (30i/31i/32i only) */
            public byte d_pot_num;         /* Lower pot occupation unit (30i/31i/32i only) */
            public byte tl_geom_num;       /* Geometry A or B (30i/31i/32i only) */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class ODBEXEPrg
        {
            public string[] name;
            public long o_num;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public class IODBPMC
        {
            public short type_a;        /* Kind of PMC address */
            public short type_d;        /* Type of the PMC data */
            public short datano_s;      /* Start PMC address number */
            public short datano_e;      /* End PMC address number */
            public char[] cdata;        /* The PMC data(byte type) */
            public short[] Idata;       /*             (word type) */
            public long[] ldata;        /*             (long type) */
        }

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_allclibhndl3")]
        private static extern short cnc_allclibhndl3([In, MarshalAs(UnmanagedType.AsAny)] Object ip, ushort port, int timeout, out ushort FlibHndl);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_actf")]
        private static extern short cnc_actf(ushort FlibHndl, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBACT a);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_acts")]
        private static extern short cnc_acts(ushort FlibHndl, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBACT actualfeed);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdspmeter")]
        private static extern short cnc_rdspmeter(ushort FlibHndl, short type, ref short data_num, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBSPLOAD a);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdcurrent")]
        private static extern short cnc_rdcurrent(ushort FlibHndl, out short current);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdsvmeter")]
        private static extern short cnc_rdsvmeter(ushort FlibHndl, ref short datanum, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBSVLOAD s);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_statinfo")]
        private static extern short cnc_statinfo(ushort FlibHndl, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBST s);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdparam")]
        private static extern short cnc_rdparam(ushort FlibHndl, short number, short axis, short length, [Out, MarshalAs(UnmanagedType.LPStruct)] IODBPSD RealPm);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rd1radius")]
        private static extern short cnc_rd1radius(ushort FlibHndl, short grp_num, short tool_num, [Out, MarshalAs(UnmanagedType.LPStruct)]ODBTLIFE4 a);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rd2radius")]
        private static extern short cnc_rd2radius(ushort FlibHndl, short grp_num, short tool_num, [Out, MarshalAs(UnmanagedType.LPStruct)]ODBTLIFE4 b);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_t1info")]
        private static extern short cnc_t1info(ushort FlibHndl, short grp_num, short tool_num, [Out, MarshalAs(UnmanagedType.LPStruct)]ODBTLIFE4 c);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_t2info")]
        private static extern short cnc_t2info(ushort FlibHndl, short grp_num, short tuse_num, [Out, MarshalAs(UnmanagedType.LPStruct)]ODBTLIFE4 c);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdtoolgrp")]
        private static extern short cnc_rdtoolgrp(ushort FlibHndl, short grp_num, short tool_num, [Out, MarshalAs(UnmanagedType.LPStruct)]ODBTG d);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdusegrpid")]
        private static extern short cnc_rdusegrpid(ushort FlibHndl, [Out, MarshalAs(UnmanagedType.LPStruct)]ODBUSEGR e);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdtoolgeom_tlm")]
        private static extern short cnc_rdtoolgeom_tlm(ushort FlibHndl, short snum, ref short datanum, [Out, MarshalAs(UnmanagedType.LPStruct)]IODBTLGEOM f);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_pdf_rdmain")]
        private static extern short cnc_pdf_rdmain(ushort FlibHndl, out string filepath);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_exeprgname")]
        private static extern short cnc_exeprgname(ushort FlibHndl, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBEXEPrg d);

        [DllImport("FWLIB32.dll", EntryPoint = "pmc_rdpmcrng")]
        private static extern short pmc_rdpmcrng(ushort FlibHndl, short adr_type, short data_type, short s_number, short e_number, short length, IODBPMC g);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdexecprog")]
        private static extern short cnc_rdexecprog(ushort FlibHndl, ref short length, out short blknum, out char data);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_freelibhndl")]
        private static extern short cnc_freelibhndl(ushort FlibHndl);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdposition")]
        private static extern short cnc_rdposition(ushort FlibHndl, short a, ref short b, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBPOS c);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdspeed")]
        private static extern short cnc_rdspeed(ushort FlibHndl, short a, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBSPEED b);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_upstart4")]
        private static extern short cnc_upstart4(ushort FlibHndl, short a, [Out, MarshalAs(UnmanagedType.AsAny)] Object b);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_upload4")]
        private static extern short cnc_upload4(ushort FlibHndl, ref int a, [Out, MarshalAs(UnmanagedType.AsAny)] Object b);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_upend4")]
        private static extern short cnc_upend4(ushort FlibHndl);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_dwnstart4")]
        private static extern short cnc_dwnstart4(ushort FlibHndl, short a, [In, MarshalAs(UnmanagedType.AsAny)] Object b);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_download4")]
        private static extern short cnc_download4(ushort FlibHndl, ref int a, [In, MarshalAs(UnmanagedType.AsAny)] Object b);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_dwnend4")]
        private static extern short cnc_dwnend4(ushort FlibHndl);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_clralm")]
        private static extern short cnc_clralm(ushort FlibHndl, short a);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_pdf_del")]
        private static extern short cnc_pdf_del(ushort FlibHndl, [In, MarshalAs(UnmanagedType.AsAny)] Object a);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdpdf_subdirn")]
        private static extern short cnc_rdpdf_subdirn(ushort FlibHndl, [In, MarshalAs(UnmanagedType.AsAny)] Object a, [Out, MarshalAs(UnmanagedType.LPStruct)] FolderInfo b);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_pdf_rdmain")]
        private static extern short cnc_pdf_rdmain(ushort FlibHndl, [Out, MarshalAs(UnmanagedType.AsAny)] Object a);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_pdf_slctmain")]
        private static extern short cnc_pdf_slctmain(ushort FlibHndl, [In, MarshalAs(UnmanagedType.AsAny)] Object a);

        [DllImport("FWLIB32.dll", EntryPoint = "cnc_rdngrp")]
        public static extern short cnc_rdngrp(ushort FlibHndl, [Out, MarshalAs(UnmanagedType.LPStruct)] ODBTLIFE2 a);
        [
            DllImport("FWLIB32.dll", EntryPoint = "cnc_rdgrpinfo4")]
        public static extern short cnc_rdgrpinfo4(ushort FlibHndl, short a, short b, short c, out short d, [Out, MarshalAs(UnmanagedType.LPStruct)] IODBTGI4 e);

        #endregion

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="Machine_IP">需要连接机床的IP地址</param>
        /// <param name="Machine_Port">需要连接机床的端口</param>
        public Focus(string Machine_IP, ushort Machine_Port = 8193)
        {
            this.Machine_IP = Machine_IP;
            this.Machine_Port = Machine_Port;
        }

        /// <summary>
        /// 连接机床
        /// </summary>
        /// <returns></returns>
        public FocusMsg Connect()
        {
            short value = cnc_allclibhndl3(Machine_IP, Machine_Port, 3, out MachineHandle);//创建机床连接
            switch (value)
            {
                case 0: Connected = true; return new FocusMsg(value, "连接成功");
                case -16: Connected = false; return new FocusMsg(value, "连接通信失败");
                case -15: Connected = false; return new FocusMsg(value, "缺少CNC的DLL文件");
                case -8: Connected = false; return new FocusMsg(value, "分配句柄号失败");
                default: Connected = false; return new FocusMsg(value, "未知错误");
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <returns></returns>
        public bool DisConnect()
        {
            short value = cnc_freelibhndl(MachineHandle);
            if (value == 0)
            {
                Connected = false;
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// 获取机床状态
        /// </summary>
        /// <returns></returns>
        public FocusStateInfo GetStateInfo()
        {
            FocusStateInfo value = new FocusStateInfo();
            ODBST s = new ODBST();
            short ret = cnc_statinfo(MachineHandle, s);
            if (ret == 0)
                value = new FocusStateInfo(s.aut, s.manual, s.run, s.alarm);
            return value;
        }

        /// <summary>
        /// 获取机床状态
        /// </summary>
        /// <param name="number">参数名称</param>
        /// <param name="axis">主轴编号</param>
        /// <param name="length">长度</param>
        /// <returns></returns>
        public FocusParam GetParam(short number, short axis, short length)
        {
            FocusParam param = new FocusParam();
            IODBPSD s = new IODBPSD();
            short ret = cnc_rdparam(MachineHandle, number, axis, length, s);
            if (ret == 0)
                param = new FocusParam(Convert.ToByte(s.cdatas[axis]));
            return param;
        }

        /// <summary>
        /// 获取使用刀具信息
        /// </summary>
        /// <returns></returns>
        public FocusToolNum GetToolNum()
        {
            FocusToolNum value = new FocusToolNum();
            ODBUSEGR s = new ODBUSEGR();
            short ret = cnc_rdusegrpid(MachineHandle, s);
            if (ret == 0)
                value = new FocusToolNum(s.next, s.use, s.slc_t);
            return value;
        }

        /// <summary>
        /// 获取程序路径
        /// </summary>
        /// <returns></returns>
        public string GetPrgPath()
        {
            string path = null;
            short ret = cnc_pdf_rdmain(MachineHandle, out path);
            return path;
        }

        /// <summary>
        /// 获取正在执行的程序名称和正在执行的O程序号
        /// </summary>
        /// <returns></returns>
        public FocusProgramName GetPrgName()
        {
            FocusProgramName Name = new FocusProgramName();
            ODBEXEPrg s = new ODBEXEPrg();
            short ret = cnc_exeprgname(MachineHandle, s);
            if (ret == 0)
            {
                Name = new FocusProgramName(s.name[36], s.o_num);
            }
            return Name;
        }

        /// <summary>
        /// 下载机床程序到本地
        /// 0：下载成功
        /// -1：启动下载失败，参数错误或机床程序文件不存在
        /// -2：下载文件时出错
        /// </summary>
        /// <param name="LocalFolder">本地保存程序文件的文件夹路径</param>
        /// <param name="MachineFile">机床程序文件的路径</param>
        /// <returns></returns>
        public int Download_Program(string LocalFolder, string MachineFile)
        {
            cnc_upend4(MachineHandle);//停止上一次下载
            short value = cnc_upstart4(MachineHandle, 0, MachineFile);//下载程序
            if (value != 0)
                return -1;//
            FileInfo file = new FileInfo(MachineFile);

            StreamWriter sw = new StreamWriter(LocalFolder + "\\" + file.Name, false);//覆盖文件
            //下载文件
            int lenth = 100 * 1024;//下载速度
            char[] buf = new char[lenth + 1];
            do
            {

                lenth = 100 * 1024;//下载速度
                value = cnc_upload4(MachineHandle, ref lenth, buf);
                if (value == (short)focus_ret.EW_BUFFER)
                    continue;
                if (value == 0)
                {
                    string sb = new string(buf, 0, lenth);
                    sb.Trim('\n');
                    sb = sb.Replace("\n", "\r\n");//需替换字符，否则没有换行
                    sw.Write(sb);
                }
                else
                    break;
                if (buf[lenth - 1] == '%')
                    break;
            } while (value == 0 || value == (short)focus_ret.EW_BUFFER);
            sw.Close();
            sw.Dispose();
            short tag = cnc_upend4(MachineHandle);//停止下载
            if (value == 0 && tag == 0)//下载成功且停止成功
                return 0;
            return -2;//下载文件时出错
        }

        /// <summary>
        /// 上传本地程序到机床
        /// 0：上传成功
        /// -1：机床通信报警（需调用Clear_Warm清除警告）、机床文件夹不存在或机床拒绝请求
        /// -2：上传程序过程中出错
        /// </summary>
        /// <param name="LocalFile">本地程序文件</param>
        /// <param name="MachineFolder">需要上传程序到机床的文件夹</param>
        /// <returns></returns>
        public int Upload_Program(string LocalFile, string MachineFolder)
        {
            cnc_dwnend4(MachineHandle);//停止上一次上传
            StreamReader sr = new StreamReader(LocalFile);
            string data = sr.ReadToEnd();//传输的字符格式
            sr.Close();
            sr.Dispose();
            try
            {
                int start = data.IndexOf('%');
                int end = data.LastIndexOf('%');
                data = data.Substring(start + 1, end - start);//裁剪内容
            }
            catch { }
            FileInfo file = new FileInfo(LocalFile);
            data = data.Replace("\r\n", "\n");//需要把换行替换
            data = "\n<" + file.Name + ">\n" + data.Trim('\n').TrimEnd('%') + "%";//上传程序名称 + 程序内容
            short value = cnc_dwnstart4(MachineHandle, 0, MachineFolder);//开始上传
            if (value != 0)
                return -1;
            int lenth = data.Length;
            while (lenth > 0)
            {
                int n = lenth;
                value = cnc_download4(MachineHandle, ref n, data);//开始上传内容
                if (value == (short)focus_ret.EW_BUFFER)//缓冲错误，重新传输
                    continue;//继续上传
                if (value == 0)
                {
                    data = data.Substring(n);//裁剪已上传的字符，上传剩余的字符
                    lenth -= n;
                }
                else
                    break;
            }
            short teg = cnc_dwnend4(MachineHandle);//停止上传
            if (value == 0 && teg == 0)
                return 0;//上传成功
            return -2;
        }

        /// <summary>
        /// 删除机床内程序
        /// </summary>
        /// <param name="MachineFile">需要删除的机床程序文件</param>
        /// <returns></returns>
        public bool Delete_Program(string MachineFile)
        {
            short value = cnc_pdf_del(MachineHandle, MachineFile);//删除指定程序文件或文件夹，文件夹需要在最后添加/，不加则认为是文件
            switch (value)
            {
                case 0: return true;//删除成功
                case 5: return false;//文件/文件夹数据错误
                case 7: return false;//写保护
                case 13: return false;//CNC拒绝
                default: return false;//未知错误
            }
        }

        /// <summary>
        /// 获取机床选定的加工程序
        /// </summary>
        /// <returns></returns>
        public string Get_MachineMainProgram()
        {
            char[] data = new char[244];
            short value = cnc_pdf_rdmain(MachineHandle, data);
            if (value == 0)
                return new string(data);
            return null;
        }

        /// <summary>
        /// 选定机床加工程序
        /// </summary>
        /// <param name="MachineFile">机床内已有的加工程序文件</param>
        /// <returns></returns>
        public FocusMsg Set_MachineMainProgram(string MachineFile)
        {
            short value = cnc_pdf_slctmain(MachineHandle, MachineFile);
            switch (value)
            {
                case 0: return new FocusMsg(0, "执行成功");
                case 5: return new FocusMsg(5, "数据错误");
                case 12: return new FocusMsg(12, "CNC模式错误：仅在EDIT和MEM模式下有效");
                default: return new FocusMsg(-1, "未知错误");
            }
        }
    }
}
