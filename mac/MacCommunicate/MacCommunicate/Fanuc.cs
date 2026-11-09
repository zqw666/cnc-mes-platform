using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static MacCommunicate.Focus;

namespace MacCommunicate
{
    public class Fanuc:macinterface
    {
        Focus focus = null;


        public Fanuc(string macip)
        {
            focus = new Focus(macip);
        }

        /// <summary>
        /// 连接机床
        /// </summary>
        /// <returns></returns>
        public bool ConnectMac()
        {
            bool flag = false;
            FocusMsg result = focus.Connect();
            if (result.Msg == "连接成功")
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
            FocusStateInfo state = focus.GetStateInfo();
            switch(state.run)
            {
                case 1:
                    status = "加工中";
                    break;
                case -1:
                    status = "待机";
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
            return true; // fanuc暂未实现
        }

        public bool closeDoor(int macindex)
        {
            return true; // fanuc暂未实现
        }

        /// <summary>
        /// 上传程序到机床
        /// </summary>
        /// <param name="localPath">本地程序文件完整路径</param>
        /// <param name="remotePath">机床内目标文件夹路径</param>
        public bool uploadprogram(string localPath, string remotePath)
        {
            return false; // fanuc暂未实现
        }
    }
}
