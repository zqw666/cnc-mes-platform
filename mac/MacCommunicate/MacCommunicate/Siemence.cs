﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MacCommunicate
{
    public class Siemence:macinterface
    {
        private string _macip;
        Siemensoperate operate =null;
        private readonly string _sessionUrl;
        private readonly string _username;
        private readonly string _password;
        private readonly string _winscpKey;

        public Siemence(string macip, string sessionUrl = null, string username = null, string password = null, string winscpKey = null)
        {
            _macip = macip;
            _sessionUrl = sessionUrl;
            _username = username;
            _password = password;
            _winscpKey = winscpKey;
        }
        /// <summary>
        /// 连接机床
        /// </summary>
        /// <returns></returns>
        public bool ConnectMac()
        {
            bool flag = false;
            operate = new Siemensoperate(_macip, _username, _password, _winscpKey, _sessionUrl);
            if (operate.Connect())
                flag = true;
            return flag;
        }

        /// <summary>
        /// 获取机床状态
        /// </summary>
        /// <returns></returns>
        public string GetMacStatus()
        {
            try
            {
                // 懒创建：不依赖预先 ConnectMac()，纯 OPC UA 直连读取（参考 OpcUaMachineTester）
                if (operate == null)
                    operate = new Siemensoperate(_macip, _username, _password, _winscpKey, _sessionUrl);
                Dictionary<string, string> vals = operate.ReadMany("/Channel/State/progStatus");
                string prog = GetNodeValue(vals, "/Channel/State/progStatus");
                if (string.IsNullOrEmpty(prog))
                    return "未连接";
                // 只用 prog 判定状态；prog 为 4、5 均视为待机
                switch (prog)
                {
                    case "1": return "中断";
                    case "2": return "停止";
                    case "3": return "运行";
                    case "4": return "待机";
                    case "5": return "待机";
                    default: return "未知";
                }
            }
            catch
            {
                return "未连接";
            }
        }

        /// <summary>
        /// 从批量读取结果中取节点值
        /// </summary>
        private static string GetNodeValue(Dictionary<string, string> vals, string key)
        {
            string v;
            if (vals != null && vals.TryGetValue(key, out v))
                return v == null ? "" : v.Trim();
            return "";
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

            }
            catch (Exception ex)
            {

            }
            return flag;
        }

        /// <summary>
        /// 获取刀具信息
        /// </summary>
        /// <param name="macindex"></param>
        /// <returns></returns>
        public string GetKnifeMes(int macindex)
        {
            string knifemes = null;
            return knifemes;
        }

        /// <summary>
        /// 打开自动门
        /// </summary>
        /// <param name="macindex">机床编号</param>
        public bool OpenDoor(int macindex)
        {
            return true; // siemense暂未实现
        }

        public bool closeDoor(int macindex)
        {
            return true; // siemense暂未实现
        }

        /// <summary>
        /// 上传程序到机床（调用 Siemensoperate.UploadProgram）
        /// </summary>
        /// <param name="localPath">本地程序文件完整路径</param>
        /// <param name="remotePath">机床内目标文件夹路径，如 /card/user/sinumerik/data/mnt/usercf/</param>
        /// <returns></returns>
        public bool uploadprogram(string localPath, string remotePath)
        {
            try
            {
                if (operate == null)
                    operate = new Siemensoperate(_macip, _username, _password, _winscpKey, _sessionUrl);
                // 使用文件传输服务（WinSCP/SCP）连接机床并传输程序
                if (!operate.ConnectFileTransfer())
                    return false;
                // 上传前先删除机床上的旧程序文件（目标文件可能不存在，删除失败不影响后续上传）
                operate.DeleteProgram(remotePath);
                return operate.UploadProgram(localPath, remotePath);
            }
            catch
            {
                return false;
            }
        }
    }
}
