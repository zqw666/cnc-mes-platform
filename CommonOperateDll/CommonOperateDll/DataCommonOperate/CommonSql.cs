using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace DataCommonOperate
{
    /// <summary>
    /// sql数据库操作
    /// </summary>
    public class CommonSql : CommonInterface
    {
        private SqlConnection myconn;
        private SqlCommand mycmd;
        private SqlTransaction mytran;
        private string myconstr;

        /// <summary>
        /// mysql类的构造函数
        /// </summary>
        /// <param name="constr">数据库连接字符串</param>
        public CommonSql(string constr)
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
            this.myconn = new SqlConnection(myconstr);
            this.mycmd = new SqlCommand();
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
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                mycmd.CommandType = System.Data.CommandType.Text;
                mycmd.CommandText = str;
                return mycmd.ExecuteScalar();
            }
            catch (SqlException ex)
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
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                mycmd.CommandType = System.Data.CommandType.Text;
                mycmd.CommandText = str;
                mycmd.ExecuteNonQuery();
                mycmd.Dispose();
                bool Flag = true;
                return Flag;
            }
            catch (SqlException ex)
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
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                mycmd.CommandType = System.Data.CommandType.Text;
                mycmd.CommandText = str;
                int hangshu = mycmd.ExecuteNonQuery();
                mycmd.Dispose();
                return hangshu;
            }
            catch (SqlException ex)
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
            try
            {
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                DataSet ds = new DataSet();
                SqlDataAdapter ad = new SqlDataAdapter();
                mycmd.CommandType = CommandType.Text;
                mycmd.CommandText = querystring;
                ad.SelectCommand = mycmd;
                ad.Fill(ds);
                return ds;
            }
            catch (SqlException ex)
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

            try
            {
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                DataSet ds = new DataSet();
                SqlDataAdapter ad = new SqlDataAdapter();
                mycmd.CommandType = CommandType.Text;
                mycmd.CommandText = querystring;
                ad.SelectCommand = mycmd;
                ad.Fill(ds, strtable);
                return ds;
            }
            catch (SqlException ex)
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
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
                mycmd.Connection = myconn;
                SqlDataAdapter ad = new SqlDataAdapter();
                mycmd.CommandText = QueryString;
                ad.SelectCommand = mycmd;
                ad.Fill(pds, pTableName);
                return pds;
            }
            catch (SqlException ex)
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
            catch (SqlException ex)
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
            catch (SqlException ex)
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
            int execute = -1;
            try
            {
                if(myconn.State.ToString().ToUpper()!="OPEN")
                    myconn.Open();
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
                execute = 0;
                return execute;
            }
            catch (SqlException ex)
            {
                mytran.Rollback();
                throw ex;
            }
            finally
            {
                myconn.Close();
            }
        }

        /// <summary>
        /// 打开数据库
        /// </summary>
        public void open()
        {
            if (myconn.State.ToString().ToUpper() != "OPEN")
                this.myconn.Open();
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
