using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataCommonOperate
{
    /// <summary>
    /// 枚举数据库类型
    /// </summary>
    public enum CommonData
    {
        /// <summary>
        /// sql
        /// </summary>
        sql = 1,
        /// <summary>
        /// mysql
        /// </summary>
        mysql =2
    }
    /// <summary>
    /// 数据工厂
    /// </summary>
    public class CommonFactory
    {

        private static CommonFactory instance = null;
        
        /// <summary>
        /// 创建工厂实例
        /// </summary>
        /// <returns></returns>
        public static CommonFactory CreateInstance()
        {
            if (instance == null)
            {
                instance = new CommonFactory();
            }
            return instance;
        }
        /// <summary>
        /// 创建一个数据访问接口
        /// </summary>
        /// <param name="CommonData_Parameter"></param>
        /// <param name="connstr"></param>
        /// <returns></returns>
        public static CommonInterface CreateInstance(CommonData CommonData_Parameter, String connstr)
        {
            switch ((int)CommonData_Parameter)
            {
                case 1:
                    return new CommonSql(connstr);
                case 2:
                    return new CommonMysql(connstr);
                default:
                    return new CommonSql(connstr);
            }
        }
    }
}
