using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RodersErpTest
{
    public class MachineClass
    {
        /// <summary>
        /// ERP路径
        /// </summary>
        public string ErpPath { get; set; }

        /// <summary>
        /// OEE文件路径
        /// </summary>
        public string OeePath { get; set; }

        /// <summary>
        /// IP
        /// </summary>
        public string IP { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public string State { get; set; } = "";

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="ErpPath"></param>
        /// <param name="OeePath"></param>
        /// <param name="IP"></param>
        public MachineClass(string ErpPath, string OeePath, string IP)
        {
            this.ErpPath = ErpPath;
            this.OeePath = OeePath;
            this.IP = IP;
        }
    }
}
