using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;

namespace PLC
{
    public class plc
    {
        /// <summary>
        /// 设置PLC全局变量
        /// </summary>
        public static bool connect;
        mcOMRON.OmronPLC omronplc = new mcOMRON.OmronPLC(mcOMRON.TransportType.udp);
        UpdateLog uplog = new UpdateLog();
        public static string logpath = ConfigurationManager.AppSettings["logpath"];
        /// <summary>
        /// 连接PLC
        /// </summary>
        /// <param name="IP">要连接的服务器IP</param>
        /// <param name="baudrate">波特率</param>
        /// <returns></returns>
        public bool Connect(string IP, int baudrate)
        {
            try
            {
                if (omronplc == null)
                {
                    connect = false;
                    return false;
                }

                mcOMRON.UdpFinsCommand udpcommand = (mcOMRON.UdpFinsCommand)omronplc.FinsCommand;
                udpcommand.SetTCPParams(IPAddress.Parse(IP), baudrate);//调用udpFinsCommand的SetTCPParams方法将IP地址转换为实例地址，将端口号强制转换为长整型
                if (!omronplc.Connect())
                {
                    connect = false;
                }
                else
                {
                    if (omronplc.Connected)
                        connect = true;
                }
            }
            catch (Exception e)
            {
                if (logpath != null)
                    uplog.upDateLogTxt(e.ToString(), true, "机器人错误日志", logpath);
            }
            return connect;
        }
        /// <summary>
        /// 设置DM区的值
        /// </summary>
        /// <param name="task">任务号</param>
        /// <param name="DM">要写入的DM区</param>
        public bool WriteDM(int DM, int task)
        {
            bool flag = false;
            lock (this)
            {
                try
                {
                    if (omronplc.WriteDM(Convert.ToUInt16(DM), Convert.ToUInt16(task)))
                    {
                        flag = cioval != 0;
                    }
                }
                catch (Exception e)
                {
                    uplog.upDateLogTxt(e.ToString() + DM + task, true, "机器人错误日志",logpath);
                }
                return flag;
            }
        }

        /// <summary>
        /// 设置WR区域的值
        /// </summary>
        /// <param name="wr">要写入的WR区(xxx.xx)</param>
        /// <param name="cioval">要写入的值（0或1）</param>
        public bool WriteWR(string wr, Byte cioval)
        {
            bool flag = false;
            string[] NewStr = wr.Split('.');
            lock (this)
            {
                try
                {
                    if (omronplc.WriteWRBit(Convert.ToUInt16(NewStr[0]), Convert.ToByte(NewStr[1]), cioval))
                    {
                        flag = cioval != 0;
                    }
                }
                catch (Exception e)
                {
                    uplog.upDateLogTxt(e.ToString() + wr + cioval, true, "机器人错误日志",logpath);
                }
                return flag;
            }
        }

        /// <summary>
        /// 设置WR区域的值
        /// </summary>
        /// <param name="wr">要写入的WR区(xxx)</param>
        /// <param name="cioval">要写入的值（0或1）</param>
        public bool WriteWr(string wr, UInt16 cioval)
        {
            bool flag = false;
            lock (this)
            {
                try
                {
                    if (omronplc.WriteWR(Convert.ToUInt16(wr), cioval))
                    {
                        flag = cioval != 0;
                    }
                }
                catch (Exception e)
                {
                    uplog.upDateLogTxt(e.ToString() + wr + cioval, true, "机器人错误日志",logpath);
                }
                return flag;
            }
        }

        /// <summary>
        /// 读取WR区相应通道的bool值,如读取W201的值为1,1为true
        /// </summary>
        /// <param name="wr">要读取的WR存储区</param>
        /// <returns></returns>
        public bool ReadWR(string wr)
        {
            bool ReadOrNot = false;
            string[] NewWR = wr.Split('.');
            Byte cioval = 0;
            lock (this)
            {
                try
                {
                    if (!omronplc.ReadWRBit(Convert.ToUInt16(NewWR[0]), Convert.ToByte(NewWR[1]), ref cioval))
                    {
                        ReadOrNot = false;
                    }
                    else
                    {
                        string[] mess = omronplc.LastDialog("Write WR Bit").Split('\r');
                        string[] ress = mess[1].Split('-');
                        string change = ress[ress.Length-1];
                        if (change == "01")
                        {
                            ReadOrNot = true;
                        }
                        else
                        {
                            ReadOrNot = false;
                        }
                    }
                }
                catch (Exception e)
                {
                    uplog.upDateLogTxt(e.ToString(), true, "机器人错误日志",logpath);
                }
                return ReadOrNot;
            }
        }

        /// <summary>
        /// 读取WR区相应位置的值
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public int ReadWr(ushort position)
        {
            ushort value = 0;
            int ReadValue = 0;
            lock (this)
            {
                try
                {
                    if (!omronplc.ReadWR(position, ref value))
                    {
                        ReadValue = -1;
                    }
                    else
                    {
                        ReadValue = value;
                    }
                }
                catch (Exception ex)
                {
                    uplog.upDateLogTxt(ex.ToString() + position, true, "机器人错误日志", logpath);
                    ReadValue = -1;
                }
                return ReadValue;
            }
        }

