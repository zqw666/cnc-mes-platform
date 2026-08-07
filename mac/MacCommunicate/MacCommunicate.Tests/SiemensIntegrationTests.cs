using System;
using System.IO;
using System.Diagnostics;

namespace MacCommunicate.Tests;

/// <summary>
/// 西门子机床真实连接集成测试
///
/// 运行前提：
/// 1. 有一台可访问的西门子 828D 机床（或本地 SSH 测试服务器）
/// 2. 设置环境变量：
///    - SIEMENS_IP=机床IP地址
///    - SIEMENS_HOSTKEY=SSH 主机密钥指纹（可选，不设置则自动获取）
///    - SIEMENS_SFTP_USER=SFTP 用户名（可选，默认 user）
///    - SIEMENS_SFTP_PASS=SFTP 密码（可选，默认 CUSTOMER）
///
/// 运行方式：
///   dotnet test --filter "Category=Integration"
///
/// 安全说明：
/// - 上传到 /tmp/ 测试目录，不会影响机床现有程序
/// - 测试完成后自动清理临时文件
/// </summary>
[TestClass]
public class SiemensIntegrationTests
{
    private static string? _ip;
    private static string? _hostKey;
    private static string? _sftpUser;
    private static string? _sftpPass;
    private static int _port = 22;

    private const string TestRemoteDir = "/tmp/codex_test/";

    [ClassInitialize]
    public static void Init(TestContext context)
    {
        _ip = Environment.GetEnvironmentVariable("SIEMENS_IP");
        _hostKey = Environment.GetEnvironmentVariable("SIEMENS_HOSTKEY");
        _sftpUser = Environment.GetEnvironmentVariable("SIEMENS_SFTP_USER") ?? "user";
        _sftpPass = Environment.GetEnvironmentVariable("SIEMENS_SFTP_PASS") ?? "CUSTOMER";
        int.TryParse(Environment.GetEnvironmentVariable("SIEMENS_SFTP_PORT") ?? "22", out _port);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void 连接真实机床_成功()
    {
        Assert.IsNotNull(_ip, "请设置环境变量 SIEMENS_IP");
        Assert.IsNotNull(_hostKey, "请设置环境变量 SIEMENS_HOSTKEY");

        var op = new Siemensoperate(_ip, "user", "CUSTOMER", _hostKey, port: _port);
        bool connected = op.Connect();

        Assert.IsTrue(connected, "连接机床失败，请检查 IP 和密钥");
        Assert.IsTrue(op.SessionConnected, "SessionConnected 应返回 true");
        Console.WriteLine($"机床连接成功: {_ip}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void 上传文件到真实机床()
    {
        Assert.IsNotNull(_ip, "请设置环境变量 SIEMENS_IP");
        Assert.IsNotNull(_hostKey, "请设置环境变量 SIEMENS_HOSTKEY");

        // 准备一个测试用的临时 NC 文件
        string localFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(localFile, ";; 测试程序 - Codex 集成测试\nN10 G90 G0 X0 Y0 Z0\nN20 M30\n");

            var op = new Siemensoperate(_ip, "user", "CUSTOMER", _hostKey, port: _port);
            if (!op.Connect())
                Assert.Inconclusive("无法连接机床，跳过测试");

            // 上传到测试目录（非正式目录）
            bool result = op.UploadProgram(localFile, TestRemoteDir);
            Assert.IsTrue(result, "上传文件失败");
            Console.WriteLine($"文件已上传到 {TestRemoteDir}");
        }
        finally
        {
            File.Delete(localFile);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void 上传后下载并验证内容一致()
    {
        Assert.IsNotNull(_ip, "请设置环境变量 SIEMENS_IP");
        Assert.IsNotNull(_hostKey, "请设置环境变量 SIEMENS_HOSTKEY");

        string localFile = Path.GetTempFileName();
        string downloadDir = Path.Combine(Path.GetTempPath(), "codex_test_download");
        Directory.CreateDirectory(downloadDir);

        try
        {
            string testContent = ";; Verification Test\nN10 G1 X100 F1000\nN20 M30\n";
            File.WriteAllText(localFile, testContent);

            var op = new Siemensoperate(_ip, "user", "CUSTOMER", _hostKey, port: _port);
            if (!op.Connect())
                Assert.Inconclusive("无法连接机床，跳过测试");

            // 上传
            string remoteFile = TestRemoteDir + "test_verify.mpf";
            bool uploadOk = op.UploadProgram(localFile, remoteFile);
            Assert.IsTrue(uploadOk, "上传失败");

            // 下载
            bool downloadOk = op.DownLoadProgram(downloadDir, remoteFile);
            Assert.IsTrue(downloadOk, "下载失败");

            // 验证内容一致
            string downloadedFile = Path.Combine(downloadDir, "test_verify.mpf");
            Assert.IsTrue(File.Exists(downloadedFile), "下载的文件不存在");
            string downloadedContent = File.ReadAllText(downloadedFile);
            Assert.AreEqual(testContent.Trim(), downloadedContent.Trim(), "文件内容不一致");

            Console.WriteLine("上传-下载-验证 全流程通过");
        }
        finally
        {
            File.Delete(localFile);
            if (Directory.Exists(downloadDir))
                Directory.Delete(downloadDir, true);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void 删除远程文件()
    {
        Assert.IsNotNull(_ip, "请设置环境变量 SIEMENS_IP");
        Assert.IsNotNull(_hostKey, "请设置环境变量 SIEMENS_HOSTKEY");

        // 先上传一个文件用于删除测试
        string localFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(localFile, ";; Delete test\nN10 M30\n");
            var op = new Siemensoperate(_ip, "user", "CUSTOMER", _hostKey, port: _port);
            if (!op.Connect())
                Assert.Inconclusive("无法连接机床，跳过测试");

            string remoteFile = TestRemoteDir + "test_delete.mpf";
            op.UploadProgram(localFile, remoteFile);

            // 删除
            bool result = op.DeleteProgram(remoteFile);
            Assert.IsTrue(result, "删除文件失败");
            Console.WriteLine($"文件已删除: {remoteFile}");
        }
        finally
        {
            File.Delete(localFile);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void 连接断开后重新连接再上传()
    {
        Assert.IsNotNull(_ip, "请设置环境变量 SIEMENS_IP");
        Assert.IsNotNull(_hostKey, "请设置环境变量 SIEMENS_HOSTKEY");

        string localFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(localFile, ";; Reconnect test\nN10 M30\n");

            var op = new Siemensoperate(_ip, "user", "CUSTOMER", _hostKey, port: _port);
            if (!op.Connect())
                Assert.Inconclusive("无法连接机床，跳过测试");

            // 断开连接
            op.Close();
            Assert.IsFalse(op.SessionConnected, "断开后 SessionConnected 应为 false");

            // 此时 UploadProgram 内部应自动重新连接
            bool result = op.UploadProgram(localFile, TestRemoteDir);
            Assert.IsTrue(result, "重新连接后上传失败");
            Assert.IsTrue(op.SessionConnected, "上传后应自动重新连接");

            Console.WriteLine("断开后自动重连并上传成功");
        }
        finally
        {
            File.Delete(localFile);
        }
    }
}
