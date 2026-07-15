using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MacCommunicate;
using static MacCommunicate.RodersData;

namespace prepare_material
{
    public class Macoperate
    {
        Roders_Server rodersserver = null;
        public string LocalPath = Directory.GetCurrentDirectory().Trim('\\') + "\\";
        string ip = ConfigurationManager.AppSettings["mac1"];
        
        #region 机床通讯函数

        /// <summary>
        /// 创建服务器
        /// </summary>
        public void CreateServer()
        {
            rodersserver = Roders_Server.Instance;
            rodersserver.MachineConnected += new Roders_Server.Machine_Connected(this.Machine_Connect);
            rodersserver.Listen_Machine_Thread(3450);
        }

        /// <summary>
        /// 获取本机IPV4地址
        /// </summary>
        /// <returns></returns>
        public string GetLocalIp()
        {
            List<string> IpList = new List<string>();
            string ip = null;
            string localname = Dns.GetHostName();//获取本机名
            IPAddress[] iPAddresses = Dns.GetHostAddresses(localname);//获取本地地址
            List<IPAddress> Values = new List<IPAddress> { };
            foreach (IPAddress address in iPAddresses)
            {
                if (Char.IsDigit(address.ToString()[0]))
                    Values.Add(address);
            }
            foreach (IPAddress iP in Values)
            {
                if (iP.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)//获取IPV4地址
                {
                    IpList.Add(iP.ToString());
                }
            }
            if (IpList.Count > 0)
                ip = IpList[0];
            return ip;
        }


        /// <summary>
        /// 机床连接触发事件
        /// </summary>
        /// <param name="Machine"></param>
        public void Machine_Connect(Roders Machine)
        {
            Machine.ReportRecieved += new Roders._ReportRecieved(ReportRec);
            Machine.Error_Event += new Roders._ErrorEvent(ErrorEvent);
            try
            {
      
            }
            finally
            {

            }
        }


        /// <summary>
        /// 状态端口事件
        /// </summary>
        /// <param name="str"></param>
        /// <param name="re"></param>
        public void ReportRec(string str, StatuReport sr, Roders Machine)
        {
            try
            {
                string cmd =sr.Header.Ver+","+ sr.Header.ID + "," + sr.Auto + "," + sr.AutoChange + "," + sr.Active + "," + sr.ECO + "," + sr.FileTransfer + "," + sr.NewMaintenance + "," + sr.NewPaldata + "," +
                    sr.Override + "," + sr.Handl_Interface + "," + sr.Handl_Mode + "," + sr.Handl_State + "," + sr.Errors + "," + sr.PalDoor + "," + sr.ToolDoor + "," + sr.UpdateTime + "," + Machine.MachineEndPoint.Address.ToString();
            }
            catch (Exception ex)
            {

            }
        }


        /// <summary>
        /// 机床错误触发事件
        /// </summary>
        /// <param name="Machine"></param>
        /// <param name="ErrMsg"></param>
        public void ErrorEvent(Roders Machine, Roders_ErrMsg[] ErrMsg)
        {
            foreach (Roders_ErrMsg err in ErrMsg)
            {
                Write_Msg(Machine.MachineEndPoint.Address.ToString(), "发生错误：" + err.Code + "，" + err.Message);
            }
        }