        /// <summary>
        /// 读取WR区双字（32位），一次FINS请求读取position和position+1两个连续字，参照ReadDMs实现
        /// </summary>
        /// <param name="position">WR区起始地址（低字地址）</param>
        /// <returns>双字值（Int32），失败返回-1</returns>
        public int ReadWrDWord(ushort position)
        {
            int ReadValue = 0;
            lock (this)
            {
                try
                {
                    // 一次FINS [1,1] MemoryAreaRead 读取2个连续WR字
                    if (!omronplc.finsMemoryAreadRead(mcOMRON.MemoryArea.WR, position, 0, 2))
                        return -1;

                    byte[] resp = omronplc.FinsCommand.Response;
                    ushort lowWord = mcOMRON.BTool.BytesToUInt16(resp[0], resp[1]);
                    ushort highWord = mcOMRON.BTool.BytesToUInt16(resp[2], resp[3]);

                    ReadValue = (highWord << 16) | lowWord;
                }
                catch (Exception ex)
                {
                    uplog.upDateLogTxt(ex.ToString() + position, true, "机器人错误日志", logpath);
                    ReadValue = -1;
                }
                return ReadValue;
            }
        }

        /// <summary>
        /// 读取WR区双字（32位）并转换为浮点型（IEEE 754单精度），一次FINS请求读取2个连续WR字
        /// </summary>
        /// <param name="position">WR区起始地址（低字地址）</param>
        /// <returns>浮点值（float），失败返回NaN</returns>
        public float ReadWrFloat(ushort position)
        {
            float ReadValue = 0;
            lock (this)
            {
                try
                {
                    // 一次FINS [1,1] MemoryAreaRead 读取2个连续WR字
                    if (!omronplc.finsMemoryAreadRead(mcOMRON.MemoryArea.WR, position, 0, 2))
                        return float.NaN;

                    byte[] resp = omronplc.FinsCommand.Response;
                    // PLC返回大端序IEEE 754，.NET BitConverter需小端序，反转字节
                    byte[] floatBytes = new byte[] { resp[3], resp[2], resp[1], resp[0] };
                    ReadValue = BitConverter.ToSingle(floatBytes, 0);
                }
                catch (Exception ex)
                {
                    uplog.upDateLogTxt(ex.ToString() + position, true, "机器人错误日志", logpath);
                    ReadValue = float.NaN;
                }
                return ReadValue;
            }
        }

        /// <summary>
        /// 读取WR区两个双字（64位），一次FINS请求读取position~position+3四个连续字
        /// </summary>
        /// <param name="position">WR区起始地址（第一个双字的低字地址）</param>
        /// <returns>64位长整型值（long），失败返回-1</returns>
        public long ReadWrTwoDWord(ushort position)
        {
            long ReadValue = 0;
            lock (this)
            {
                try
                {
                    // 一次FINS [1,1] MemoryAreaRead 读取4个连续WR字
                    if (!omronplc.finsMemoryAreadRead(mcOMRON.MemoryArea.WR, position, 0, 4))
                        return -1;

                    byte[] resp = omronplc.FinsCommand.Response;
                    ushort lowWord1 = mcOMRON.BTool.BytesToUInt16(resp[0], resp[1]);
                    ushort highWord1 = mcOMRON.BTool.BytesToUInt16(resp[2], resp[3]);
                    ushort lowWord2 = mcOMRON.BTool.BytesToUInt16(resp[4], resp[5]);
                    ushort highWord2 = mcOMRON.BTool.BytesToUInt16(resp[6], resp[7]);

                    long dword1 = ((long)highWord1 << 16) | lowWord1;
                    long dword2 = ((long)highWord2 << 16) | lowWord2;
                    ReadValue = (dword2 << 32) | (dword1 & 0xFFFFFFFF);
                }
                catch (Exception ex)
                {
                    uplog.upDateLogTxt(ex.ToString() + position, true, "机器人错误日志", logpath);
                    ReadValue = -1;
                }
                return ReadValue;
            }
        }

        /// <summary>
        /// 读取DM区
        /// </summary>
        /// <param name="dm">要读取的DM区</param>
        /// <returns></returns>
        public bool ReadDM(int dm)
        {
            ushort cioval = 0;
            bool flag = false;
            lock (this)
            {
                try
                {
                    if (!omronplc.ReadDM(Convert.ToUInt16(dm), ref cioval))
                    {
                        flag = false;
                    }
                    else
                    {
                        flag = cioval != 0;
                    }
                }
                catch (Exception e)
                {
                    uplog.upDateLogTxt(e.ToString() + dm, true, "机器人错误日志",logpath);
                }
                return flag;
            }
        }

        /// <summary>
        /// 复原WR区
        /// </summary>
        /// <param name="wr">要复原的wr区</param>
        public void InitialWR(string wr)
        {
            WriteWR(wr, 1);
            Thread.Sleep(2000);
            WriteWR(wr, 0);
        }

        /// <summary>
        /// 给机器人发送任务号
        /// </summary>
        /// <param name="dm">要执行的任务编号</param>
        /// <param name="task">写入任务号的dm区</param>
        /// <param name="wr">写入任务号的wr区</param>
        public bool writetask(int dm, int task, string wr)
        {
            bool flag = false;
            lock (this)
            {
                if (WriteDM(dm, task))//发送任务号
                {
                    if (WriteWR(wr, 1))//给PLC发送任务开始信号
                        flag = cioval != 0;
                }
            }
            return flag;
        }

        /// <summary>
        /// read a WR bit
        /// </summary>
        public bool ReadWRBit(UInt16 position, byte bit_position, ref byte value)
        {
            lock (this)
            {
                if (!omronplc.ReadWRBit(position, bit_position, ref value))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        public bool WriteWRBit(UInt16 position, byte bit_position, byte value)
        {
            lock (this)
            {
                if (!omronplc.WriteWRBit(position, bit_position, value))
                    return false;
                else
                    return true;
            }
        }

        /// <summary>
        /// 获取数据
        /// </summary>
        /// <returns></returns>
        public string GetHex()
        {
            return omronplc.ResultMess();
        }

        /// <summary>
        /// 关闭
        /// </summary>
        public void close()
        {
            omronplc.Close();
        }
    }
}
