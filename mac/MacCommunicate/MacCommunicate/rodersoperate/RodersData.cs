using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MacCommunicate.Roders;

namespace MacCommunicate
{
    public class RodersData
    {
        #region 函数返回数据

        /// <summary>
        /// 状态报告类
        /// </summary>
        public class StatuReport
        {
            /// <summary>
            /// 头部
            /// </summary>
            public Header Header { get; }

            /// <summary>
            /// 获取数据最后更新的时间
            /// </summary>
            public string UpdateTime { get; }

            /// <summary>
            /// 获取自动模式状态
            /// </summary>
            public bool Auto { get; } = false;
            /// <summary>
            /// 获取自动更改状态
            /// </summary>
            public bool AutoChange { get; } = false;
            /// <summary>
            /// 获取加工状态
            /// </summary>
            public bool Active { get; } = false;

            public bool ECO { get; } = false;
            /// <summary>
            /// 获取
            /// </summary>
            public int Override { get; } = 0;
            /// <summary>
            /// 获取
            /// </summary>
            public bool FileTransfer { get; } = false;
            /// <summary>
            /// 获取
            /// </summary>
            public bool NewMaintenance { get; } = false;
            /// <summary>
            /// 获取
            /// </summary>
            public bool NewPaldata { get; } = false;
            /// <summary>
            /// 获取
            /// </summary>
            public bool NewToolData { get; } = false;
            /// <summary>
            /// 获取
            /// </summary>
            public string Handl_Interface { get; } = "OFF";
            /// <summary>
            /// 获取
            /// </summary>
            public string Handl_Mode { get; } = "";
            /// <summary>
            /// 获取
            /// </summary>
            public string Handl_State { get; } = "";
            /// <summary>
            /// 获取错误信息：错误编号+内容
            /// </summary>
            public string Errors { get; } = null;
            /// <summary>
            /// 获取
            /// </summary>
            public string PalDoor { get; } = "";
            /// <summary>
            /// 获取刀具门状态
            /// </summary>
            public string ToolDoor { get; } = "";

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Header"></param>
            /// <param name="Data"></param>
            public StatuReport(Header Header, string UpdateTime, bool Auto, bool AutoChange, bool Active, bool ECO, bool FileTransfer, bool NewmAintenance, bool NewPaldata,
                int Override,
                string Handl_Interface, string Handl_Mode, string Handl_State, string Errors, string PalDoor, string ToolDoor)
            {
                this.UpdateTime = UpdateTime;
                this.Header = Header;
                this.Auto = Auto;
                this.AutoChange = AutoChange;
                this.Active = Active;
                this.ECO = ECO;
                this.FileTransfer = FileTransfer;
                this.NewMaintenance = NewmAintenance;
                this.NewPaldata = NewPaldata;
                this.Override = Override;
                this.Handl_Interface = Handl_Interface;
                this.Handl_Mode = Handl_Mode;
                this.Handl_State = Handl_State;
                this.Errors = Errors;
                this.PalDoor = PalDoor;
                this.ToolDoor = ToolDoor;
            }
        }

        /// <summary>
        /// 错误信息提示
        /// </summary>
        public class Error
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="ErrorNum"></param>
            /// <param name="ErrorMsg"></param>
            /// <param name="ErrorTime"></param>
            public Error(int ErrorNum, string ErrorMsg)
            {
                this.ErrorNum = ErrorNum;
                this.ErrorMsg = ErrorMsg;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="ErrorNum"></param>
            /// <param name="ErrorMsg"></param>
            /// <param name="ErrorTime"></param>
            public Error(string ErrorNum, string ErrorMsg)
            {
                try
                {
                    this.ErrorNum = Convert.ToInt32(ErrorNum);
                }
                catch { }
                this.ErrorMsg = ErrorMsg;
            }

            /// <summary>
            /// 错误代号
            /// </summary>
            public int ErrorNum { get; }

            /// <summary>
            /// 错误信息
            /// </summary>
            public string ErrorMsg { get; }

            /// <summary>
            /// 发送错误的时间
            /// </summary>
            public DateTime ErrorTime = DateTime.Now;
        }

        /// <summary>
        /// DIS命令返回的数据
        /// </summary>
        public class Rec_DIS
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_DIS(bool Ret, Header Header)
            {
                this.Ret = Ret;
                this.Header = Header;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_DIS(string Ret, Header Header)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }
        }

