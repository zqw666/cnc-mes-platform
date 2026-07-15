using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataCommonOperate
{
    /// <summary>
    /// 数据接口
    /// </summary>
    public interface CommonInterface
    {
     
        /// <summary>
        /// 初始化
        /// </summary>
        void Initial();

        /// <summary>
        /// 获取单个值
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        object GetValue(string str);

        /// <summary>
        /// 执行单条sql语句
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        bool ExecuteSql(string str);

        /// <summary>
        /// 执行事务
        /// </summary>
        /// <param name="strsql"></param>
        /// <returns></returns>
       int ExecuteTrans(List<string> strsql);

        /// <summary>
        /// 执行单条sql语句
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        int executeSql(string str);

        /// <summary>
        /// 返回一个数据集合
        /// </summary>
        /// <param name="querystring">要执行的搜索语句</param>
        /// <returns></returns>
        DataSet ExeForDst(string querystring);

        /// <summary>
        /// 返回一个数据集合
        /// </summary>
        /// <param name="querystring">sql语句</param>
        /// <param name="strtable">给dataset的datatable赋予名称</param>
        /// <returns></returns>
        DataSet ExeForDst(string querystring, string strtable);

        /// <summary>
        /// 执行sql语句，返回一个datatable
        /// </summary>
        /// <param name="querystring">sql语句</param>
        /// <param name="tablename">datatable的名称</param>
        /// <returns></returns>
        DataTable ExeForDal(string querystring, string tablename);

        /// <summary>
        /// 执行sql语句，返回一个datatable
        /// </summary>
        /// <param name="querystring"></param>
        /// <returns></returns>
        DataTable ExeForDtl(string querystring);

        /// <summary>
        /// 打开
        /// </summary>
        void open();

        /// <summary>
        /// 关闭
        /// </summary>
        void close();

        /// <summary>
        /// 执行SQL语句，返回DataSet集合
        /// </summary>
        /// <param name="querystring"></param>
        /// <param name="pds"></param>
        /// <param name="ptablename"></param>
        /// <returns></returns>
        DataSet ExeForFillDst(string querystring, DataSet pds, string ptablename);

    }
}
