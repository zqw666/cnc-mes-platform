using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MacCommunicate
{
    public class ftp
    {
        /// <summary>
        /// 使用该类的说明：
        /// 1.创建该类的对象最好使用构造函数FtpClass(string DomainName, string UserName, string UserPwd)，可以先给对象初始化IP、用户名、密码
        ///   创建对象后必须调用Ftp_CheckLogin()检查登录信息，设置FtpCheck属性，后续的方法才有效
        /// 2.以下每个函数的参数传递，带path是指完整的路径，带name的是名称和后缀
        ///   服务器的路径格式实例：
        ///     服务器根目录：ftp://172.21.241.29
        ///     服务器某目录：ftp://172.21.241.29/林家明482552/新建文件夹
        ///     服务器某文件：ftp://172.21.241.29/林家明482552/新建文件夹/新建文本.txt
        ///   系统路径实例：
        ///     系统某目录：F:\【FTP上传和下载】\林家明482552
        ///     系统某文件：F:\【FTP上传和下载】\林家明482552\新建文件夹\新建文本.txt
        /// 3.目前存在的问题：
        ///   ①服务器上的文件命名不能有空格，否则显示不正常，文件名不能含“DIR”字眼，否则误认为是文件夹，导致误操作。
        ///     FTP服务器返回的信息部分示例：12-23-19  06:53PM       <DIR>          年度总结
        ///    （不能带空格是因为FTP服务器返回的数据靠空格截取分析，不能带DIR字眼是因为FTP服务器返回的字符串就是靠该字眼进行区分文件夹和文件的）
        ///   ②服务器上在Windows操作下不能创建带半角#符号名称的文件夹或文件，否则无法删除，导致无限递归以致内存溢出，全角＃符号可以，但还是禁止比较好
        ///   
        /// </summary>
        private const int Ftp_StreamBuffer_Lenth = 3 * 1024 * 1024;//FTP流大小,字节为单位
        private const long Ftp_Kb = 1024;//Kb的字节大小
        private const long Ftp_Mb = 1024 * 1024;//Mb的字节大小
        private const long Ftp_Gb = 1024 * 1024 * 1024;//Gb的字节大小
        public int FtpStreamBufferLenth { get { return Ftp_StreamBuffer_Lenth; } }//获取FTP流大小属性
        private string FtpEncoding = "UTF-8";//获取流信息的编码方式
        public string FtpServerIP;//记录服务器的IP
        public string FtpUserName;//记录登录的用户名
        public string FtpUserPwd;//记录登录用户的密码

        /// <summary>
        /// 获取FTP登录验证
        /// </summary>
        public bool FtpCheckLogin { get; private set; }

        /// <summary>
        /// 路径信息结构体
        /// </summary>
        public struct ServerPathDetial
        {
            public string[] Floders_Files_Name;             //当前路径的所有文件夹名和所有文件名
            public string[] Floders_Files_Style;            //当前路径的所有文件夹、文件类型
            public string[] Floders_Files_Size;             //当前路径的所有文件大小、文件夹为 ""
            public string[] Floders_Files_LastWriteTime;    //当前路径的所有文件夹、文件最后修改的时间
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="DomainName"></param>服务器
        /// <param name="UserName"></param>登录用户名
        /// <param name="UserPwd"></param>登录密码
        public ftp(string DomainName, string UserName, string UserPwd)
        {
            FtpServerIP = DomainName;
            FtpUserName = UserName;
            FtpUserPwd = UserPwd;
        }

        /// <summary>
        /// 验证登录用户是否合法
        /// </summary>
        /// <returns></returns>
        public bool Ftp_CheckLogin()
        {
            try
            {
                if (!FtpCheckLogin)
                {
                    //创建FtpWebRequest对象
                    FtpWebRequest ftprequest = (FtpWebRequest)WebRequest.Create(new Uri("ftp://" + FtpServerIP));//设置FTP登录信息
                    ftprequest.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//发送一个请求命令
                    ftprequest.Method = WebRequestMethods.Ftp.ListDirectory;//响应一个请求
                    FtpWebResponse ftpResponse = (FtpWebResponse)ftprequest.GetResponse();//关闭请求
                    Thread.Sleep(3000);
                    ftpResponse.Close();
                    FtpCheckLogin = true;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return FtpCheckLogin;
        }

        /// <summary>
        /// 从FTP服务器上获得指定文件夹列表，ftpServerIP为IP则获取根目录
        /// FTP服务器返回的信息部分示例：
        /// 12-23-19  06:53PM       <DIR>          年度总结
        /// 12-23-19  06:53PM                      年度总结.jpg
        /// 12-23-19  06:59PM                      新建文本文档.txt
        /// </summary>
        /// <param name="ftpServerPath"></param>服务器地址目录
        /// <returns></returns>
        public string[] Ftp_GetFileList(string ftpServerPath)
        {
            string[] FileList = null;     //声明一个数组
            if (ftpServerPath == null || ftpServerPath == "")
                return FileList;
            if (FtpCheckLogin)
            {
                StringBuilder result = new StringBuilder(); //实例化StringBuilder对象
                FtpWebRequest reqFTP;   //声明一个FtpWebRequest变量
                try
                {
                    reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(ftpServerPath));//实例化FtpWebRequest
                    reqFTP.UseBinary = true;  //指定文件传输类型
                    reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//设置与服务器通信的凭据
                    reqFTP.Method = WebRequestMethods.Ftp.ListDirectoryDetails;//发送FTP服务器命令
                    WebResponse response = reqFTP.GetResponse();//实例化WebResponse

                    //实例化StreamReader
                    StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding(FtpEncoding));//读取流信息
                    string line = reader.ReadLine();
                    while (line != null)//循环读取文件列表
                    {
                        result.Append(line);
                        result.Append("\n");
                        line = reader.ReadLine();
                    }
                    result.Remove(result.ToString().LastIndexOf('\n'), 1);//去掉最后添加的换行
                    reader.Close();                     //关闭StreamReader
                    response.Close();
                    FileList = result.ToString().Split('\n');
                    //按文件夹到文件进行排序
                    //因为FTP服务器是按字母排序的
                    int num = 0;
                    foreach (string str in FileList)
                    {
                        if (str.IndexOf("DIR") != -1)//是文件夹
                        {
                            //上浮法排序
                            for (int i = num; i > 0; i--)
                            {
                                if (FileList[i - 1].IndexOf("DIR") == -1)
                                {
                                    string strChage = FileList[i];
                                    FileList[i] = FileList[i - 1];
                                    FileList[i - 1] = strChage;
                                }
                            }
                        }
                        num++;
                    }
                    return FileList;//返回获取列表内容
                }
                catch(Exception ex)
                {
                    FileList = null;
                    throw ex;
                }
            }
            return FileList;
        }

        /// <summary>
        /// 获取FTP服务器指定文件大小
        /// </summary>
        /// <param name="ftpServerPath"></param>文件路径
        /// <returns></returns>
        public long Ftp_GetFileSize(string ftpServerPath)
        {
            if (ftpServerPath == null || ftpServerPath == "")
                return -1;
            long filesize = 0; //定义存储文件大小的变量
            if (FtpCheckLogin)
            {
                try
                {
                    FtpWebRequest ftprequest = (FtpWebRequest)WebRequest.Create(new Uri(ftpServerPath));//设置FTP登录信息
                    ftprequest.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//发送一个请求命令
                    ftprequest.Method = WebRequestMethods.Ftp.GetFileSize;             //设置Method属性
                    FtpWebResponse response = (FtpWebResponse)ftprequest.GetResponse();//创建FtpWebResponse实例
                    filesize = response.ContentLength;                             //获取文件大小
                }
                catch(Exception ex)
                {
                    throw ex;
                }
            }
            return filesize;
        }

        /// <summary>
        /// 把传入的日期字符串参数进行反向转换为标准的日期字符串
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public string Ftp_Oppisite(string str)
        {
            string[] strDate = str.Split('-');
            string value = strDate[strDate.Length - 1] + "-" + strDate[0] + "-" + strDate[1];//分别获取年月日
            return value.ToString();
        }

        /// <summary>
        /// 单个或批量上传文件夹/文件
        /// 批量上传的基本思路是上传时判断选中的是文件还是文件夹，如果是文件则调用上传文件的方法进行上传，如果是文件夹则首先在服务器端创建同名文件夹，
        /// 然后再遍历本地文件夹，循环上传文件夹中的文件。Ftp_Upload方法用于上传单个文件
        /// </summary>
        /// <param name="FilePath"></param>要上传的文件名，需要带路径的标准FileInfo本地文件路径
        /// <param name="ftpServerPath"></param>要上传的服务器路径
        /// <returns></returns>
        public bool Ftp_UploadFile(string FilePath, string ftpServerPath)
        {
            if (ftpServerPath == null || ftpServerPath == "")
                return false;
            //声明全局变量
            bool success = true;
            if (FtpCheckLogin)
            {
                FileInfo fileInf = new FileInfo(FilePath);//新建一个FileInfo对象
                int allbye = (int)fileInf.Length;
                int startbye = 0;
                string UpLoadFileName = fileInf.Name;
                if (fileInf.Name.IndexOf('#') != -1)
                {
                    UpLoadFileName = UpLoadFileName.Replace("#", "＃");//把文件名称中的半角#换为全角＃
                }
                string uri = ftpServerPath + "/" + UpLoadFileName.Replace(" ", "");//在当前操作的服务器地址上新建一个路径，去掉空格，fileInf.Name只获取文件名和后缀
                FtpWebRequest reqFTP;
                reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(uri));//根据uri创建FtpWebRequest对象
                reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//ftp用户名和密码

                reqFTP.KeepAlive = false;//默认为true，连接不会被关闭，在一个命令之后被执行
                reqFTP.Method = WebRequestMethods.Ftp.UploadFile;//指定执行什么命令
                reqFTP.UseBinary = true;//指定数据传输类型
                reqFTP.ContentLength = fileInf.Length;//上传文件时通知服务器文件的大小
                int buffLength = Ftp_StreamBuffer_Lenth;// 缓冲大小设置为kb 
                byte[] buff = new byte[buffLength];
                //打开一个文件流(System.IO.FileStream) 去读上传的文件
                FileStream fs = fileInf.OpenRead();
                try
                {
                    Stream strm = reqFTP.GetRequestStream();//把上传的文件写入流
                    int contentLen = fs.Read(buff, 0, buffLength);//每次读文件流的kb 
                    while (contentLen != 0)//流内容没有结束
                    {
                        //把内容从file stream写入upload stream 
                        strm.Write(buff, 0, contentLen);
                        contentLen = fs.Read(buff, 0, buffLength);
                        startbye += contentLen;
                    }
                    //关闭两个流
                    strm.Close();
                    fs.Close();
                }
                catch(Exception ex)
                {
                    success = false;
                    throw ex;
                }
            }
            else
                return false;
            return success;
        }

        /// <summary>
        /// 单个或批量上传文件夹/文件
        /// 批量上传的基本思路是上传时判断选中的是文件还是文件夹，如果是文件则调用上传文件的方法进行上传，如果是文件夹则首先在服务器端创建同名文件夹，
        /// 然后再遍历本地文件夹，循环上传文件夹中的文件。Ftp_Upload方法用于上传单个文件
        /// </summary>
        /// <param name="FilePath"></param>要上传的文件名，需要带路径的标准FileInfo本地文件路径
        /// <param name="pb"></param>进度条
        /// <param name="ftpServerPath"></param>要上传的服务器路径
        /// <returns></returns>
        public bool Ftp_UploadFiles(string FilePath, string ftpServerPath)
        {
            if (ftpServerPath == null || ftpServerPath == "")
                return false;
            //声明全局变量
            bool success = true;
            if (FtpCheckLogin)
            {
                FileInfo fileInf = new FileInfo(FilePath);//新建一个FileInfo对象
                int allbye = (int)fileInf.Length;
                int startbye = 0;
                string UpLoadFileName = fileInf.Name;
                if (fileInf.Name.IndexOf('#') != -1)
                {
                    UpLoadFileName = UpLoadFileName.Replace("#", "＃");//把文件名称中的半角#换为全角＃
                }
                string uri = ftpServerPath + "/" + UpLoadFileName.Replace(" ", "");//在当前操作的服务器地址上新建一个路径，去掉空格，fileInf.Name只获取文件名和后缀
                FtpWebRequest reqFTP;
                reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(uri));//根据uri创建FtpWebRequest对象
                reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//ftp用户名和密码

                reqFTP.KeepAlive = false;//默认为true，连接不会被关闭，在一个命令之后被执行
                reqFTP.Method = WebRequestMethods.Ftp.UploadFile;//指定执行什么命令
                reqFTP.UseBinary = true;//指定数据传输类型
                reqFTP.ContentLength = fileInf.Length;//上传文件时通知服务器文件的大小
                int buffLength = Ftp_StreamBuffer_Lenth;// 缓冲大小设置为kb 
                byte[] buff = new byte[buffLength];
                //打开一个文件流(System.IO.FileStream) 去读上传的文件
                FileStream fs = fileInf.OpenRead();
                try
                {
                    Stream strm = reqFTP.GetRequestStream();//把上传的文件写入流
                    int contentLen = fs.Read(buff, 0, buffLength);//每次读文件流的kb 
                    while (contentLen != 0)//流内容没有结束
                    {
                        //把内容从file stream写入upload stream 
                        strm.Write(buff, 0, contentLen);
                        contentLen = fs.Read(buff, 0, buffLength);
                        startbye += contentLen;
                    }
                    //关闭两个流
                    strm.Close();
                    fs.Close();
                }
                catch(Exception ex)
                {
                    success = false;
                    throw ex;
                }
            }
            else
                return false;
            return success;
        }

        /// <summary>
        /// 在服务器指定目录新建文件夹
        /// 注：原则上不允许创建带#的文件夹
        /// </summary>
        /// <param name="ftpServerPath"></param>需要创建的服务器完整目录，不能带#
        /// <returns></returns>
        public bool Ftp_CreateFloder(string ftpServerPath)
        {
            bool flag = false;
            string uri = ftpServerPath.Replace(" ", "");//去空格;
            if (ftpServerPath.IndexOf('#') != -1 || ftpServerPath.IndexOf('＃') != -1)
            {
                return false;
            }
            else if (uri.EndsWith("/"))
            {
                return false;
            }
            if (FtpCheckLogin)
            {
                try
                {
                    //声明一个uri变量用于建立服务器连接
                    //string uri = ftpServerPath.Replace("#","＃");//去符号#
                    //uri.Replace(" ","");//去空格
                    FtpWebRequest reqFTP;
                    reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(uri));//根据uri创建FtpWebRequest对象
                    reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//ftp用户名和密码
                    reqFTP.Method = WebRequestMethods.Ftp.MakeDirectory;//设置Method属性发送MakeDirectory命令创建目录
                    FtpWebResponse response = (FtpWebResponse)reqFTP.GetResponse();  //返回响应
                    response.Close();//断开
                    flag = true;
                }
                catch (Exception ex)
                {
                    Ftp_DeleteFloder(uri);
                    if (Ftp_CreateFloder(uri))
                        flag = true;
                    throw ex;
                }
            }
            else
                return false;
            return flag;
        }

        /// <summary>
        /// 上传本地文件夹到服务器上
        /// 注：禁止上传带符号#的文件夹
        /// </summary>
        /// <param name="LocalFloder"></param>需要上传的本地文件夹的完整路径
        /// <param name="localFloderName"></param>需要上传的文件夹的名称，仅名称不带路径
        /// <param name="ftpServerPath"></param>需要上传到服务器上的根路径
        /// <returns></returns>
        public bool Ftp_UploadFloders(string LocalFloder, string localFloderName, string ftpServerPath)
        {
            bool flag = true;
            if (localFloderName.IndexOf('#') != -1 || localFloderName.IndexOf('＃') != -1)
            {
                return false;
            }
            if (FtpCheckLogin)
            {
                try
                {
                    string[] fileList = Directory.GetFileSystemEntries(LocalFloder);//得到源目录的文件列表，里面是包含文件以及目录路径的一个数组
                    string ServerFloder = ftpServerPath + "/" + localFloderName;//需要在服务器上创建的文件夹路径
                    if (Ftp_CreateFloder(ServerFloder))//在服务器上新建同名文件夹
                    {
                        foreach (string file in fileList)//遍历所有的文件和目录
                        {
                            string[] pathdetial = file.Split('\\');//Windows系统的目录：D:\学习资料\笔记\C#
                            string pathname = pathdetial[pathdetial.Length - 1];//获取到文件夹或文件的名称
                            if (Directory.Exists(file))//判断是否是文件夹
                            {
                                Ftp_UploadFloder(file, pathname, ServerFloder);//把路径下的文件夹上传到服务器的新建同名文件夹
                            }
                            else
                            {
                                Ftp_UploadFile(file, ServerFloder);//上传文件
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    flag = false;
                    throw ex;
                }
            }
            else
                return false;
            return flag;
        }


        /// <summary>
        /// 上传本地文件夹到服务器上
        /// 注：禁止上传带符号#的文件夹
        /// </summary>
        /// <param name="LocalFloder"></param>需要上传的本地文件夹路径
        /// <param name="localFloderName"></param>需要上传的文件夹的名称，仅名称不带路径
        /// <param name="pb"></param>进度条
        /// <param name="ftpServerPath"></param>需要上传到服务器上的路径
        /// <returns></returns>
        public bool Ftp_UploadFloder(string LocalFloder, string localFloderName, string ftpServerPath)
        {
            bool flag = true;
            if (localFloderName.IndexOf('#') != -1 || localFloderName.IndexOf('＃') != -1)
            {
                return false;
            }

            if (FtpCheckLogin)
            {
                try
                {
                    string[] fileList = Directory.GetFileSystemEntries(LocalFloder);//得到源目录的文件列表，里面是包含文件以及目录路径的一个数组
                    string ServerFloder = ftpServerPath + "/" + localFloderName;//需要在服务器上创建的文件夹路径
                    if (Ftp_CreateFloder(ServerFloder))//在服务器上新建同名文件夹
                    {
                        foreach (string file in fileList)//遍历所有的文件和目录
                        {
                            string[] pathdetial = file.Split('\\');//Windows系统的目录：D:\学习资料\笔记\C#
                            string pathname = pathdetial[pathdetial.Length - 1];//获取到文件夹或文件的名称
                            if (Directory.Exists(file))//判断是否是文件夹
                            {
                                Ftp_UploadFloder(file, pathname, ServerFloder);//把路径下的文件夹上传到服务器的新建同名文件夹
                            }
                            else
                            {
                                Ftp_UploadFile(file, ServerFloder);//上传文件
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    flag = false;
                    throw ex;
                }
            }
            else
                return false;
            return flag;
        }

        /// <summary>
        /// 删除服务器上的文件夹
        /// </summary>
        /// <param name="dirName"></param>
        public void Ftp_DeleteFloder(string ftpServerPath)
        {
            if (FtpCheckLogin)
            {
                try
                {
                    FtpWebRequest reqFTP;
                    StringBuilder result = new StringBuilder(); //实例化StringBuilder对象
                    reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(ftpServerPath));//根据uri创建FtpWebRequest对象
                    reqFTP.KeepAlive = true;//保持连接
                    reqFTP.UseBinary = true;  //指定文件传输类型
                    reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//设置与服务器通信的凭据
                    reqFTP.Method = WebRequestMethods.Ftp.ListDirectoryDetails;//发送FTP服务器命令
                    WebResponse response = reqFTP.GetResponse();//实例化WebResponse，注：该行删除名为半角的“#”的文件夹会导致无限递归，直到内存溢出
                                                                //删除全角的“＃”不会出现该种情况
                                                                //实例化StreamReader
                    StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding(FtpEncoding));//读取流信息
                    string line = reader.ReadLine();
                    while (line != null)//循环读取文件列表
                    {
                        result.Append(line);
                        result.Append("\n");
                        line = reader.ReadLine();
                    }
                    reader.Close();                     //关闭StreamReader
                    if (result.Length > 0)
                    {
                        string[] Path = result.ToString().Trim('\n').Split('\n');
                        if (Path != null)//文件夹下不为空
                        {
                            string[] Floders_Files_Name = new string[Path.Length];
                            for (int i = 0; i < Path.Length; i++)
                            {
                                string[] FloderDetial = Path[i].ToString().Split(' ');//获取的一行信息为： 12-23-19  06:53PM       <DIR>          年度总结
                                string FloderOrFileName = FloderDetial[FloderDetial.Length - 1];//最后的字符串就是文件夹或文件名，获取该名称
                                //判断是否是文件夹
                                if (Path[i].IndexOf("DIR") != -1)
                                    Ftp_DeleteFloder(ftpServerPath + "/" + FloderOrFileName);//文件夹，递归删除
                                else
                                    Ftp_DeleteFile(ftpServerPath + "/" + FloderOrFileName);//文件，直接删除
                            }
                        }
                    }
                    reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(ftpServerPath));//根据uri创建FtpWebRequest对象
                    reqFTP.UseBinary = false;  //指定文件传输类型
                    reqFTP.KeepAlive = false;//保持连接
                    reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//设置与服务器通信的凭据
                    reqFTP.Method = WebRequestMethods.Ftp.RemoveDirectory;//向服务器发送删除文件夹的命令
                    response = (FtpWebResponse)reqFTP.GetResponse();
                    response.Close();  //关闭
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        /// <summary>
        /// 删除服务器上的指定文件
        /// </summary>
        /// <param name="FilePath"></param>
        public void Ftp_DeleteFile(string ftpFilePath)
        {
            if (FtpCheckLogin)
            {
                try
                {
                    FtpWebRequest reqFTP;
                    reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(ftpFilePath));//根据uri创建FtpWebRequest对象
                    reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//ftp用户名和密码
                    reqFTP.UseBinary = true;
                    reqFTP.KeepAlive = false;//在一个命令之后被执行
                    reqFTP.Method = WebRequestMethods.Ftp.DeleteFile;//指定执行什么命令
                    FtpWebResponse response = (FtpWebResponse)reqFTP.GetResponse();

                    response.Close();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        /// <summary>
        /// 计算服务器文件夹下的文件
        /// </summary>
        /// <param name="strServerPath"></param>服务器文件夹路径,如果是文件路径
        /// <returns></returns>
        public int Ftp_CountFile(string strServerPath)
        {
            int Count = 0;
            try
            {
                string[] PathDetial = Ftp_GetFileList(strServerPath);
                foreach (string str in PathDetial)
                {
                    if (str.IndexOf("DIR") == -1)//不是文件夹
                        Count++;
                    else
                    {
                        string[] Floder = str.Split(' ');
                        Count += Ftp_CountFile(strServerPath + "/" + Floder[Floder.Length - 1]);
                    }
                }
            }
            catch (Exception ex)
            {
                Count = -1;
                throw ex;
            }
            return Count;
        }

        public bool Ftp_CheckDirExist(string ftpserverpath)
        {
            bool flag = false;
            if (FtpCheckLogin)
            {
                try
                {
                    FtpWebRequest reqFTP;
                    StringBuilder result = new StringBuilder(); //实例化StringBuilder对象
                    reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(ftpserverpath));//根据uri创建FtpWebRequest对象
                    reqFTP.KeepAlive = true;//保持连接
                    reqFTP.UseBinary = true;  //指定文件传输类型
                    reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//设置与服务器通信的凭据
                    reqFTP.Method = WebRequestMethods.Ftp.ListDirectoryDetails;//发送FTP服务器命令
                    WebResponse response = reqFTP.GetResponse();//实例化WebResponse，注：该行删除名为半角的“#”的文件夹会导致无限递归，直到内存溢出
                                                                //删除全角的“＃”不会出现该种情况
                                                                //实例化StreamReader
                    StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding(FtpEncoding));//读取流信息
                    string line = reader.ReadLine();
                    while (line != null)//循环读取文件列表
                    {
                        result.Append(line);
                        result.Append("\n");
                        line = reader.ReadLine();
                    }
                    reader.Close();
                    if (result.Length > 0)
                        flag = true;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return flag;
        }

        /// <summary>
        /// 从FTP服务器上下载文件夹
        /// 注：如果选择保存的目录中有同名的文件，则直接保存在该文件夹内
        /// </summary>
        /// <param name="LocalPath"></param>保存该文件夹的本地路径，如果不存在同名文件夹软件在选择的路径自动创建下载文件夹同名的文件夹
        /// <param name="ftpServerFloderName"></param>需要下载服务器上的文件夹的名称
        /// <param name="ftpServerPath"></param>需要下载服务器上的文件夹的完整路径
        public void Ftp_DownLoadFloder(string LocalPath, string ftpServerFloderName, string ftpServerPath)
        {
            DirectoryInfo di;                                         //声明DirectoryInfo变量

            if (FtpCheckLogin)
            {
                try
                {
                    string savepath = LocalPath + "\\" + ftpServerFloderName;//新建路径
                    di = new DirectoryInfo(savepath);                        //根据路径实例化DirectoryInfo对象
                    if (!di.Exists)//判断文件夹是否存在
                        di.Create();                                      //在本地创建文件夹
                    string[] pathList = Ftp_GetFileList(ftpServerPath);   //获取服务器上该文件夹下的信息
                    if (pathList == null)                                          //判断是否存在文件
                    { }
                    else
                    {
                        foreach (string file in pathList)                           //遍历文件列表
                        {
                            string[] pathdetial = file.Split(' ');//FTP服务器返回的信息部分示例：12-23-19  06:53PM       <DIR>          年度总结
                            string pathname = pathdetial[pathdetial.Length - 1];//最后的字符串就是文件夹或文件名称

                            if (file.IndexOf("DIR") != -1)    //判断是否是文件夹
                            {
                                Ftp_DownLoadFloder(savepath, pathname, ftpServerPath + "/" + pathname);//递归下载文件夹
                            }
                            else
                            {
                                Ftp_DownloadFile(savepath, pathname, ftpServerPath + "/" + pathname);//下载文件
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }


        /// <summary>
        /// 从FTP服务器上下载文件
        /// 注：保存的路径中如果包含同名的文件，会覆盖掉原文件
        /// </summary>
        /// <param name="LocalPath"></param>需要保存文件的路径，仅路径不带文件名和后缀，因为是根据传参下载文件名创建新文件
        /// <param name="ftpServerFileName"></param>需要下载的文件名
        /// <param name="ftpServerPath"></param>需要下载的文件在服务器上的完整路径
        /// <returns></returns>
        public bool Ftp_DownloadFile(string LocalPath, string ftpServerFileName, string ftpServerPath)
        {
            //设置全局变量
            bool check = false;
            FtpWebRequest reqFTP;
            if (FtpCheckLogin)
            {
                try
                {
                    FileStream outputStream = new FileStream(LocalPath + "\\" + ftpServerFileName, FileMode.Create);//实例化FileStream对象
                    reqFTP = (FtpWebRequest)FtpWebRequest.Create(new Uri(ftpServerPath));//实例化FtpWebRequest对象建立一个新的连接
                    reqFTP.Method = WebRequestMethods.Ftp.DownloadFile;//设置FtpWebRequest对象的Method属性向服务器发送DownloadFile命令下载文件
                    reqFTP.UseBinary = true;
                    reqFTP.KeepAlive = true;//设置文件传输类型
                    reqFTP.Credentials = new NetworkCredential(FtpUserName, FtpUserPwd);//与服务器通信的凭据
                    Thread.Sleep(3000);
                    FtpWebResponse response = (FtpWebResponse)reqFTP.GetResponse();//返回响应
                    Stream ftpStream = response.GetResponseStream();   //实例化Stream对象
                    int bufferSize = (int)response.ContentLength;
                    byte[] buffer = new byte[bufferSize];
                    int readCount = ftpStream.Read(buffer, 0, bufferSize);
                    while (readCount > 0)
                    {
                        outputStream.Write(buffer, 0, readCount);//把数据写入文件
                        readCount = ftpStream.Read(buffer, 0, bufferSize);//读取数据
                    }
                    check = true;
                    ftpStream.Close();
                    outputStream.Close();
                    response.Close();
                }
                catch (Exception ex)
                {
                    check = false;
                    throw ex;
                }
            }
            else
                check = false;
            return check;
        }

        public void DisConnect()
        {

        }
    }
}
