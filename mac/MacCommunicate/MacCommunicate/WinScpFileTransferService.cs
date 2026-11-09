using System;
using WinSCP;

namespace MacCommunicate
{
    /// <summary>
    /// 基于 WinSCP（SCP 协议）的文件传输服务实现，用于与西门子数控机床通信
    /// </summary>
    public class WinScpFileTransferService : IFileTransferService
    {
        private readonly Session _session = new Session();
        private readonly string _hostName;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _hostKeyFingerprint;
        private readonly int _port;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="hostName">机床 IP 地址</param>
        /// <param name="userName">SSH 登录用户名（默认 user）</param>
        /// <param name="password">SSH 登录密码（默认 CUSTOMER）</param>
        /// <param name="hostKeyFingerprint">SSH 主机密钥指纹</param>
        public WinScpFileTransferService(string hostName, string userName, string password, string hostKeyFingerprint, int port = 22)
        {
            _hostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
            _userName = userName ?? "user";
            _password = password ?? "CUSTOMER";
            _hostKeyFingerprint = hostKeyFingerprint ?? "";
            _port = port;
        }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _session.Opened;

        /// <summary>
        /// 连接机床
        /// </summary>
        public bool Connect()
        {
            if (_session.Opened)
                return true;

            try
            {
                var options = new SessionOptions
                {
                    Protocol = Protocol.Scp,
                    HostName = _hostName,
                    UserName = _userName,
                    Password = _password,
                    SshHostKeyFingerprint = _hostKeyFingerprint,
                            PortNumber = _port,
                };
                _session.Open(options);
                return _session.Opened;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Close()
        {
            try
            {
                if (_session.Opened)
                    _session.Close();
            }
            catch
            {
                // 忽略关闭时的异常
            }
        }

        /// <summary>
        /// 上传文件到机床
        /// </summary>
        public bool UploadFile(string localPath, string remotePath)
        {
            try
            {
                // 如果连接已断开则重新连接
                if (!_session.Opened && !Connect())
                    return false;

                var transferOptions = new TransferOptions { TransferMode = TransferMode.Binary };
                var result = _session.PutFiles(localPath, remotePath, false, transferOptions);
                return result.IsSuccess;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 删除机床内文件
        /// </summary>
        public bool DeleteFile(string remotePath)
        {
            try
            {
                var result = _session.RemoveFiles(remotePath);
                return result.IsSuccess;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 从机床下载文件到本地
        /// </summary>
        public bool DownloadFile(string localPath, string remotePath)
        {
            try
            {
                var transferOptions = new TransferOptions { TransferMode = TransferMode.Binary };
                var result = _session.GetFiles(remotePath, localPath.TrimEnd('\\') + "\\", false, transferOptions);
                return result.IsSuccess;
            }
            catch
            {
                return false;
            }
        }
    }
}