        /// <summary>
        /// 写入日志到文件
        /// </summary>
        /// <param name="IP"></param>
        /// <param name="Msg"></param>
        public void Write_Msg(string IP, string Msg)
        {
            StreamWriter sw = new StreamWriter(LocalPath + "Config\\Message.txt", true);
            sw.WriteLine(string.Format("{0} {1}：{2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), IP, Msg));
            sw.Close();
            sw.Dispose();
        }

        #endregion

        /// <summary>
        /// 获取刀具信息
        /// </summary>
        /// <param name="macindex"></param>
        /// <param name="InOrOut"></param>
        /// <returns></returns>
        public string GetKnifeMes(int macindex, int InOrOut)
        {
            Rec_TTREQ knifemes = null;
            switch (InOrOut)
            {
                case 0:
                    switch (macindex)
                    {
                        case 1:
                            knifemes = rodersserver.Machines[macindex - 1].Send_TTREQ2(Roders.TTREQ_Magazine.INTERNAL);
                            break;
                        case 2:
                            knifemes = rodersserver.Machines[macindex - 1].Send_TTREQ2(Roders.TTREQ_Magazine.INTERNAL);
                            break;
                        default:
                            knifemes = rodersserver.Machines[0].Send_TTREQ2(Roders.TTREQ_Magazine.INTERNAL);
                            break;
                    }
                    break;
                case 1:
                    switch (macindex)
                    {
                        case 1:
                            knifemes = rodersserver.Machines[macindex - 1].Send_TTREQ2(Roders.TTREQ_Magazine.EXTERNAL);
                            break;
                        case 2:
                            knifemes = rodersserver.Machines[macindex - 1].Send_TTREQ2(Roders.TTREQ_Magazine.EXTERNAL);
                            break;
                        default:
                            knifemes = rodersserver.Machines[0].Send_TTREQ2(Roders.TTREQ_Magazine.EXTERNAL);
                            break;
                    }
                    break;
            }
            string mes2 = null;
            for (int i = 0; i < knifemes.Place.Length; i++)
            {
                mes2 = mes2 + "\r\n" + knifemes.Place[i];
            }
            string mes = knifemes.Ret + "," + knifemes.Header + "," + mes2;
            return mes;
        }

        /// <summary>
        /// 启动机床加工
        /// </summary>
        /// <param name="macindex">机床编号</param>
        /// <returns></returns>
        public bool MacStartProcess(int macindex)
        {
            bool flag = false;
            try
            {
                string filepath = null;
                switch (macindex)
                {
                    case 1:
                        filepath = ConfigurationManager.AppSettings["Mac1NcPath"];//机床本地程序路径
                        Console.Write("已发送启动加工，" + filepath + "");
                        rodersserver.Machines[macindex - 1].Send_Run(filepath);
                        break;
                    case 2:
                        filepath = ConfigurationManager.AppSettings["Mac2NcPath"];
                        rodersserver.Machines[macindex - 1].Send_Run(filepath);
                        break;
                    default:
                        filepath = ConfigurationManager.AppSettings["Mac1NcPath"];
                        rodersserver.Machines[0].Send_Run(filepath);
                        break;
                }
            }
            catch (Exception ex)
            {

            }
            return flag;
        }



        /// <summary>
        /// 获取机床状态
        /// </summary>
        /// <param name="macindex">机床编号</param>
        /// <returns></returns>
        public string GetMacStatus(int macindex)
        {
            string status = null;
            try
            {
                bool flag = rodersserver.Machines[macindex - 1].LastReport.Active;
                if (flag)
                    status = "runing";
                else
                    status = "reset";
            }
            catch
            {

            }
            return status;
        }

        /// <summary>
        /// 打开自动门
        /// </summary>
        /// <param name="macindex">机床编号</param>
        public void OpenDoor(int macindex)
        {
            switch (macindex)
            {
                case 1:
                    rodersserver.Machines[macindex - 1].Send_PDOPEN();
                    break;
                case 2:
                    rodersserver.Machines[macindex - 1].Send_PDOPEN();
                    break;
                default:
                    rodersserver.Machines[0].Send_PDOPEN();
                    break;
            }
        }

        /// <summary>
        /// 关闭自动门
        /// </summary>
        /// <param name="macindex">机床序号</param>
        public void closeDoor(int macindex)
        {
            switch (macindex)
            {
                case 1:
                    rodersserver.Machines[macindex - 1].Send_PDCLOSE();
                    break;
                case 2:
                    rodersserver.Machines[macindex - 1].Send_PDCLOSE();
                    break;
                default:
                    rodersserver.Machines[0].Send_PDCLOSE();
                    break;
            }
        }
    }
}
