using System;
using System.IO;

namespace MacCommunicate.Tests;

[TestClass]
public class SiemensoperateTests
{
    /// <summary>
    /// 模拟文件传输服务，记录调用以便验证
    /// </summary>
    private class MockFileTransferService : IFileTransferService
    {
        public bool IsConnected { get; private set; }
        public bool ConnectResult { get; set; } = true;
        public bool UploadResult { get; set; } = true;
        public bool DeleteResult { get; set; } = true;
        public bool DownloadResult { get; set; } = true;

        public int ConnectCallCount { get; private set; }
        public int UploadCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }

        public string? LastUploadLocal { get; private set; }
        public string? LastUploadRemote { get; private set; }
        public string? LastDeleteRemote { get; private set; }
        public string? LastDownloadLocal { get; private set; }
        public string? LastDownloadRemote { get; private set; }

        public bool Connect()
        {
            ConnectCallCount++;
            IsConnected = ConnectResult;
            return ConnectResult;
        }

        public void Close()
        {
            IsConnected = false;
        }

        public bool UploadFile(string localPath, string remotePath)
        {
            UploadCallCount++;
            LastUploadLocal = localPath;
            LastUploadRemote = remotePath;
            return UploadResult;
        }

        public bool DeleteFile(string remotePath)
        {
            DeleteCallCount++;
            LastDeleteRemote = remotePath;
            return DeleteResult;
        }

        public bool DownloadFile(string localPath, string remotePath)
        {
            DownloadCallCount++;
            LastDownloadLocal = localPath;
            LastDownloadRemote = remotePath;
            return DownloadResult;
        }
    }

    [TestMethod]
    public void 构造函数_默认创建WinScp实现()
    {
        var op = new Siemensoperate("192.168.1.1", "user", "pass", "ssh-rsa KEY");
        Assert.IsFalse(op.SessionConnected);
    }

    [TestMethod]
    public void UploadProgram_调用文件传输服务的UploadFile()
    {
        var mock = new MockFileTransferService();
        var op = new Siemensoperate("192.168.1.1", "user", "pass", "ssh-rsa KEY",
            fileTransfer: mock);

        string localFile = Path.GetTempFileName();
        File.WriteAllText(localFile, "test nc program content\nline2");

        try
        {
            bool result = op.UploadProgram(localFile, "/card/user/sinumerik/data/mnt/usercf/");
            Assert.IsTrue(result);
            Assert.AreEqual(1, mock.UploadCallCount);
            Assert.AreEqual(localFile, mock.LastUploadLocal);
        }
        finally
        {
            File.Delete(localFile);
        }
    }

    [TestMethod]
    public void UploadProgram_上传失败时返回False()
    {
        var mock = new MockFileTransferService { UploadResult = false };
        var op = new Siemensoperate("192.168.1.1", "user", "pass", "ssh-rsa KEY",
            fileTransfer: mock);

        string localFile = Path.GetTempFileName();
        File.WriteAllText(localFile, "test");

        try
        {
            bool result = op.UploadProgram(localFile, "/card/user/");
            Assert.IsFalse(result);
        }
        finally
        {
            File.Delete(localFile);
        }
    }

    [TestMethod]
    public void UploadProgram_上传时计算程序行数()
    {
        var mock = new MockFileTransferService();
        var op = new Siemensoperate("192.168.1.1", "user", "pass", "ssh-rsa KEY",
            fileTransfer: mock);

        string localFile = Path.GetTempFileName();
        File.WriteAllText(localFile, "line1\nline2\nline3\n");

        try
        {
            op.UploadProgram(localFile, "/card/user/");
            Assert.AreEqual(3, op.AllLine);
        }
        finally
        {
            File.Delete(localFile);
        }
    }

    [TestMethod]
    public void DeleteProgram_调用文件传输服务的DeleteFile()
    {
        var mock = new MockFileTransferService();
        var op = new Siemensoperate("192.168.1.1", "user", "pass", "ssh-rsa KEY",
            fileTransfer: mock);

        bool result = op.DeleteProgram("/card/user/sinumerik/data/mnt/usercf/NCPROGRAM.mpf");
        Assert.IsTrue(result);
        Assert.AreEqual(1, mock.DeleteCallCount);
    }

    [TestMethod]
    public void DownLoadProgram_调用文件传输服务的DownloadFile()
    {
        var mock = new MockFileTransferService();
        var op = new Siemensoperate("192.168.1.1", "user", "pass", "ssh-rsa KEY",
            fileTransfer: mock);

        bool result = op.DownLoadProgram(@"C:\temp\nc_files", "/card/user/sinumerik/data/mnt/usercf/NCPROGRAM.mpf");
        Assert.IsTrue(result);
        Assert.AreEqual(1, mock.DownloadCallCount);
    }

    [TestMethod]
    public void SessionConnected_委托给_fileTransfer的IsConnected()
    {
        var mock = new MockFileTransferService();
        var op = new Siemensoperate("192.168.1.1", "user", "pass", "ssh-rsa KEY",
            fileTransfer: mock);

        Assert.IsFalse(op.SessionConnected);
        mock.Connect();
        Assert.IsTrue(op.SessionConnected);
        mock.Close();
        Assert.IsFalse(op.SessionConnected);
    }
}
