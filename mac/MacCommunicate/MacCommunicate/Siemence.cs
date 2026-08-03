using System;
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
            string status = null;
            if (operate == null)
                return "未连接";
            Siemensoperate.MachineStatus macstatus = operate.Status;
            int Status = macstatus.Stus;
            switch(Status)
            {
                case 0:
                    status = "未连接";
                    break;
                case 1:
                    status = "中断";
                    break;
                case 2:
                    status = "停止";
                    break;
                case 3:
                    status = "运行";
                    break;
                case 4:
                    status = "等待";
                    break;
                case 5:
                    status = "完成";
                    break;
                case -1:
                    status = "连接失败";
                    break;
            }
            return status;
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
    }
}
