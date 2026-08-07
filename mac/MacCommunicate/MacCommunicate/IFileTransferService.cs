using System;

namespace MacCommunicate
{
    /// <summary>
    /// 文件传输服务接口，用于与西门子数控机床通过 SCP/SFTP 传输文件
    /// </summary>
    public interface IFileTransferService
    {
        /// <summary>
        /// 连接机床
        /// </summary>
        bool Connect();

        /// <summary>
        /// 断开连接
        /// </summary>
        void Close();

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 上传文件到机床
        /// </summary>
        /// <param name="localPath">本地文件完整路径</param>
        /// <param name="remotePath">机床内目标路径（含文件名）</param>
        /// <returns>是否成功</returns>
        bool UploadFile(string localPath, string remotePath);

        /// <summary>
        /// 删除机床内文件
        /// </summary>
        /// <param name="remotePath">机床内文件完整路径</param>
        /// <returns>是否成功</returns>
        bool DeleteFile(string remotePath);

        /// <summary>
        /// 从机床下载文件到本地
        /// </summary>
        /// <param name="localPath">本地保存路径（文件夹）</param>
        /// <param name="remotePath">机床内文件完整路径</param>
        /// <returns>是否成功</returns>
        bool DownloadFile(string localPath, string remotePath);
    }
}
