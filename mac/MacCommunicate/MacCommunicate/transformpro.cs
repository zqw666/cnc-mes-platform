using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace MacCommunicate
{
    public class transformpro
    {
        string transfermway;
        public string ftpserverpath;
        public string localrootpath;
        public string serverrootpath;
        public string username;
        public string pwd;
        public ftp Ftp;

        /// <summary>
        /// 加载配置文件
        /// </summary>
        void LoadConfig()
        {

        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="ftpserverpath"></param>
        /// <param name="username"></param>
        /// <param name="pwd"></param>
        public transformpro(string ftpserverpath,string username,string pwd)
        {
            LoadConfig();
            switch (transfermway)
            {
                case "ftp":
                    Ftp = new ftp(ftpserverpath, username, pwd);
                    Ftp.Ftp_CheckLogin();
                    break;
                case "io":
                    break;
            }
        }

        /// <summary>
        /// 从服务器下载程序到本地
        /// </summary>
        /// <param name="remotepath">服务器根目录</param>
        /// <param name="localpath">本地根目录</param>
        /// <param name="filename">文件名称</param>
        /// <returns></returns>
        public bool DownloadProgramToLocal(string remotepath,string localpath,string filename)
        {
            bool flag = false;
            switch(transfermway)
            {
                case "ftp":
                        string[] list = Ftp.Ftp_GetFileList(remotepath);
                        if (CheckFtpHaveFile(Ftp,remotepath,filename))
                        {
                            if (Directory.Exists(localpath))
                            {
                                if (Ftp.Ftp_DownloadFile(localpath + "\\" + filename, filename, remotepath))
                                    flag = true;
                            }
                            else
                            {
                                Directory.CreateDirectory(localpath);
                                if (Directory.Exists(localpath))
                                {
                                    if (Ftp.Ftp_DownloadFile(localpath + "\\" + filename, filename, remotepath))
                                        flag = true;
                                }
                            }
                        }
                        else
                            flag = false;
                    break;
                case "io":
                    if(Directory.Exists(remotepath))
                    {
                        if(Directory.Exists(localpath))
                        {
                            if (File.Exists(remotepath + "\\" + filename))
                            {
                                File.Copy(remotepath + "\\" + filename, localpath + "\\" + filename);
                                if (File.Exists(localpath + "\\" + filename))
                                    flag = true;
                                else
                                    flag = false;
                            }
                            else
                                flag = false;
                        }
                    }
                    break;
            }
            return flag;
        }

        /// <summary>
        /// 检查ftp服务器上是否有指定文件
        /// </summary>
        /// <param name="Ftp">ftp类的对象</param>
        /// <param name="remotepath">服务器文件夹</param>
        /// <param name="filename">文件名称</param>
        /// <returns></returns>
        public bool CheckFtpHaveFile(ftp Ftp, string remotepath, string filename)
        {
            bool flag = false;
            try
            {
                string[] list = Ftp.Ftp_GetFileList(remotepath);
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i].Contains(filename))
                    {
                        flag = true;
                        break;
                    }
                }
            }
            catch(Exception ex)
            {

            }
            return flag;
        }

        /// <summary>
        /// 从本地上传程序到服务器
        /// </summary>
        /// <param name="localpath">本地路径</param>
        /// <param name="remotepath">服务器路径</param>
        /// <returns></returns>
        public bool UpLoadProgramToRemote(string localpath,string remotepath,string filename)
        {
            bool flag = false;
            switch(transfermway)
            {
                case "ftp":
                    if(Ftp.Ftp_CheckDirExist(remotepath))
                    {
                        if(File.Exists(localpath+"\\"+filename))
                        {
                            if (Ftp.Ftp_UploadFile(filename, remotepath))
                                flag = true;
                        }
                    }
                    else
                    {
                        if(Ftp.Ftp_CreateFloder(remotepath))
                        {
                            if (File.Exists(localpath + "\\" + filename))
                            {
                                if (Ftp.Ftp_UploadFile(filename, remotepath))
                                    flag = true;
                            }
                        }
                    }
                    break;
                case "io":
                    if(Directory.Exists(remotepath))
                    {
                        File.Copy(localpath+"\\"+filename,remotepath+"\\"+filename);
                        if (File.Exists(remotepath + "\\" + filename))
                            flag = true;
                    }
                    else
                    {
                        Directory.CreateDirectory(remotepath);
                        if(Directory.Exists(remotepath))
                        {
                            File.Copy(localpath + "\\" + filename, remotepath + "\\" + filename);
                            if (File.Exists(remotepath + "\\" + filename))
                                flag = true;
                        }
                    }
                    break;
            }
            return flag;
        }
    }
}
