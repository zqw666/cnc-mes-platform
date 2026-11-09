using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using static MacCommunicate.RodersData;

namespace MacCommunicate
{
    public class Roderss : macinterface
    {
        private string _macip;
        private int _macport = 3450;
        private static Roders_Server rodersserver = null;
        private static readonly object _serverLock = new object();
        private static bool _serverStarted = false;
        private bool _isConnected = false;
        private string _lastStatus = null;
        public string LocalPath = Directory.GetCurrentDirectory().Trim('\\') + "\\";

        public Roderss(string macip)
        {
            _macip = macip;
        }

        #region 机床通讯内部方法

        /// <summary>
        /// 创建Roders服务器并监听机床连接（单例：全局仅一个TCP服务器，多机床共享端口20200）
        /// 罗德斯机床主动连接本机IP:20200
        /// IP优先级: config.json > 机床IP > 自动检测
        /// </summary>
        private bool CreateServer()
        {
            lock (_serverLock)
            {
                try
                {
                    // 单例：服务器只创建一次
                    if (rodersserver == null)
                    {
                        // 从 web-ui backend config.json 读取 serverIp
                        string localIp = ReadServerIpFromConfig();
                        if (string.IsNullOrEmpty(localIp))
                        {
                            Write_Msg(_macip, "无法从config.json获取服务器IP，创建服务器失败");
                            return false;
                        }

                        rodersserver = new Roders_Server(localIp, 20200);
                        rodersserver.MachineConnected += new Roders_Server.Machine_Connected(this.Machine_Connect);
                        Write_Msg(localIp, "TCP服务器已创建，监听端口: 20200，等待机床连接...");
                    }
                    else if (!_serverStarted)
                    {
                        // 如果已有实例但未启动监听，绑定新实例的 MachineConnected
                        rodersserver.MachineConnected += new Roders_Server.Machine_Connected(this.Machine_Connect);
                    }

                    if (!_serverStarted)
                    {
                        rodersserver.Listen_Machine_Thread(3450);
                        _serverStarted = true;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Write_Msg(_macip, "创建服务器失败：" + ex.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// 从 config.json 读取服务器IP（向上逐级查找）
        /// 查找当前目录及所有父级目录中的 config.json，读取 serverIp 字段
        /// </summary>
        private string ReadServerIpFromConfig()
        {
            try
            {
                var dir = new DirectoryInfo(LocalPath);
                while (dir != null)
                {
                    string cfgPath = Path.Combine(dir.FullName, "config.json");
                    if (File.Exists(cfgPath))
                    {
                        string json = File.ReadAllText(cfgPath, Encoding.UTF8);
                        int idx = json.IndexOf("\"serverIp\"");
                        if (idx >= 0)
                        {
                            int colonIdx = json.IndexOf(':', idx);
                            int quoteStart = json.IndexOf('"', colonIdx + 1);
                            int quoteEnd = json.IndexOf('"', quoteStart + 1);
                            if (quoteStart > 0 && quoteEnd > quoteStart)
                            {
                                string ip = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                                if (!string.IsNullOrEmpty(ip)) return ip;
                            }
                        }
                        break;
                    }
                    dir = dir.Parent;
                }
            }
            catch (Exception ex)
            {
                Write_Msg(_macip, "读取config.json serverIp失败: " + ex.Message);
            }
            return "";
        }

        /// <summary>
        /// 机床连接触发事件
        /// </summary>
        private void Machine_Connect(Roders Machine)
        {
            Machine.ReportRecieved += new Roders._ReportRecieved(ReportRec);
            Machine.Error_Event += new Roders._ErrorEvent(ErrorEvent);
            _isConnected = true;
            Write_Msg(Machine.MachineEndPoint.Address.ToString(), "机床已连接");
        }

        /// <summary>
        /// 状态端口事件
        /// </summary>
        private void ReportRec(string str, StatuReport sr, Roders Machine)
        {
            try
            {
                // 解析机床状态
                if (sr.Active)
                    _lastStatus = "加工中";
                else
                    _lastStatus = "待机";
            }
            catch (Exception ex)
            {
                Write_Msg(Machine.MachineEndPoint.Address.ToString(), "状态解析失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 机床错误触发事件
        /// </summary>
        private void ErrorEvent(Roders Machine, Roders_ErrMsg[] ErrMsg)
        {
            foreach (Roders_ErrMsg err in ErrMsg)
            {
                Write_Msg(Machine.MachineEndPoint.Address.ToString(), "发生错误：" + err.Code + "，" + err.Message);
            }
        }

        /// <summary>
        /// 获取本机IPV4地址
        /// </summary>
        public string GetLocalIp()
        {
            List<string> IpList = new List<string>();
            string ip = null;
            string localname = Dns.GetHostName();
            IPAddress[] iPAddresses = Dns.GetHostAddresses(localname);
            List<IPAddress> Values = new List<IPAddress>();
            foreach (IPAddress address in iPAddresses)
            {
                if (char.IsDigit(address.ToString()[0]))
                    Values.Add(address);
            }
            foreach (IPAddress iP in Values)
            {
                if (iP.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    IpList.Add(iP.ToString());
                }
            }
            if (IpList.Count > 0)
                ip = IpList[0];
            return ip;
        }

        /// <summary>
        /// 写入日志到文件
        /// </summary>
        private void Write_Msg(string IP, string Msg)
        {
            try
            {
                string dir = LocalPath + "Config";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                StreamWriter sw = new StreamWriter(dir + "\\Message.txt", true);
                sw.WriteLine(string.Format("{0} {1}：{2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), IP, Msg));
                sw.Close();
                sw.Dispose();
            }
            catch { }
        }

        /// <summary>
        /// 获取与本实例 IP 匹配的已连接机床对象
        /// </summary>
        private Roders GetMachine()
        {
            if (rodersserver == null) return null;
            var machines = rodersserver.Machines;
            if (machines == null || machines.Length == 0) return null;
            // 通过 _macip 匹配机床的终端地址，而非依赖连接顺序
            foreach (var m in machines)
            {
                try
                {
                    if (m.MachineEndPoint != null &&
                        m.MachineEndPoint.Address.ToString() == _macip)
                        return m;
                }
                catch { }
            }
            return null;
        }

        #endregion

        /// <summary>
        /// 连接机床（单例服务器模式：服务器创建一次，机床主动连接）
        /// </summary>
        /// <returns></returns>
        public bool ConnectMac()
        {
            try
            {
                // 创建/获取单例服务器
                if (!CreateServer())
                {
                    return false;
                }

                // 等待机床连接，最多等待60秒（机床需要时间主动发起TCP连接）
                int waitCount = 0;
                while (!_isConnected && waitCount < 600)
                {
                    Thread.Sleep(100);
                    waitCount++;
                }

                if (!_isConnected)
                {
                    Write_Msg(_macip, "等待机床连接超时（60秒）");
                    return false;
                }

                Write_Msg(_macip, "机床连接成功");
                return true;
            }
            catch (Exception ex)
            {
                Write_Msg(_macip, "连接机床异常：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 获取机床状态
        /// </summary>
        /// <returns>加工中 / 待机 / null=未知</returns>
        public string GetMacStatus()
        {
            try
            {
                Roders machine = GetMachine();
                if (machine != null && machine.LastReport != null)
                {
                    return machine.LastReport.Active ? "加工中" : "待机";
                }
                return _lastStatus;
            }
            catch
            {
                return null;
            }
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
                Roders machine = GetMachine();
                if (machine != null)
                {
                    string filepath = null;
                    // 根据机床编号获取NC程序路径
                    switch (macindex)
                    {
                        case 1:
                            filepath = System.Configuration.ConfigurationManager.AppSettings["Mac1NcPath"];
                            break;
                        case 2:
                            filepath = System.Configuration.ConfigurationManager.AppSettings["Mac2NcPath"];
                            break;
                        default:
                            filepath = System.Configuration.ConfigurationManager.AppSettings["Mac1NcPath"];
                            break;
                    }

                    if (!string.IsNullOrEmpty(filepath))
                    {
                        machine.Send_Run(filepath);
                        flag = true;
                        Write_Msg(_macip, "已发送启动加工，程序路径：" + filepath);
                    }
                    else
                    {
                        Write_Msg(_macip, "NC程序路径未配置，机床编号：" + macindex);
                    }
                }
            }
            catch (Exception ex)
            {
                Write_Msg(_macip, "启动机床加工异常：" + ex.Message);
            }
            return flag;
        }

        /// <summary>
        /// 获取刀具信息
        /// </summary>
        /// <param name="macindex">机床编号</param>
        /// <returns></returns>
        public string GetKnifeMes(int macindex)
        {
            try
            {
                Roders machine = GetMachine();
                if (machine != null)
                {
                    // 默认获取外部刀库信息
                    Rec_TTREQ knifemes = machine.Send_TTREQ2(Roders.TTREQ_Magazine.EXTERNAL);

                    if (knifemes != null)
                    {
                        string mes2 = null;
                        for (int i = 0; i < knifemes.Place.Length; i++)
                        {
                            mes2 = mes2 + "\r\n" + knifemes.Place[i];
                        }
                        string mes = knifemes.Ret + "," + knifemes.Header + "," + mes2;
                        return mes;
                    }
                }
            }
            catch (Exception ex)
            {
                Write_Msg(_macip, "获取刀具信息异常：" + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 获取刀具信息（指定内部/外部刀库）
        /// </summary>
        /// <param name="macindex">机床编号</param>
        /// <param name="InOrOut">0=内部刀库, 1=外部刀库</param>
        /// <returns></returns>
        public string GetKnifeMes(int macindex, int InOrOut)
        {
            try
            {
                Roders machine = GetMachine();
                if (machine != null)
                {
                    Rec_TTREQ knifemes = null;
                    switch (InOrOut)
                    {
                        case 0:
                            knifemes = machine.Send_TTREQ2(Roders.TTREQ_Magazine.INTERNAL);
                            break;
                        case 1:
                            knifemes = machine.Send_TTREQ2(Roders.TTREQ_Magazine.EXTERNAL);
                            break;
                        default:
                            knifemes = machine.Send_TTREQ2(Roders.TTREQ_Magazine.EXTERNAL);
                            break;
                    }

                    if (knifemes != null)
                    {
                        string mes2 = null;
                        for (int i = 0; i < knifemes.Place.Length; i++)
                        {
                            mes2 = mes2 + "\r\n" + knifemes.Place[i];
                        }
                        string mes = knifemes.Ret + "," + knifemes.Header + "," + mes2;
                        return mes;
                    }
                }
            }
            catch (Exception ex)
            {
                Write_Msg(_macip, "获取刀具信息异常：" + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// 打开自动门
        /// </summary>
        /// <param name="macindex">机床编号</param>
        /// <returns>true=开门成功, false=开门失败</returns>
        public bool OpenDoor(int macindex)
        {
            try
            {
                Roders machine = GetMachine();
                if (machine != null)
                {
                    var result = machine.Send_PDOPEN();
                    if (result != null && result.Ret)
                    {
                        Write_Msg(_macip, "开门成功，机床编号：" + macindex);
                        return true;
                    }
                    Write_Msg(_macip, "开门失败：" + (result != null ? result.Error : "无响应") + "，机床编号：" + macindex);
                    return false;
                }
                Write_Msg(_macip, "开门失败，未找到机床：" + macindex);
                return false;
            }
            catch (Exception ex)
            {
                Write_Msg(_macip, "开门异常：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 关闭自动门
        /// </summary>
        /// <param name="macindex">机床序号</param>
        /// <returns>true=关门成功, false=关门失败</returns>
        public bool closeDoor(int macindex)
        {
            try
            {
                Roders machine = GetMachine();
                if (machine != null)
                {
                    var result = machine.Send_PDCLOSE();
                    if (result != null && result.Ret)
                    {
                        Write_Msg(_macip, "关门成功，机床编号：" + macindex);
                        return true;
                    }
                    Write_Msg(_macip, "关门失败：" + (result != null ? result.Error : "无响应") + "，机床编号：" + macindex);
                    return false;
                }
                Write_Msg(_macip, "关门失败，未找到机床：" + macindex);
                return false;
            }
            catch (Exception ex)
            {
                Write_Msg(_macip, "关门异常：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 上传程序到机床
        /// </summary>
        /// <param name="localPath">本地程序文件完整路径</param>
        /// <param name="remotePath">机床内目标文件夹路径</param>
        public bool uploadprogram(string localPath, string remotePath)
        {
            return false; // roders暂未实现
        }

        /// <summary>
        /// 断开机床连接
        /// </summary>
        public void Disconnect()
        {
            _isConnected = false;
            rodersserver = null;
        }
    }
}
