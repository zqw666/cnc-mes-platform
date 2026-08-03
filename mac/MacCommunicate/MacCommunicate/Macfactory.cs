using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MacCommunicate
{
    public class Macfactory
    {
        /// <summary>
        /// 枚举机床类型
        /// </summary>
        public enum MacType
        {
            /// <summary>
            /// 发那科
            /// </summary>
            fanuc = 1,
            /// <summary>
            /// 罗德斯
            /// </summary>
            roders = 2,
            /// <summary>
            /// 西门子
            /// </summary>
            siemense = 3,
            /// <summary>
            /// 牧野
            /// </summary>
            makino = 4
        }

        /// <summary>
        /// 机床通信工厂类
        /// </summary>
        public static class MacFactory
        {
            /// <summary>
            /// 根据机床类型和IP创建对应的通信接口实例
            /// </summary>
            /// <param name="typeparameter">机床类型枚举</param>
            /// <param name="macip">机床IP地址</param>
            /// <param name="sessionUrl">OPC UA 会话地址（仅西门子使用）</param>
            /// <param name="username">OPC UA 登录用户名（仅西门子使用）</param>
            /// <param name="password">OPC UA 登录密码（仅西门子使用）</param>
            /// <param name="winscpKey">WinSCP SSH 密钥（仅西门子使用）</param>
            /// <returns>机床通信接口实例，类型不匹配时返回null</returns>
            public static macinterface CreateInstance(MacType typeparameter, string macip,
                string sessionUrl = null, string username = null, string password = null, string winscpKey = null)
            {
                switch (typeparameter)
                {
                    case MacType.fanuc:
                        return new Fanuc(macip);
                    case MacType.roders:
                        return new Roderss(macip);
                    case MacType.siemense:
                        return new Siemence(macip, sessionUrl, username, password, winscpKey);
                    case MacType.makino:
                        return new Makino(macip);
                    default:
                        return null;
                }
            }
        }
    }
}
