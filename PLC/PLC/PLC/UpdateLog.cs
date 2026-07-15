using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PLC
{
    public class UpdateLog
    {
        /// <summary>
        /// 更新日志
        /// </summary>
        /// <param name="msg">要更新入Listview中的内容</param>
        public void UpMsg(string msg)
        {
            if (msg.Contains("错误"))
            {
                Mould.Lvmessage = msg;
                Mould.Iserror = 2;
            }
            if (msg.Contains("警告"))
            {
                Mould.Lvmessage = msg;
                Mould.Iserror = 1;
            }
            if (msg.Contains("正常"))
            {
                Mould.Lvmessage = msg;
                Mould.Iserror = 0;
            }
        }

        /// <summary>
        /// 更新主界面料架图片
        /// </summary>
        /// <param name="racktype">料架类型</param>
        /// <param name="pos_id">料位编号</param>
        /// <param name="monitorid">监控编号</param>
        /// <param name="iamgeindex">图片索引值</param>
        public void UpImage(int racktype, int pos_id, string monitorid, int iamgeindex)
        {
            Mould.RackTypeIndex = racktype;
            Mould.posImage = iamgeindex;
            Mould.MonitorID = monitorid;
            Mould.Pos_id = pos_id;
        }

        private static readonly object obj = new object();

        /// <summary>
        /// 更新日志文件
        /// </summary>
        /// <param name="msg">要更新的语句</param>
        public void upDateLogTxt(string msg, bool Errflag, string logfilename,string logpath)
        {
            lock (obj)
            {
                try
                {
                    string logfilepath = "" + logpath + "\\运行日志";
                    if (!Directory.Exists(logfilepath))
                        Directory.CreateDirectory(logfilepath);
                    string[] time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss").Split(' ');
                    string[] Time = time[0].Split('-');
                    string name = "" + Time[1] + "月" + Time[2] + "日";
                    string path = "";
                    string str = "";
                    if (Errflag)
                    {
                        path = @"" + logfilepath + "/" + name + "" + logfilename + ".txt";
                        str = "错误内容：" + msg + ",错误发生时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n";
                    }
                    else
                    {
                        path = @"" + logfilepath + "/" + name + "" + logfilename + ".txt";
                        str = "执行内容:" + msg + ",执行时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n";
                    }
                    StreamWriter sw = new StreamWriter(path, true);
                    sw.Write(str);
                    sw.Dispose();
                    sw.Close();
                }
                catch (Exception e)
                {

                }
            }
        }

        /// <summary>
        /// 更新数据库操作错误日志
        /// </summary>
        /// <param name="errmessage">错误信息</param>
        /// <param name="errorposition">错误发生位置</param>
        public void UpSqlErrorLog(string errmessage, string errorposition,string logpath)
        {
            try
            {
                string logfilepath = "" + logpath + "\\运行日志";
                if (!Directory.Exists(logfilepath))
                    Directory.CreateDirectory(logfilepath);
                string[] time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss").Split(' ');
                string[] Time = time[0].Split('-');
                string name = "" + Time[1] + "月" + Time[2] + "日";
                string path = "";
                string str = "";
                path = @"" + logfilepath + "/" + name + "数据库操作错误日志.txt";
                str = "错误内容：" + errmessage + ",错误位置:" + errorposition + ",错误发生时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n";
                StreamWriter sw = new StreamWriter(path, true);
                sw.Write(str);
                sw.Dispose();
                sw.Close();
            }
            catch
            {

            }
        }
    }
}
