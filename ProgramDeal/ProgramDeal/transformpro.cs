using System;
using FTP;
using System.IO;
using System.Collections.Generic;

namespace ProgramDeal
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
        public List<List<int>> sameKnifeList = new List<List<int>>();

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

        /// <summary>
        /// 合并电极程序
        /// </summary>
        /// <param name="mergeprogrampath">合并程序路径</param>
        /// <param name="polprogrampathlist">电极程序路径列表</param>
        public void MergeProgram(List<string> mergeknifelist, string mergeprogrampath, string localrootdirectory, List<string[]> polprogrampathlist, List<string> coords = null)
        {
            List<string> Mergeknife = mergeknifelist;
            if (Mergeknife.Count == 0)
                Mergeknife = MergeKnifeList(localrootdirectory, sameKnifeList, polprogrampathlist);
            // 坐标系列表：优先使用传入的 coords，否则回退到默认 G54-G59
            List<string> coordList = BuildCoordList(polprogrampathlist.Count, coords);
            File.AppendAllText(mergeprogrampath, "G75 Z0" + "\r\n");
            File.AppendAllText(mergeprogrampath, "G40 G17 G90 G56 G64" + "\r\n");
            List<string> sameknifemes = new List<string>();
            try
            {
                for (int x = 0; x < Mergeknife.Count; x++)
                {
                    for (int y = x; y < Mergeknife.Count; y++)
                    {
                        if (Mergeknife[x] == Mergeknife[y])
                        {
                            if (x > y)
                            {
                                string mes = Mergeknife[x] + "," + y + "," + x;
                                sameknifemes.Add(mes);
                            }
                            else if (x < y)
                            {
                                string mes = Mergeknife[x] + "," + x + "," + y;
                                sameknifemes.Add(mes);
                            }
                        }
                    }
                }
                string sameknife = sameknifemes.ToString();
                for (int i = 0; i < Mergeknife.Count; i++)
                {
                    File.AppendAllText(mergeprogrampath, "G75 Z0" + "\r\n");
                    File.AppendAllText(mergeprogrampath, "G40 G17 G90 G56 G64" + "\r\n");
                    File.AppendAllText(mergeprogrampath, Mergeknife[i] + "\r\n");
                    File.AppendAllText(mergeprogrampath, "M196"+ "\r\n");
                    for (int j = 0; j < polprogrampathlist.Count; j++)
                    {
                        if (File.Exists(localrootdirectory + "\\" + polprogrampathlist[j][0] + "\\" + polprogrampathlist[j][1] + ".mpf"))
                        {
                            FileStream fs = new FileStream(localrootdirectory + "\\" + polprogrampathlist[j][0] + "\\" + polprogrampathlist[j][1] + ".mpf", FileMode.Open, FileAccess.Read);
                            StreamReader sr = new StreamReader(fs);
                            string con = sr.ReadToEnd();
                            string key = null;
                            string g_Valuse = null;
                            List<int> knifeindexmes = null;
                            // 根据 coords 参数替换坐标系（默认替换 G54 为对应工位坐标系）
                            string coord = coordList[j];
                            key = con.Replace("G54", coord);
                            g_Valuse = "G90 " + coord;
                            if (j < sameKnifeList.Count)
                                knifeindexmes = sameKnifeList[j];
                            string[] cons = key.Split(new string[] { " D1" }, StringSplitOptions.RemoveEmptyEntries);
                            for (int z = 0; z < cons.Length; z++)
                            {
                                if (cons[z].Contains(Mergeknife[i]))
                                {
                                    int index = -1;
                                    if (sameknife.Contains(Mergeknife[i]))
                                    {
                                    }
                                    for (int m = 0; m < sameknifemes.Count; m++)
                                    {
                                        if (sameknifemes[m].Contains(Mergeknife[i]))
                                        {
                                            index = m;
                                            break;
                                        }
                                    }
                                    if (index != -1)
                                    {
                                        string[] knifemes = sameknifemes[index].Split(',');
                                        string speed = null;
                                        if (cons[z].Contains("M3"))
                                        {
                                            string[] ss = cons[z].Split(new string[] { "M3" }, StringSplitOptions.RemoveEmptyEntries);
                                            string[] sss = ss[0].Split(new string[] { coord }, StringSplitOptions.RemoveEmptyEntries);
                                            speed = sss[2] + "\r" + "M3";
                                        }
                                        for (int w = 1; w < knifemes.Length; w++)
                                        {
                                            if (z == Convert.ToInt16(knifemes[w]))
                                            {
                                                File.AppendAllText(mergeprogrampath, g_Valuse + "\r\n");
                                                File.AppendAllText(mergeprogrampath, speed + "\r\n");
                                                File.AppendAllText(mergeprogrampath, "D1" + "\r\n");
                                                string g55Mid = cons[z + 1].Split(new string[] { "M9" }, StringSplitOptions.RemoveEmptyEntries)[0];
                                                sr.Close();
                                                fs.Close();
                                                File.AppendAllText(mergeprogrampath, g55Mid+ "\r\n");
                                                File.AppendAllText(mergeprogrampath, "M9"+ "\r\n");
                                                File.AppendAllText(mergeprogrampath, "G75 Z0"+ "\r\n");
                                                sr.Close();
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        string speed = null;
                                        if (cons[z].Contains("M3"))
                                        {
                                            string[] ss = cons[z].Split(new string[] { "M3" }, StringSplitOptions.RemoveEmptyEntries);
                                            string[] sss = ss[0].Split(new string[] {coord}, StringSplitOptions.RemoveEmptyEntries);
                                            speed = sss[2] + "\r" + "M3";
                                        }
                                        File.AppendAllText(mergeprogrampath, g_Valuse + "\r\n");
                                        File.AppendAllText(mergeprogrampath, speed + "\r\n");
                                        File.AppendAllText(mergeprogrampath, "D1" + "\r\n");
                                        string g55Mid = cons[z + 1].Split(new string[] { "M9" }, StringSplitOptions.RemoveEmptyEntries)[0];
                                        sr.Close();
                                        fs.Close();
                                        File.AppendAllText(mergeprogrampath, g55Mid+ "\r\n");
                                        File.AppendAllText(mergeprogrampath, "M9"+ "\r\n");
                                        File.AppendAllText(mergeprogrampath, "G75 Z0"+ "\r\n");
                                        sr.Close();
                                    }
                                }
                            }
                            sr.Close();
                            fs.Close();
                        }
                        else
                        {
          
                            break;
                        }
                    }
                }
                File.AppendAllText(mergeprogrampath, "M09" + "\r\n");
                File.AppendAllText(mergeprogrampath, "M05" + "\r\n");
                File.AppendAllText(mergeprogrampath, "G75 Z0" + "\r\n");
                File.AppendAllText(mergeprogrampath, "G75 Y0" + "\r\n");
                File.AppendAllText(mergeprogrampath, "M30" + "\r\n");
            }
            catch (Exception ex)
            {

            }
        }

        /// <summary>
        /// 构建坐标系列表：优先使用传入的 coords，缺失项回退到默认 G54-G59
        /// </summary>
        static List<string> BuildCoordList(int count, List<string> coords)
        {
            var list = new List<string>();
            string[] defaults = { "G54", "G55", "G56", "G57", "G58", "G59" };
            for (int i = 0; i < count; i++)
            {
                string c = (coords != null && i < coords.Count && !string.IsNullOrEmpty(coords[i]))
                    ? coords[i]
                    : (i < defaults.Length ? defaults[i] : "G54");
                list.Add(c);
            }
            return list;
        }

        /// <summary>
        /// 获取合并刀具列表
        /// </summary>
        /// <param name="pathlist">电极程序路径列表</param>
        /// <returns></returns>
        public List<string> MergeKnifeList(string localrootdirectory, List<List<int>> sameknifelist, List<string[]> pathlist)
        {
            List<string> G54 = new List<string>();
            List<string> G55 = new List<string>();
            List<string> G56 = new List<string>();
            List<string> G57 = new List<string>();
            List<string> G58 = new List<string>();
            List<string> G59 = new List<string>();
            List<string> MoBan = new List<string>();
            List<List<string>> numknife = new List<List<string>>();
            List<string> MergeKnifeList = new List<string>();
            for (int i = 0; i < pathlist.Count; i++)
            {
                if (File.Exists(localrootdirectory + "\\" + pathlist[i][0] + "\\" + pathlist[i][1] + ".mpf"))
                {
                    StreamReader _rstream = new StreamReader(localrootdirectory + "\\" + pathlist[i][0] + "\\" + pathlist[i][1] + ".mpf");
                    List<int> mes = new List<int>();
                    string line;
                    while ((line = _rstream.ReadLine()) != null)
                    {
                        if (line.Contains("M6"))
                        {
                            switch (i)
                            {
                                case 0:
                                    if (G54.Contains(line))
                                    {
                                        mes.Add(G54.IndexOf(line));
                                        G54.Add(line);
                                        mes.Add(G54.LastIndexOf(line));
                                    }
                                    else
                                        G54.Add(line);
                                    break;
                                case 1:
                                    if (G55.Contains(line))
                                    {
                                        mes.Add(G55.IndexOf(line));
                                        G55.Add(line);
                                        mes.Add(G55.LastIndexOf(line));
                                    }
                                    else
                                        G55.Add(line);
                                    break;
                                case 2:
                                    if (G56.Contains(line))
                                    {
                                        mes.Add(G56.IndexOf(line));
                                        G56.Add(line);
                                        mes.Add(G56.LastIndexOf(line));
                                    }
                                    else
                                        G56.Add(line);
                                    break;
                                case 3:
                                    if (G57.Contains(line))
                                    {
                                        mes.Add(G57.IndexOf(line));
                                        G57.Add(line);
                                        mes.Add(G57.LastIndexOf(line));
                                    }
                                    else
                                        G57.Add(line);
                                    break;
                                case 4:
                                    if (G58.Contains(line))
                                    {
                                        mes.Add(G58.IndexOf(line));
                                        G58.Add(line);
                                        mes.Add(G58.LastIndexOf(line));
                                    }
                                    else
                                        G58.Add(line);
                                    break;
                                case 5:
                                    if (G59.Contains(line))
                                    {
                                        mes.Add(G59.IndexOf(line));
                                        G59.Add(line);
                                        mes.Add(G59.LastIndexOf(line));
                                    }
                                    else
                                        G59.Add(line);
                                    break;
                            }
                        }
                    }
                    if (mes.Count != 0)
                        sameknifelist.Add(mes);
                    _rstream.Close();
                }
                else
                {

                    break;
                }
            }

            List<List<string>> knifecount = new List<List<string>> { G54, G55, G56, G57, G58 };

            int maxnum = 0;
            int a = 0;
            int b = 0;
            for (int i = 0; i < knifecount.Count; i++)
            {
                if (i == 0)
                {
                    a = knifecount[i].Count;
                }
                else
                {
                    if (i == knifecount.Count - 1)
                        b = knifecount[i].Count;
                    else
                        b = knifecount[i + 1].Count;
                }
                if (maxnum != 0)
                {
                    if (maxnum > b)
                        continue;
                    else if (maxnum < b)
                        maxnum = b;
                }
                else
                {
                    if (a > b)
                    {
                        maxnum = a;
                    }
                    else if (a < b)
                    {
                        maxnum = b;
                    }
                }
            }

            for (int i = 0; i < knifecount.Count; i++)
            {
                if (knifecount[i].Count == maxnum)
                {
                    MoBan = knifecount[i];
                    break;
                }
            }

            for (int i = 0; i < MoBan.Count; i++)
            {
                string knifename = MoBan[i];
                for (int j = 0; j < knifecount.Count; j++)
                {
                    if (i < knifecount[j].Count)
                    {
                        string knifename2 = knifecount[j][i];
                        if (knifename == knifename2)
                        {
                            if (!MergeKnifeList.Contains(knifename))
                            {
                                MergeKnifeList.Add(knifename);
                            }
                            else
                            {
                                int index = MergeKnifeList.IndexOf(knifename);
                                if (Math.Abs(index - i) > 2)
                                    MergeKnifeList.Add(knifename);
                            }
                        }
                        else if (knifename != knifename2)
                        {
                            if (!MergeKnifeList.Contains(knifename))
                                MergeKnifeList.Add(knifename);
                            else
                            {
                                int index = MergeKnifeList.IndexOf(knifename);
                                if (Math.Abs(index - i) > 2)
                                    MergeKnifeList.Add(knifename);
                            }
                            if (!MergeKnifeList.Contains(knifename2))
                                MergeKnifeList.Add(knifename2);
                            else
                            {
                                int index = MergeKnifeList.IndexOf(knifename2);
                                if (Math.Abs(index - i) > 2)
                                    MergeKnifeList.Add(knifename2);
                            }
                        }
                    }
                }
            }
            return MergeKnifeList;
        }
    }
}
