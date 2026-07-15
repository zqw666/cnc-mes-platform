using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rcd
{
    /// <summary>
    /// 返回的客户端ip类
    /// </summary>
    public class ReturnClientMess : EventArgs
    {
        public string Mess { get; set; }
        public string Function { get; set; }
    }

    public class RCD_100
    {
        public string ReturnMess = string.Empty;
        private bool isConnect = false;
        private string Ip = string.Empty;
        private string Port = string.Empty;

        #region 事件
        public event EventHandler<string> MessageSent;//返回信息到测试界面
        public event EventHandler<ReturnClientMess> SentClientMess;//返回客户端信息到测试界面
        #endregion


        #region 任务
        private TcpClient tcpClient;
        private int heartbeatInterval;
        ManualResetEvent m;//中断信号
        CancellationTokenSource ListenTokenSoure;//取消线程信号源
        #endregion



        public RCD_100(string ip, string port, int heartbeatIntervalInSeconds)
        {
            heartbeatInterval = heartbeatIntervalInSeconds * 1000;
            this.Ip = ip;
            this.Port = port;
        }
        /// <summary>
        /// 返回信息到测试界面
        /// </summary>
        /// <param name="mess"></param>
        public void SenMessageToWinForm(string mess)
        {
            //触发事件，将消息发送给订阅者
            MessageSent?.Invoke(this, mess);
        }

        /// <summary>
        /// 返回客户端信息到测试界面
        /// </summary>
        /// <param name="mess"></param>
        /// <param name="function"></param>
        public void SenClientMessageToWinForm(string mess, string function)
        {
            //触发事件，将消息发送给订阅者
            SentClientMess?.Invoke(this, new ReturnClientMess { Mess = mess, Function = function });
        }

        /// <summary>
        /// 连接服务器
        /// </summary>
        /// <param name="IP">扫码枪IP</param>
        /// <param name="Port">扫码枪端口</param>
        /// <param name="state">状态</param>
        public bool Connect()
        {
            bool flag = false;
            try
            {
                if (tcpClient == null)
                {
                    tcpClient = new TcpClient();
                    tcpClient.Connect(IPAddress.Parse(this.Ip), int.Parse(this.Port));
                    Console.WriteLine("与服务端连接成功！");

                    // 启动心跳发送线程
                    ListenTokenSoure = new CancellationTokenSource();
                    CancellationToken token = new CancellationToken();//取消线程通知
                    m = new ManualResetEvent(true);//true就是一开始不阻塞，就是正常使用
                    token = ListenTokenSoure.Token;
                    //发送心跳包线程
                    Task.Run(() => SendHeartbeat(token));
                    //接收信息线程
                    Task.Run(() => ReceiveMessages(token));
                    flag = true;
                    isConnect = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return flag;
        }

        private void SendHeartbeat(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                m.WaitOne();
                try
                {
                    if (tcpClient.Connected)
                    {
                        // 编码心跳消息
                        string heartbeatMessage = "heartbeat";
                        SendMess(heartbeatMessage);
                    }
                    else
                    {
                        // 客户端已断开连接
                        Console.WriteLine("与服务端的连接已断开。");
                        break;
                    }

                    // 暂停指定的时间间隔（60秒）
                    Thread.Sleep(heartbeatInterval);
                }
                catch (Exception ex)
                {
                    // 发生异常，通常表示客户端断开连接
                    Console.WriteLine("发送心跳数据给服务端发生异常: {0}", ex.Message);
                    break;
                }
            }
        }

        private void ReceiveMessages(CancellationToken token)
        {
            try
            {
                var networkStream = tcpClient.GetStream();
                byte[] buffer = new byte[1024]; // 用于接收消息的缓冲区
                int bytesRead;

                while (!token.IsCancellationRequested)
                {
                    //适当休眠，避免占用过多CPU资源
                    Thread.Sleep(300);
                    if (networkStream.DataAvailable)
                    {
                        bytesRead = networkStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            string receivedMessage = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                            // 触发接收到消息的事件
                            SenMessageToWinForm(receivedMessage);
                        }
                    }
                    //else
                    //{
                    //    // 如果没有数据可用，可以适当休眠，避免占用过多CPU资源
                    //    Thread.Sleep(300);
                    //}
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("接收消息时发生异常: {0}", ex.Message);
            }
        }

        public bool IsConnect()
        {
            if (isConnect)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// 发送信息
        /// </summary>
        public void SendMess(string mess)
        {
            try
            {
                if (mess.Equals("heartbeat"))
                {
                    byte[] Mess = Encoding.ASCII.GetBytes(mess);
                    // 发送消息给服务端
                    tcpClient.GetStream().Write(Mess, 0, Mess.Length);
                }
                else
                {
                    PauseHeart();
                    byte[] Mess = Encoding.ASCII.GetBytes(mess);
                    // 发送消息给服务端
                    tcpClient.GetStream().Write(Mess, 0, Mess.Length);
                    ContinueHeart();
                }

            }
            catch (Exception ex)
            {

            }
        }
        /// <summary>
        /// 停止心跳包线程
        /// </summary>
        public void StopHeart()
        {
            ListenTokenSoure.Cancel();//取消监听线程
        }
        /// <summary>
        /// 暂停心跳包
        /// </summary>
        public void PauseHeart()
        {
            m.Reset();
        }
        /// <summary>
        /// 继续发送心跳包
        /// </summary>
        public void ContinueHeart()
        {
            m.Set();
        }


        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                isConnect = false;
                StopHeart();
                tcpClient.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
