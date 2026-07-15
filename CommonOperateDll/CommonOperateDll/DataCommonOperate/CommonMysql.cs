using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;

namespace DataCommonOperate
{
    /// <summary>
    /// mysql数据库操作
    /// </summary>
    public class CommonMysql : CommonInterface
    {
        private MySqlConnection myconn;
        private MySqlCommand mycmd;
        private MySqlTransaction mytran;
        private string myconstr;

        /// <summary>
        /// mysql类的构造函数
        /// </summary>
        /// <param name="constr">数据库连接字符串</param>
        public CommonMysql(string constr)
        {
            myconstr = constr;
            Initial();
        }

        /// <summary>
        ///初始化数据库 
        /// </summary>
        public void Initial()
        {
            if (myconstr == null)
            {
                throw (new Exception("没有配置连接字符串！"));
            }
            this.myconn = new MySqlConnection(myconstr);
            this.mycmd = new MySqlCommand();
        }

        /// <summary>
        /// 获取单个值
        /// </summary>
        /// <param name="str">sql搜索语句</param>
        /// <returns></returns>
        public object GetValue(string str)
        {
            try
            {
                if (myconn.State.ToString().ToUpper() != "OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                mycmd.CommandType = System.Data.CommandType.Text;
                mycmd.CommandText = str;
                return mycmd.ExecuteScalar();
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
            finally
            {
                myconn.Close();
            }
        }

        /// <summary>
        /// 执行sql语句
        /// </summary>
        /// <param name="str">要执行的sql语句</param>
        public bool ExecuteSql(string str)
        {
            try
            {
                if (str != null && str != "")
                {
                    if (myconn.State.ToString().ToUpper() != "OPEN")
                        myconn.Open();
                    mycmd.Connection = myconn;
                    mycmd.CommandType = System.Data.CommandType.Text;
                    mycmd.CommandText = str;
                    mycmd.ExecuteNonQuery();
                    mycmd.Dispose();
                    return true;
                }
                else
                    return false;
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
            finally
            {
                myconn.Close();
            }
        }

        /// <summary>
        /// 执行SQL语句，并返回受影响的行数
        /// </summary>
        /// <param name="str">要执行的sql语句</param>
        /// <returns></returns>
        public int executeSql(string str)
        {
            try
            {
                if (myconn.State.ToString().ToUpper() != "OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                mycmd.CommandType = System.Data.CommandType.Text;
                mycmd.CommandText = str;
                int hangshu = mycmd.ExecuteNonQuery();
                mycmd.Dispose();
                return hangshu;
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
            finally
            {
                myconn.Close();
            }
        }

        /// <summary>
        /// 返回数据集
        /// </summary>
        /// <param name="querystring">要执行的搜索语句</param>
        /// <returns></returns>
        public DataSet ExeForDst(string querystring)
        {
            DataSet ds = new DataSet();
            MySqlDataAdapter ad = new MySqlDataAdapter();
            try
            {
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                mycmd.CommandType = CommandType.Text;
                mycmd.CommandText = querystring;
                ad.SelectCommand = mycmd;
                ad.Fill(ds);
                return ds;
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
            finally
            {
                myconn.Close();
            }
        }

        /// <summary>
        /// 返回数据集
        /// </summary>
        /// <param name="querystring">要执行的搜索语句</param>
        /// <param name="strtable">给数据集中的表指定名称</param>
        /// <returns></returns>
        public DataSet ExeForDst(string querystring, string strtable)
        {
            DataSet ds = new DataSet();
            MySqlDataAdapter ad = new MySqlDataAdapter();
            try
            {
                if (myconn.State.ToString().ToUpper() != "OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                mycmd.CommandType = CommandType.Text;
                mycmd.CommandText = querystring;
                ad.SelectCommand = mycmd;
                ad.Fill(ds, strtable);
                return ds;
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
            finally
            {
                myconn.Close();
            }
        }

        /// <summary>
        /// 返回数据集
        /// </summary>
        /// <param name="QueryString">要执行的搜索语句</param>
        /// <param name="pds">要填充的数据集名称</param>
        /// <param name="pTableName">给数据集中的表指定名称</param>
        /// <returns></returns>
        public DataSet ExeForFillDst(String QueryString, DataSet pds, String pTableName)
        {
            try
            {
                if (myconn.State.ToString().ToUpper() != "OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                MySqlDataAdapter ad = new MySqlDataAdapter();
                mycmd.CommandText = QueryString;
                ad.SelectCommand = mycmd;
                ad.Fill(pds, pTableName);
                return pds;
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
            finally
            {
                myconn.Close();
            }
        }

        /// <summary>
        /// 执行sql语句，返回默认DataTable
        /// </summary>
        /// <param name="querystring">sql语句</param>
        /// <returns></returns>
        public DataTable ExeForDtl(string querystring)
        {
            DataSet ds;
            try
            {
                ds = ExeForDst(querystring);
                DataTable dt = ds.Tables[0];
                ds = null;
                return dt;
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 返回DataTable
        /// </summary>
        /// <param name="querystring">要执行的搜索语句</param>
        /// <param name="tablename">指定填充datatable的数据集中的数据表名称</param>
        /// <returns></returns>
        public DataTable ExeForDal(string querystring, string tablename)
        {
            DataSet ds;
            try
            {
                ds = ExeForDst(querystring, tablename);
                DataTable dt = ds.Tables[tablename];
                ds = null;
                return dt;
            }
            catch (MySqlException ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 执行多条sql语句
        /// </summary>
        /// <param name="strSQLs">SQL语句列表</param>
        /// <returns></returns>
        public int ExecuteTrans(List<string> strSQLs)
        {
            // 处理空列表情况
            if (strSQLs == null || strSQLs.Count == 0)
            {
                // 可以选择返回0（空列表执行成功）或-1（无效输入）
                // 这里返回0，表示没有SQL语句执行，但事务没有问题
                return 0;
            }

            try
            {
                // 确保连接和命令对象已初始化
                if (myconn == null || mycmd == null)
                {
                    throw new InvalidOperationException("数据库连接或命令对象未初始化");
                }

                mycmd.Connection = myconn;
                mytran = myconn.BeginTransaction();
                mycmd.Transaction = mytran;
                mycmd.CommandType = CommandType.Text;

                foreach (string str in strSQLs)
                {
                    mycmd.CommandText = str;
                    mycmd.ExecuteNonQuery();
                }

                mytran.Commit();
                return 0; // 0 表示成功
            }
            catch (Exception ex)
            {
                // 确保事务回滚
                if (mytran != null)
                {
                    mytran.Rollback();
                }

                // 记录日志（建议添加）
                // Logger.Error("执行SQL事务失败", ex);

                return -1; // -1 表示失败
            }
        }

        /// <summary>
        /// 打开数据库
        /// </summary>
        public void open()
        {
            if (myconn.State.ToString().ToUpper() != "OPEN")
            {
                this.myconn.Open();
            }
        }

        /// <summary>
        /// 关闭数据库
        /// </summary>
        public void close()
        {
            if (myconn.State.ToString().ToUpper() == "OPEN")
            {
                this.myconn.Close();
            }
        }
    }
}
