using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MacCommunicate
{
    public class Makino:macinterface
    {
        private string _macip;

        public Makino(string macip)
        {
            _macip = macip;
        }

        /// <summary>
        /// 连接机床
        /// </summary>
        /// <returns></returns>
        public bool ConnectMac()
        {
            bool flag = false;

            return flag;
        }

        /// <summary>
        /// 获取机床状态
        /// </summary>
        /// <returns></returns>
        public string GetMacStatus()
        {
            string status = null;
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
            return true; // makino暂未实现
        }

        public bool closeDoor(int macindex)
        {
            return true; // makino暂未实现
        }

        /// <summary>
        /// 上传程序到机床
        /// </summary>
        /// <param name="localPath">本地程序文件完整路径</param>
        /// <param name="remotePath">机床内目标文件夹路径</param>
        public bool uploadprogram(string localPath, string remotePath)
        {
            return false; // makino暂未实现
        }
    }
}