        /// <summary>
        /// WDREQ命令返回的数据
        /// </summary>
        public class Rec_WDREQ
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            public Rec_WDREQ(bool Ret, Header Header)
            {
                this.Header = Header;
                this.Ret = Ret;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            public Rec_WDREQ(string Ret, Header Header)
            {
                this.Header = Header;
                this.Ret = Ret == "OK" ? true : false;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }
        }

        /// <summary>
        /// PTREQ命令返回的数据
        /// </summary>
        public class Rec_PTREQ
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Chuck"></param>
            /// <param name="Place"></param>
            public Rec_PTREQ(bool Ret, Header Header, PtreqData_Chuck[] Chuck, Place[] Place)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Chuck = Chuck;
                this.Place = Place;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Chuck"></param>
            /// <param name="Place"></param>
            public Rec_PTREQ(string Ret, Header Header, PtreqData_Chuck[] Chuck, Place[] Place)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Chuck = Chuck;
                this.Place = Place;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// chuck部分数据
            /// </summary>
            public PtreqData_Chuck[] Chuck;

            /// <summary>
            /// place部分数据
            /// </summary>
            public Place[] Place;

            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }
        }

        /// <summary>
        /// COREQ命令返回的数据
        /// </summary>
        public class Rec_COREQ
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Chuck"></param>
            /// <param name="Offset"></param>
            public Rec_COREQ(bool Ret, Header Header, string Chuck, Offset Offset, Error Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Chuck = Chuck;
                this.Offset = Offset;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Chuck"></param>
            /// <param name="Offset"></param>
            public Rec_COREQ(string Ret, Header Header, string Chuck, Offset Offset, Error Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Chuck = Chuck;
                this.Offset = Offset;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 获取错误信息
            /// </summary>
            public Error Error { get; }

            /// <summary>
            /// chuck number
            /// </summary>
            public string Chuck { get; }

            /// <summary>
            /// result of internal offset measurement
            /// </summary>
            public Offset Offset;
        }
        /// <summary>
        /// TTREQ命令返回的数据
        /// </summary>
        public class Rec_TTREQ
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Place"></param>
            public Rec_TTREQ(bool Ret, Header Header, Place[] Place)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Place = Place;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Place"></param>
            public Rec_TTREQ(string Ret, Header Header, Place[] Place)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Place = Place;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// place部分数据
            /// </summary>
            public Place[] Place;
        }
        /// <summary>
        /// PLREP命令返回的数据
        /// </summary>
        public class Rec_PLREP
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_PLREP(bool Ret, Header Header)
            {
                this.Ret = Ret;
                this.Header = Header;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_PLREP(string Ret, Header Header)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }
        }
        /// <summary>
        /// PTREP命令返回的数据
        /// </summary>
        public class Rec_PTREP
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_PTREP(bool Ret, Header Header)
            {
                this.Ret = Ret;
                this.Header = Header;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_PTREP(string Ret, Header Header)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }
        }
        /// <summary>
        /// TTREP命令返回的数据
        /// </summary>
        public class Rec_TTREP
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_TTREP(bool Ret, Header Header)
            {
                this.Ret = Ret;
                this.Header = Header;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_TTREP(string Ret, Header Header)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
            }
            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }
        }

        /// <summary>
        /// RUN命令返回的数据
        /// </summary>
        public class Rec_RUN
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_RUN(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_RUN(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; } = null;
        }

        /// <summary>
        /// ECO命令返回的数据
        /// </summary>
        public class Rec_ECO
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_ECO(bool Ret, Header Header)
            {
                this.Ret = Ret;
                this.Header = Header;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            public Rec_ECO(string Ret, Header Header)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }
        }

        /// <summary>
        /// NCLOAD命令返回的数据
        /// </summary>
        public class Rec_NCLOAD
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCLOAD(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCLOAD(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; } = null;
        }

        /// <summary>
        /// NCGET命令返回的数据
        /// </summary>
        public class Rec_NCGET
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCGET(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCGET(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; } = null;
        }

        /// <summary>
        /// NCDEL命令返回的数据
        /// </summary>
        public class Rec_NCDEL
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCDEL(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCDEL(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; } = null;
        }

        /// <summary>
        /// NCLIST命令返回的数据
        /// </summary>
        public class Rec_NCLIST
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCLIST(bool Ret, Header Header, List<string> Files, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Files = Files;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_NCLIST(string Ret, Header Header, List<string> Files, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Files = Files;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;
            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; } = null;

            /// <summary>
            /// 所有文件列表
            /// </summary>
            public List<string> Files = new List<string> { };
        }

        /// <summary>
        /// CHANGEPOS命令返回的数据
        /// </summary>
        public class Rec_CHANGEPOS
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_CHANGEPOS(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_CHANGEPOS(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// PDOPEN命令返回的数据
        /// </summary>
        public class Rec_PDOPEN
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_PDOPEN(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_PDOPEN(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// PDCLOSE命令返回的数据
        /// </summary>
        public class Rec_PDCLOSE
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_PDCLOSE(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_PDCLOSE(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// TDOPEN命令返回的数据
        /// </summary>
        public class Rec_TDOPEN
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_TDOPEN(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_TDOPEN(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// TDCLOSE命令返回的数据
        /// </summary>
        public class Rec_TDCLOSE
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_TDCLOSE(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_TDCLOSE(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// TTLA命令返回的数据
        /// </summary>
        public class Rec_TTLA
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_TTLA(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_TTLA(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// CHANGE命令返回的数据
        /// </summary>
        public class Rec_CHANGE
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_CHANGE(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_CHANGE(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// LOCK命令返回的数据
        /// </summary>
        public class Rec_LOCK
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_LOCK(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_LOCK(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }

        /// <summary>
        /// ABORT命令返回的数据
        /// </summary>
        public class Rec_ABORT
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_ABORT(bool Ret, Header Header, string Error = null)
            {
                this.Ret = Ret;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret"></param>
            /// <param name="Header"></param>
            /// <param name="Error"></param>
            public Rec_ABORT(string Ret, Header Header, string Error = null)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Header = Header;
                this.Error = Error;
            }

            /// <summary>
            /// 头数据部分
            /// </summary>
            public Header Header;

            /// <summary>
            /// 获取本命令执行的结果，
            /// 如果为NOK，详细查看Error属性
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 命令执行失败的原因
            /// </summary>
            public string Error { get; set; } = null;
        }


        #endregion

        #region 基数据

        /// <summary>
        /// 头部数据，每一个命令必须返回的信息
        /// </summary>
        public class Header
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Cmd">本数据的命令回复</param>
            /// <param name="Ver">版本Version</param>
            /// <param name="ID">本通信的ID</param>
            public Header(string Cmd, float Ver, int ID)
            {
                this.Cmd = Cmd;
                this.Ver = Ver;
                this.ID = ID;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Cmd">本数据的命令回复</param>
            /// <param name="Ver_Float">版本Version的字符串形式</param>
            /// <param name="ID_Int">本通信的ID的字符串形式</param>
            public Header(string Cmd, string Ver_Float, string ID_Int)
            {
                this.Cmd = Cmd;
                try
                {
                    this.Ver = (float)Convert.ToDouble(Ver_Float);
                    this.ID = Convert.ToInt16(ID_Int);
                }
                catch { }
            }

            /// <summary>
            /// 获取本数据的命令回复
            /// </summary>
            public string Cmd { get; } = null;
            /// <summary>
            /// 获取Version
            /// </summary>
            public float Ver { get; } = 0;
            /// <summary>
            /// 获取本通信的ID
            /// </summary>
            public int ID { get; } = 0;
        }

        #region 发送CON连接命令反馈数据

        /// <summary>
        /// 发送CON连接命令反馈的数据
        /// </summary>
        public class ConData
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret">本命令执行的结果</param>
            /// <param name="Name">机床名称</param>
            /// <param name="Type">客户端类型： RMS_MAC，RMS_MAG</param>
            public ConData(bool Ret, string Name, string Type)
            {
                this.Ret = Ret;
                this.Name = Name;
                this.Type = Type;
            }

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Ret">本命令执行的结果：OK或NOK</param>
            /// <param name="Name">机床名称</param>
            /// <param name="Type">客户端类型： RMS_MAC，RMS_MAG</param>
            public ConData(string Ret, string Name, string Type)
            {
                this.Ret = Ret == "OK" ? true : false;
                this.Name = Name;
                this.Type = Type;
            }

            /// <summary>
            /// 获取本命令执行的结果
            /// </summary>
            public bool Ret { get; }

            /// <summary>
            /// 机床名称
            /// </summary>
            public string Name;

            /// <summary>
            /// 客户端类型：RMS_MAC，RMS_MAG
            /// </summary>
            public string Type;
        }

        #endregion


        #region 发送PTREQ命令反馈数据

        /// <summary>
        /// PTREQ命令反馈的chuck部分数据
        /// </summary>
        public class PtreqData_Chuck
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="mac"></param>
            /// <param name="pos"></param>
            /// <param name="state"></param>
            /// <param name="pos_org"></param>
            /// <param name="id"></param>
            public PtreqData_Chuck(string mac, string pos, string state, string pos_org, string id)
            {
                this.mac = mac;
                this.pos = pos;
                this.state = state;
                this.pos_org = pos_org;
                this.id = id;
            }

            /// <summary>
            /// opt. RMSMag: machine, 1..n/name
            /// </summary>
            public string mac { get; }
            /// <summary>
            /// chuck number, 1..n
            /// </summary>
            public string pos { get; }
            /// <summary>
            /// 状态：LOCKED，EMPTY，FULL，opt. FULL
            /// </summary>
            public string state { get; }
            /// <summary>
            /// original magazine position, 1..n
            /// </summary>
            public string pos_org { get; }
            /// <summary>
            /// 芯片ID
            /// </summary>
            public string id { get; }
        }


        #endregion


        /// <summary>
        /// Objects部分数据
        /// </summary>
        public class Cmd_Object
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Name">名称</param>
            /// <param name="Comment">name (more than one workpiece on a pallet)</param>
            /// <param name="ID">ID</param>
            /// <param name="x">x轴坐标</param>
            /// <param name="y">y轴坐标</param>
            /// <param name="z">z轴坐标</param>
            /// <param name="c">c轴坐标</param>
            public Cmd_Object(string Name, string Comment, int ID, double x, double y, double z, double c)
            {
                this.Name = Name;
                this.Comment = Comment;
                this.ID = ID;
                this.x = x;
                this.y = y;
                this.z = z;
                this.c = c;
            }

            /// <summary>
            /// 获取名称
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// name (more than one workpiece on a pallet)
            /// </summary>
            public string Comment { get; }

            /// <summary>
            /// 获取ID
            /// </summary>
            public int ID { get; }

            /// <summary>
            /// 获取x轴坐标
            /// </summary>
            public double x { get; }

            /// <summary>
            /// 获取y轴坐标
            /// </summary>
            public double y { get; }

            /// <summary>
            /// 获取z轴坐标
            /// </summary>
            public double z { get; }

            /// <summary>
            /// 获取c轴坐标
            /// </summary>
            public double c { get; }
        }

        /// <summary>
        /// Pallet部分数据
        /// </summary>
        public class Pallet
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="ID"></param>
            /// <param name="Objects"></param>
            public Pallet(int ChipID, List<Cmd_Object> Objects)
            {
                this.ChipID = ChipID;
                this.Objects = Objects;
            }

            /// <summary>
            /// ChipID
            /// </summary>
            public int ChipID { get; }

            /// <summary>
            /// 
            /// </summary>
            public List<Cmd_Object> Objects;
        }

        /// <summary>
        /// Chuck部分数据
        /// </summary>
        public class Chuck
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Pos"></param>
            /// <param name="State"></param>
            /// <param name="SubPos"></param>
            /// <param name="Pos_Org"></param>
            /// <param name="ChipID"></param>
            /// <param name="Objects"></param>
            public Chuck(int Pos, State State, SubChuck SubPos, int Pos_Org, int ChipID, List<Cmd_Object> Objects)
            {
                this.Pos = Pos;
                this.State = State;
                this.SubPos = SubPos;
                this.Pos_Org = Pos_Org;
                this.ChipID = ChipID;
                this.Objects = Objects;
            }

            /// <summary>
            /// 
            /// </summary>
            public int Pos { get; }

            /// <summary>
            /// loading state: LOCKED, EMPTY, FULL
            /// </summary>
            public State State { get; }

            /// <summary>
            /// subchuck, 0/90/180/270
            /// </summary>
            public SubChuck SubPos { get; }

            /// <summary>
            /// 
            /// </summary>
            public int Pos_Org { get; }

            /// <summary>
            /// ChipID
            /// </summary>
            public int ChipID { get; }

            /// <summary>
            /// 
            /// </summary>
            public List<Cmd_Object> Objects;
        }

        /// <summary>
        /// place数据
        /// </summary>
        public class Place
        {

            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Pos"></param>
            /// <param name="Sta"></param>
            /// <param name="ChipID"></param>
            /// <param name="type"></param>
            /// <param name="objecttype"></param>
            /// <param name="toolstate"></param>
            /// <param name="length"></param>
            /// <param name="diameter"></param>
            public Place(string Pos, string Sta, string ChipID, string type, string objecttype, string toolstate, string length, string diameter)
            {
                this.Pos = Convert.ToInt16(Pos);
                this.State = State.EMPTY;
                if (Sta == "LOCKED")
                    this.State = State.LOCKED;
                else if (Sta == "FULL")
                    this.State = State.FULL;
                this.ChipID = Convert.ToInt32(ChipID);
                this.type = type;
                this.objecttype = objecttype;
                this.toolstate = toolstate;
                this.length = length;
                this.diameter = diameter;
            }

            /// <summary>
            /// magazine position, 1..n
            /// </summary>
            public int Pos { get; }
            /// <summary>
            /// 状态：LOCKED，EMPTY，FULL，opt. FULL
            /// </summary>
            public State State { get; }
            /// <summary>
            /// ChipID
            /// </summary>
            public int ChipID { get; }

            public string type { get; }

            public string objecttype { get; }

            public string toolstate { get; }

            public string length { get; }

            public string diameter { get; }
        }

        /// <summary>
        /// Place部分数据
        /// </summary>
        public class Places
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Pos"></param>
            /// <param name="State"></param>
            /// <param name="ChipID"></param>
            /// <param name="Objects"></param>
            public Places(int Pos, State State, int ChipID, List<Cmd_Object> Objects)
            {
                this.Pos = Pos;
                this.State = State;
                this.ChipID = ChipID;
                this.Objects = Objects;
            }

            /// <summary>
            /// magazine position, 1..n
            /// </summary>
            public int Pos { get; }
            /// <summary>
            /// 状态：LOCKED，EMPTY，FULL，opt. FULL
            /// </summary>
            public State State { get; }
            /// <summary>
            /// ChipID
            /// </summary>
            public int ChipID { get; }

            /// <summary>
            /// 
            /// </summary>
            public List<Cmd_Object> Objects;
        }

        /// <summary>
        /// 坐标
        /// </summary>
        public class Offset
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="x">x轴坐标</param>
            /// <param name="y">y轴坐标</param>
            /// <param name="z">z轴坐标</param>
            /// <param name="c">c轴坐标</param>
            public Offset(double x, double y, double z, double c)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.c = c;
            }
            /// <summary>
            /// 获取x轴坐标
            /// </summary>
            public double x { get; }
            /// <summary>
            /// 获取y轴坐标
            /// </summary>
            public double y { get; }
            /// <summary>
            /// 获取z轴坐标
            /// </summary>
            public double z { get; }
            /// <summary>
            /// 获取c轴坐标
            /// </summary>
            public double c { get; }
        }

        /// <summary>
        /// Piece
        /// </summary>
        public class Piece
        {
            /// <summary>
            /// 构造函数
            /// </summary>
            /// <param name="Pos"></param>
            /// <param name="Name"></param>
            /// <param name="Chuck"></param>
            /// <param name="SubChuck"></param>
            /// <param name="Zdir"></param>
            /// <param name="Zrot"></param>
            public Piece(int Pos, string Name, int Chuck, SubChuck SubChuck, Zdir Zdir, Zrot Zrot)
            {
                this.Pos = Pos;
                this.Name = Name;
                this.Chuck = Chuck;
                this.SubChuck = SubChuck;
                this.Zdir = Zdir;
                this.Zrot = Zrot;
            }

            /// <summary>
            /// pallet’s original magazine position, 1..n
            /// </summary>
            public int Pos { get; }

            /// <summary>
            /// name of piece on pallet, if more than one
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// chuck, 1..n
            /// </summary>
            public int Chuck { get; }

            /// <summary>
            /// opt.: subchuck, 0/90/180/270
            /// </summary>
            public SubChuck SubChuck { get; }

            /// <summary>
            /// inclination of Z axis of pallet chuck [int] 0°/ 90°
            /// </summary>
            public Zdir Zdir { get; }

            /// <summary>
            /// rotation of pallet around Z axis of pallet chuck [int] 0° / 90° / 180° / 270°
            /// </summary>
            public Zrot Zrot { get; }
        }



        #endregion
    }
}
