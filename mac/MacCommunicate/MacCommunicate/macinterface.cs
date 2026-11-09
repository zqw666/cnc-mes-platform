using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MacCommunicate
{
    public interface macinterface
    {
        bool ConnectMac();

        string GetMacStatus();

        string GetKnifeMes(int macindex);

        bool MacStartProcess(int macindex);

        bool OpenDoor(int macindex);

        bool closeDoor(int macindex);

        /// <summary>
        /// 上传程序到机床
        /// </summary>
        /// <param name="localPath">本地程序文件完整路径</param>
        /// <param name="remotePath">机床内目标文件夹路径</param>
        bool uploadprogram(string localPath, string remotePath);
    }
}
