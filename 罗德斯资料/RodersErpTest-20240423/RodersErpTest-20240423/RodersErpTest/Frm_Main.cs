using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RodersNameSpace;
using System.IO;
using System.Xml;
using System.Threading;
using System.Data.SqlClient;
using System.Net;

namespace RodersErpTest
{
    public partial class Frm_Main : Form
    {
        public Frm_Main()
        {
            InitializeComponent();
            CheckForIllegalCrossThreadCalls = false;
        }

        /// <summary>
        /// 当前工作路径
        /// </summary>
        public string LocalPath = Directory.GetCurrentDirectory().Trim('\\') + "\\";


        /// <summary>
        /// 写入日志到文件
        /// </summary>
        /// <param name="IP"></param>
        /// <param name="Msg"></param>
        public void Write_Msg(string IP,string Msg)
        {
            StreamWriter sw = new StreamWriter(LocalPath + "Config\\Message.txt",true);
            sw.WriteLine(string.Format("{0} {1}：{2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),IP,Msg));
            sw.Close();
            sw.Dispose();
        }

        /// <summary>
        /// 窗体加载事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Frm_Main_Load(object sender, EventArgs e)
        {
            string localname = Dns.GetHostName();//获取本机名
            IPAddress[] iPAddresses = Dns.GetHostAddresses(localname);//获取本地地址
            List<IPAddress> Values = new List<IPAddress> { };
            foreach (IPAddress address in iPAddresses)
            {
                if (Char.IsDigit(address.ToString()[0]))
                    Values.Add(address);
            }
            foreach (IPAddress iP in Values)
            {
                if (iP.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)//获取IPV4地址
                {
                    comboBox1.Items.Add(iP.ToString());
                }
            }
            if(comboBox1.Items.Count > 0)
                comboBox1.SelectedIndex = 0;
            try
            {
                StreamReader sr = new StreamReader(LocalPath + "Config\\Config.xml");
                string doc = sr.ReadToEnd();
                sr.Close();
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(doc);
                AutoServer = xmlDocument.GetElementsByTagName("autoserver")[0].InnerText.ToLower() == "true"?true:false;//读取创建服务器的地址
                XmlNodeList nodeList = xmlDocument.GetElementsByTagName("machine");
                foreach(XmlNode n in nodeList)
                {
                    string erppath = n.SelectSingleNode(".//machine_erppath").InnerText;
                    string oeepath = n.SelectSingleNode(".//machine_oeepath").InnerText;
                    string ip = n.SelectSingleNode(".//machine_ip").InnerText;
                    Machine.Add(new MachineClass(erppath, oeepath, ip));
                    comboBox3.Items.Add(ip);
                }
            }
            catch(Exception ex)
            {
                timer1.Enabled = false;
                MessageBox.Show("配置文件有误！\n将退出！", "发生错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
            timer1.Enabled = true;
            timer3.Enabled = true;
            Machine_ERP_Thread();//机床 ERP线程
            if (AutoServer)//自动创建服务器监听机床连接
            {
                Button1_Click(sender, e);////开启服务器
            }
        }

        #region 配置变量

        /// <summary>
        /// 线程循环变量
        /// </summary>
        private bool Thread_Flag = true;
        
        /// <summary>
        /// 自动创建服务器
        /// </summary>
        private bool AutoServer = false;

        /// <summary>
        /// 配置的机床
        /// </summary>
        List<MachineClass> Machine = new List<MachineClass> { };

        #endregion

        /// <summary>
        /// 窗体关闭事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Frm_Main_FormClosed(object sender, FormClosedEventArgs e)
        {
            Thread_Flag = false;
            if(server != null)
                server.Close();
            try
            {
                this.notifyIcon1.Dispose();
            }
            catch { }
        }

        /// <summary>
        /// 端口号限制输入
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TextBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar <= 32 || (e.KeyChar >= '0' && e.KeyChar <= '9'))
                return;
            else
                e.Handled = true;
        }

        /// <summary>
        /// 定时器1中断事件，用于获取配置文件的信息
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                StreamReader sr = new StreamReader(LocalPath + "Config\\Config.xml");
                string doc = sr.ReadToEnd();
                sr.Close();
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(doc);
                string vis = xmlDocument.GetElementsByTagName("visible")[0].InnerText;
                if(vis.ToLower() == "true")
                {
                    this.Visible = true;
                }
                else
                {
                    this.Visible = false;
                }
                XmlNodeList nodeList = xmlDocument.GetElementsByTagName("machine");
                List<string> ms = new List<string> { };
                foreach (XmlNode n in nodeList)
                {
                    string erppath = n.SelectSingleNode(".//machine_erppath").InnerText;
                    string oeepath = n.SelectSingleNode(".//machine_oeepath").InnerText;
                    string ip = n.SelectSingleNode(".//machine_ip").InnerText;
                    ms.Add(ip);
                    bool flag = true;
                    foreach (MachineClass m in Machine)
                    {
                        if(m.IP == ip)
                        {
                            if (m.ErpPath != erppath)
                                m.ErpPath = erppath;
                            if (m.OeePath != oeepath)
                                m.OeePath = oeepath;
                            flag = false;
                            break;
                        }
                    }
                    if (flag)
                    {
                        Machine.Add(new MachineClass(erppath, oeepath, ip));
                        comboBox3.Items.Add(ip);
                    }
                }
                foreach (MachineClass m in Machine)
                {
                    if (ms.IndexOf(m.IP) < 0)//配置文件无而创建了对象
                    {
                        Machine.Remove(m);
                        comboBox3.Items.Remove(m.IP);
                    }
                }
            }
            catch
            {
                Thread_Flag = false;
                timer1.Enabled = false;
                MessageBox.Show("配置文件有误！\n将退出！", "发生错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        /// <summary>
        /// 检测是否有指定内容更新
        /// </summary>
        /// <param name="sr"></param>
        /// <returns></returns>
        private bool Check_Report(StatuReport sr1,StatuReport sr2)
        {
            if (sr2 == null || sr1 == null)
                return true;
            if (sr2.Auto != sr1.Auto || sr2.AutoChange != sr1.AutoChange || sr2.Active != sr1.Active || sr2.ECO != sr1.ECO || sr2.FileTransfer != sr1.FileTransfer || sr2.NewPaldata != sr1.NewPaldata)
                return true;
            if (sr2.Override != sr1.Override || sr2.Handl_Interface != sr1.Handl_Interface || sr2.Handl_Mode != sr1.Handl_Mode || sr2.Handl_State != sr1.Handl_State || sr2.Errors != sr1.Errors || sr2.PalDoor != sr1.PalDoor || sr2.ToolDoor != sr1.ToolDoor)
                return true;
            return false;
        }
        
        /// <summary>
        /// 检查指定内容更新了才插入新数据
        /// </summary>
        /// <param name="old_erp"></param>
        /// <param name="new_erp"></param>
        /// <returns></returns>
        private bool Check_Change(Roders_Erp old_erp, Roders_Erp new_erp)
        {
            if (old_erp == null)
                return true;
            if (old_erp.Mode != new_erp.Mode || old_erp.State != new_erp.State || old_erp.DoorState != new_erp.DoorState || old_erp.AutoDoorState != new_erp.AutoDoorState)
                return true;
            if (old_erp.Ov_Feed != new_erp.Ov_Feed || old_erp.Tool != new_erp.Tool || old_erp.SpinSpeed != new_erp.SpinSpeed || old_erp.Ov_SpinSpeed != new_erp.Ov_SpinSpeed)
            //if (old_erp.Feed != new_erp.Feed || old_erp.Ov_Feed != new_erp.Ov_Feed || old_erp.Tool != new_erp.Tool || old_erp.SpinSpeed != new_erp.SpinSpeed || old_erp.Ov_SpinSpeed != new_erp.Ov_SpinSpeed)
                return true;
            if (old_erp.Job.Name != new_erp.Job.Name || old_erp.Job.NcPgm != new_erp.Job.NcPgm || old_erp.Job.Start != new_erp.Job.Start || old_erp.Job.SubPgm != new_erp.Job.SubPgm || old_erp.Job.SubStart != new_erp.Job.SubStart)
                return true;
            return false;
        }
        
        /// <summary>
        /// 机床获取ERP信息线程
        /// </summary>
        private void Machine_ERP_Thread()
        {
            Thread th2 = new Thread(()=> {
                Roders_Erp old_erp = null;
                DateTime StateStart = DateTime.Now;
                //如果表无该机床数据，插入
                foreach (MachineClass m in Machine)
                {
                    string cmd = "Server=10.19.27.11;User Id=sa;Pwd=qwe!23;database=DataSystem;Max Pool Size=512;";
                    SqlConnection con = new SqlConnection(cmd);
                    con.Open();
                    SqlCommand Command = new SqlCommand();
                    Command.Connection = con;
                    try
                    {
                        string UpdateCmd = string.Format("INSERT INTO Roeders_Erp(ip)VALUES('{0}')", m.IP);
                        Command.CommandText = UpdateCmd;
                        int num = Command.ExecuteNonQuery();//获取受影响的行数
                    }catch(Exception ec){ }
                    try
                    {
                        string UpdateCmd = string.Format("INSERT INTO Roeders_Msg(ip)VALUES('{0}')", m.IP);
                        Command.CommandText = UpdateCmd;
                        int num = Command.ExecuteNonQuery();//获取受影响的行数
                    }
                    catch { }
                    try
                    {
                        string UpdateCmd = string.Format("INSERT INTO Roeders_Stu(ip)VALUES('{0}')", m.IP);
                        Command.CommandText = UpdateCmd;
                        int num = Command.ExecuteNonQuery();//获取受影响的行数
                    }
                    catch { }
                }
                while (Thread_Flag)
                {
                    try
                    {
                        foreach (MachineClass m in Machine)
                        {
                            if (File.Exists(m.ErpPath))
                            {
                                Thread.Sleep(100);//等待文件写入完毕
                                File.Delete(LocalPath + "Config\\Machine_ERP.xml");
                                File.Move(m.ErpPath, LocalPath + "Config\\Machine_ERP.xml");//剪切文件
                                Roders_Erp erp = Roders_Erp.Erp_Load(LocalPath + "Config\\Machine_ERP.xml");//加载数据
                                string cmd = "Server=10.19.27.11;User Id=sa;Pwd=qwe!23;database=DataSystem;Max Pool Size=512;";
                                SqlConnection con = new SqlConnection(cmd);
                                //try
                                //{
                                //    con.Open();
                                //    SqlCommand Command = new SqlCommand();
                                //    Command.Connection = con;
                                //    string UpdateCmd = string.Format("UPDATE Roeders_Erp SET version='{0}',updatetime='{1}',machinename='{2}',ver='{3}',mode='{4}',state='{5}',doorstate='{6}',autodoorstate='{7}',feed='{8}',ov_feed='{9}',tool='{10}',tooltype='{11}',spinspeed='{12}',ov_spinspeed='{13}',job_name='{14}',job_ncpgm='{15}',job_start='{16}',job_subpgm='{17}',job_substart='{18}',job_lastjobend='{19}',job_percent='{20}',job_line='{21}' WHERE ip='{22}'",
                                //        erp.Version, erp.UpdateTime.ToString("yyyy/MM/dd HH:mm:ss"), erp.MachineName, erp.Ver, erp.Mode, erp.State, erp.DoorState, erp.AutoDoorState, erp.Feed, erp.Ov_Feed, erp.Tool, erp.ToolType, erp.SpinSpeed, erp.Ov_SpinSpeed, erp.Job.Name, erp.Job.NcPgm, erp.Job.Start, erp.Job.SubPgm, erp.Job.SubStart, erp.Job.LastJobEnd, erp.Job.Percent, erp.Job.Line, m.IP);
                                //    Command.CommandText = UpdateCmd;
                                //    Command.ExecuteNonQuery();//获取受影响的行数
                                //    if (Check_Change(old_erp, erp))
                                //    {
                                //        string InsertCmd = string.Format("INSERT INTO Roeders_ErpHistory(version,updatetime,machinename,ver,mode,state,doorstate,autodoorstate,feed,ov_feed,tool,tooltype,spinspeed,ov_spinspeed,job_name,job_ncpgm,job_start,job_subpgm,job_substart,job_lastjobend,job_percent,job_line,ip)VALUES('{0}','{1}','{2}','{3}','{4}','{5}','{6}','{7}',{8},{9},{10},'{11}',{12},{13},'{14}','{15}','{16}','{17}','{18}','{19}',{20},{21},'{22}')",
                                //            erp.Version, erp.UpdateTime.ToString("yyyy/MM/dd HH:mm:ss"), erp.MachineName, erp.Ver, erp.Mode, erp.State, erp.DoorState, erp.AutoDoorState, erp.Feed, erp.Ov_Feed, erp.Tool, erp.ToolType, erp.SpinSpeed, erp.Ov_SpinSpeed, erp.Job.Name, erp.Job.NcPgm, erp.Job.Start, erp.Job.SubPgm, erp.Job.SubStart, erp.Job.LastJobEnd, erp.Job.Percent, erp.Job.Line, m.IP);
                                //        Command.CommandText = InsertCmd;
                                //        Command.ExecuteNonQuery();//获取受影响的行数
                                //    }
                                //    old_erp = erp;
                                //    //不为空，即记录了上一次的状态
                                //    if (m.State != "")
                                //    {
                                //        if (erp.State != m.State)//状态变化
                                //        {
                                //            DateTime now = DateTime.Now;
                                //            Command.CommandText = string.Format("UPDATE Roeders_State SET endTime='{0}',allTime={1} WHERE ip='{2}' AND startTime='{3}' AND state='{4}'", now.ToString("yyyy/MM/dd HH:mm:ss"), (now - StateStart).TotalMinutes, m.IP, StateStart.ToString("yyyy/MM/dd HH:mm:ss"), m.State);
                                //            Command.ExecuteNonQuery();//获取受影响的行数
                                //            Command.CommandText = string.Format("INSERT INTO Roeders_State(ip,state,startTime,allTime)VALUES('{0}','{1}','{2}',0.0)", m.IP, erp.State, now.ToString("yyyy/MM/dd HH:mm:ss"));
                                //            Command.ExecuteNonQuery();//获取受影响的行数
                                //            StateStart = now;
                                //            m.State = erp.State;
                                //        }
                                //    }
                                //    //为空，即第一次启动
                                //    else
                                //    {
                                //        DateTime now = DateTime.Now;
                                //        StateStart = now;
                                //        m.State = erp.State;
                                //        Command.CommandText = string.Format("SELECT * FROM Roeders_State WHERE  ip='{0}' AND endTime IS NULL", m.IP);
                                //        SqlDataReader sqlDataReader = Command.ExecuteReader();
                                //        if (sqlDataReader.Read())//有数据，更新表
                                //        {
                                //            DateTime start = (DateTime)sqlDataReader["startTime"];//获取开始记录时间
                                //            sqlDataReader.Close();//关闭对象，否则无法执行命令
                                //            Command.CommandText = string.Format("UPDATE Roeders_State SET endTime='{0}',allTime={1},canUse=0 WHERE ip='{2}' AND endTime IS NULL", now.ToString("yyyy/MM/dd HH:mm:ss"), (now - start).TotalMinutes, m.IP);
                                //            Command.ExecuteNonQuery();//更新上一条记录
                                //        }
                                //        sqlDataReader.Close();//关闭对象，否则无法执行命令
                                //        Command.CommandText = string.Format("INSERT INTO Roeders_State(ip,state,startTime,allTime)VALUES('{0}','{1}','{2}',0.0)", m.IP, erp.State, now.ToString("yyyy/MM/dd HH:mm:ss"));
                                //        Command.ExecuteNonQuery();//插入新数据
                                //    }
                                //}
                                //catch (Exception es) { }
                                //finally
                                //{
                                //    con.Close();
                                //}
                                if (checkBox1.Checked && comboBox3.Text == m.IP)
                                {
                                    version1.Text = erp.Version;
                                    updatetime1.Text = erp.UpdateTime.ToString("yyyy/MM/dd HH:mm:ss");
                                    machinename1.Text = erp.MachineName;
                                    ver1.Text = erp.Ver;
                                    mode1.Text = erp.Mode;
                                    state1.Text = erp.State;
                                    doorstate1.Text = erp.DoorState;
                                    autodoorstate1.Text = erp.AutoDoorState;
                                    feed1.Text = erp.Feed.ToString();
                                    ov_feed1.Text = erp.Ov_Feed.ToString();
                                    tool1.Text = erp.Tool.ToString();
                                    tooltype1.Text = erp.ToolType;
                                    spinspeed1.Text = erp.SpinSpeed.ToString();
                                    ov_spinspeed1.Text = erp.Ov_SpinSpeed.ToString();
                                    job_name1.Text = erp.Job.Name;
                                    job_ncpgm1.Text = erp.Job.NcPgm;
                                    job_start1.Text = erp.Job.Start.ToString();
                                    job_subpgm1.Text = erp.Job.SubPgm;
                                    job_substart1.Text = erp.Job.SubStart.ToString();
                                    job_lastjobend1.Text = erp.Job.LastJobEnd.ToString();
                                    job_percent1.Text = erp.Job.Percent.ToString();
                                    job_line1.Text = erp.Job.Line.ToString();
                                }
                            }
                        }
                    }
                    catch { }
                }
            });
            th2.Start();//开始线程
        }

        Roders_Server server = null;

        /// <summary>
        /// 创建服务器按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Button1_Click(object sender, EventArgs e)
        {
            if (button1.Text == "创建服务器")
            {
                server = new Roders_Server("192.168.125.201", Convert.ToInt32(textBox1.Text));
                server.MachineConnected += new Roders_Server.Machine_Connected(this.Machine_Connect);
                server.Listen_Machine_Thread(3450);
                comboBox1.Enabled = false;
                textBox1.Enabled = false;
                button1.Text = "关闭服务器";
                timer2.Enabled = true;
            }
            else
            {
                server.MachineConnected -= new Roders_Server.Machine_Connected(this.Machine_Connect);
                server.Close_Server();
                comboBox1.Enabled = true;
                textBox1.Enabled = true;
                button1.Text = "创建服务器";
                timer2.Enabled = false;
            }
        }
        /// <summary>
        /// 机床连接触发事件
        /// </summary>
        /// <param name="Machine"></param>
        public void Machine_Connect(Roders Machine)
        {
            string ip = Machine.MachineEndPoint.Address.ToString();
            if (comboBox2.Items.IndexOf(ip) > 0)
            {
                comboBox2.Items.Remove(ip);
            }
            comboBox2.Items.Add(ip);
            richTextBox2.SelectionColor = Color.Green;
            richTextBox2.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   " + Machine.MachineName + "\n");
            richTextBox2.SelectionColor = Color.Green;
            richTextBox2.AppendText(Machine.MachineType + "：" + ip + "连接成功\n");
            Machine.ReportRecieved += new Roders._ReportRecieved(ReportRec);
            Machine.MachineDisConnect += new Roders._MachineDisConnect(Machine_DisConnect);
            Machine.Error_Event += new Roders._ErrorEvent(ErrorEvent);
            Machine.ResultEvent += new Roders._ResultEvent(result_event);//加工完毕事件
            Write_Msg(ip, "连接成功");
            string cmd = "Server=10.19.27.11;User Id=sa;Pwd=qwe!23;database=DataSystem;Max Pool Size=512;";
            SqlConnection con = new SqlConnection(cmd);
            try
            {
                con.Open();
                SqlCommand Command = new SqlCommand();
                Command.Connection = con;
                string UpdateCmd = string.Format("UPDATE Roeders_Msg SET machineType='{0}',version='{1}',machineName='{2}' WHERE ip='{3}'", Machine.MachineType, Machine.Version, Machine.MachineName, ip);
                Command.CommandText = UpdateCmd;
                Command.ExecuteNonQuery();//获取受影响的行数
            }
            finally
            {
                con.Close();
            }
        }

        /// <summary>
        /// 统计加工完毕件数
        /// </summary>
        /// <param name="Machine"></param>
        /// <param name="Result"></param>
        public void result_event(Roders Machine, Roders_Result Result)
        {
            string cmd = "Server=10.19.27.11;User Id=sa;Pwd=qwe!23;database=DataSystem;Max Pool Size=512;";
            SqlConnection con = new SqlConnection(cmd);
            DateTime Now = DateTime.Now;
            try
            {
                con.Open();
                SqlCommand Command = new SqlCommand();
                Command.Connection = con;
                Command.CommandText = string.Format("SELECT * FROM Roeders_OeeNow WHERE ip = '{0}'", Machine.MachineEndPoint.Address.ToString());
                SqlDataReader sqlDataReader = Command.ExecuteReader();
                if (sqlDataReader.Read())//有数据，更新表
                {
                    int output = (int)sqlDataReader["MachineOutput"] + 1;
                    Command.CommandText = string.Format("UPDATE Roeders_OeeNow SET MachineOutput={0} WHERE ip='{1}'", output, Machine.MachineEndPoint.Address.ToString());
                    sqlDataReader.Close();
                    Command.ExecuteNonQuery();//获取受影响的行数
                }
                /*
                else//无数据，插入
                {
                    Command.CommandText = string.Format("INSERT INTO Roeders_OeeNow(ip,startTime,endTime,startProTime,endProTime,oee_Now,oee_Day,oee_Val)VALUES('{0}','{1}','{1}',{2:f3},{2:f3},{3:f3},{3:f3},24)", Machine1_IP, Now.ToString("yyyy/MM/dd HH:mm:ss"), oee.ProTime, 0.0);
                    sqlDataReader.Close();
                    Command.ExecuteNonQuery();//获取受影响的行数
                }
                */
            }
            catch (Exception ex)
            { }
            finally
            {
                con.Close();
            }
        }

        /// <summary>
        /// 状态端口事件
        /// </summary>
        /// <param name="str"></param>
        /// <param name="re"></param>
        public void ReportRec(string str, StatuReport sr, Roders Machine)
        {
            try
            {
                SqlConnection con = new SqlConnection("Server=10.19.27.11;User Id=sa;Pwd=qwe!23;database=DataSystem;Max Pool Size=512;");
                con.Open();
                SqlCommand Command = new SqlCommand();
                Command.Connection = con;
                string cmd = "UPDATE Roeders_Stu SET ver='{0}',id='{1}',auto='{2}',autochange='{3}',active='{4}',eco='{5}',filetransfer='{6}',newmaintenance='{7}',newpaldata='{8}'," +
                    "override='{9}',handl_interface='{10}',handl_mode='{11}',handl_state='{12}',errors='{13}',paldoor='{14}',tooldoor='{15}',updateTime='{16}' WHERE IP = '{17}'";
                cmd = string.Format(cmd, sr.Header.Ver, sr.Header.ID, sr.Auto, sr.AutoChange, sr.Active, sr.ECO, sr.FileTransfer, sr.NewMaintenance, sr.NewPaldata,
                    sr.Override, sr.Handl_Interface, sr.Handl_Mode, sr.Handl_State, sr.Errors, sr.PalDoor, sr.ToolDoor, sr.UpdateTime, Machine.MachineEndPoint.Address.ToString());
                Command.CommandText = cmd;
                Command.ExecuteNonQuery();//获取受影响的行数
                con.Close();
                con.Dispose();
            }
            catch (Exception ex)
            {

            }
            //开启实时状态端口，且选择的是当前的机床
            if (checkBox4.Checked && Machine.MachineEndPoint.Address.ToString() == comboBox2.Text)
            {
                richTextBox3.Text = "";
                richTextBox3.SelectionColor = Color.Red;
                richTextBox3.AppendText("接收时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
                richTextBox3.AppendText(str);
            }
        }

        /// <summary>
        /// 机床断开连接触发事件
        /// </summary>
        /// <param name="Machine"></param>
        public void Machine_DisConnect(Roders Machine)
        {
            try
            {
                string ip = Machine.MachineEndPoint.Address.ToString();
                comboBox2.Items.Remove(ip);
                server.RemoveMachine(Machine);
                richTextBox2.SelectionColor = Color.Red;
                richTextBox2.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   " + Machine.MachineName + "\n");
                richTextBox2.SelectionColor = Color.Red;
                richTextBox2.AppendText(Machine.MachineType + "：" + ip + "已断开连接\n");
                Write_Msg(ip, "已断开连接");
            }
            catch { }
        }

        /// <summary>
        /// 机床错误触发事件
        /// </summary>
        /// <param name="Machine"></param>
        /// <param name="ErrMsg"></param>
        public void ErrorEvent(Roders Machine, Roders_ErrMsg[] ErrMsg)
        {
            richTextBox2.SelectionColor = Color.Red;
            richTextBox2.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   " + Machine.MachineName + "\n");
            foreach (Roders_ErrMsg err in ErrMsg)
            {
                richTextBox2.SelectionColor = Color.Red;
                richTextBox2.AppendText("发生错误：" + err.Code + "，" + err.Message + "\n");
                Write_Msg(Machine.MachineEndPoint.Address.ToString(), "发生错误：" + err.Code + "，" + err.Message);
            }
        }

        /// <summary>
        /// 刷新已连接的机床
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Button2_Click(object sender, EventArgs e)
        {
            comboBox2.Items.Clear();
            if (server == null)
                return;
            foreach(Roders r in server.Machines)
            {
                string ip = r.MachineEndPoint.Address.ToString();
                if (!r.CmdAlive || !r.StateAlive)
                {
                    richTextBox2.SelectionColor = Color.Red;
                    richTextBox2.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   " + r.MachineName + "\n");
                    richTextBox2.SelectionColor = Color.Red;
                    richTextBox2.AppendText(r.MachineType + "：" + ip + "已断开连接\n");
                    comboBox2.Items.Remove(ip);
                    server.RemoveMachine(r);
                    Write_Msg(ip, "已断开连接");
                    continue;
                }
                comboBox2.Items.Add(ip);
            }
        }
        
        /// <summary>
        /// 发送命令
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Button3_Click(object sender, EventArgs e)
        {
            richTextBox1.Text = "";
            if (comboBox2.SelectedIndex < 0)
            {
                richTextBox1.Text = "请选择机床";
                comboBox2.Focus();
                return;
            }
            if (cbbox_Cmd.SelectedIndex < 0)
            {
                richTextBox1.Text = "请选择命令";
                cbbox_Cmd.Focus();
                return;
            }
            try
            {
                switch(cbbox_Cmd.SelectedIndex)
                {
                    case 0:server.Machines[comboBox2.SelectedIndex].Send_WDREQ();richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    case 1: server.Machines[comboBox2.SelectedIndex].Send_PTREQ(); richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;

                    case 14: server.Machines[comboBox2.SelectedIndex].Send_PDOPEN(); richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    case 15: server.Machines[comboBox2.SelectedIndex].Send_PDCLOSE(); richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    case 16: server.Machines[comboBox2.SelectedIndex].Send_TDOPEN(); richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    /*
                    case 17: server.Machines[comboBox2.SelectedIndex].Send_TDCLOSE();richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    case 18: server.Machines[comboBox2.SelectedIndex].Send_TTLA();richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    case 19: server.Machines[comboBox2.SelectedIndex].Send_CHANGE();richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    case 20: server.Machines[comboBox2.SelectedIndex].Send_LOCK();richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                    */
                    case 21: server.Machines[comboBox2.SelectedIndex].Send_ABORT(); richTextBox1.Text = server.Machines[comboBox2.SelectedIndex].LastAcknowledges; break;
                }
            }
            catch(Exception ex)
            {
                richTextBox1.Text = ex.Message;
            }
        }
             
        /// <summary>
        /// 定时器中断事件，定时获取稼动率文件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer3_Tick(object sender, EventArgs e)
        {
            timer3.Enabled = false;
            foreach (MachineClass m in Machine)
            {
                try
                {
                    File.Copy(m.OeePath, LocalPath + "Config\\Machine.bde", true);
                    File.SetAttributes(LocalPath + "Config\\Machine.bde", FileAttributes.Normal);
                    Roders_Oee oee = new Roders_Oee(LocalPath + "Config\\Machine.bde");
                    File.Delete(LocalPath + "Config\\Machine.bde");
                    string updatecmd = "";
                    foreach (string k in oee.Keys)
                    {
                        try
                        {
                            if (k != "ProTime")
                            {
                                if (oee.Values[k].GetType() == Type.GetType("System.Double"))
                                    updatecmd += k.ToLower() + "=" + string.Format("{0:f3}", oee.Values[k]) + ",";//浮点数保留3位
                                else
                                    updatecmd += k.ToLower() + "=" + oee.Values[k].ToString() + ",";
                            }
                        }
                        catch { }
                    }
                    string cmd = "Server=10.19.27.11;User Id=sa;Pwd=qwe!23;database=DataSystem;Max Pool Size=512;";
                    SqlConnection con = new SqlConnection(cmd);
                    DateTime Now = DateTime.Now;
                    try
                    {
                        con.Open();
                        SqlCommand Command = new SqlCommand();
                        Command.Connection = con;
                        /*
                        Command.CommandText = string.Format("SELECT * FROM Roeders_OeeHistory WHERE ip = '{0}' AND CONVERT(date,startTime,111)='{1}'", m.IP, DateTime.Now.ToShortDateString());
                        SqlDataReader sqlDataReader = Command.ExecuteReader();
                        if (sqlDataReader.Read())//有数据，更新表
                        {
                            DateTime start = (DateTime)sqlDataReader["startTime"];//获取开始记录时间
                            double allTime = (Now - start).TotalHours;//计算总时间，单位h
                            double startProTime = (double)sqlDataReader["startProTime"];
                            int oee_Val = (int)sqlDataReader["oee_Val"];
                            string mmd = updatecmd + string.Format("endTime='{0}',endProTime={1:f3},oee_Day={2:f3},oee_Now={3:f3}", Now.ToString("yyyy/MM/dd HH:mm:ss"), oee.ProTime, (Convert.ToDouble(string.Format("{0:f3}", oee.ProTime)) - startProTime) / oee_Val, (Convert.ToDouble(string.Format("{0:f3}", oee.ProTime)) - startProTime) / allTime);
                            Command.CommandText = string.Format("UPDATE Roeders_OeeHistory SET {0} WHERE ip='{1}' AND CONVERT(date,startTime,111)='{2}'", mmd, m.IP, DateTime.Now.ToShortDateString());
                            sqlDataReader.Close();
                            Command.ExecuteNonQuery();//获取受影响的行数
                        }
                        else//无数据，插入
                        {
                            Command.CommandText = string.Format("INSERT INTO Roeders_OeeHistory(ip,startTime,endTime,startProTime,endProTime,oee_Now,oee_Day,oee_Val)VALUES('{0}','{1}','{1}',{2:f3},{2:f3},{3:f3},{3:f3},24)", m.IP, Now.ToString("yyyy/MM/dd HH:mm:ss"), oee.ProTime, 0.0);
                            sqlDataReader.Close();
                            Command.ExecuteNonQuery();//获取受影响的行数
                        }
                        */
                        Command.CommandText = string.Format("SELECT * FROM Roeders_OeeNow WHERE ip = '{0}'", m.IP);
                        SqlDataReader sqlDataReader = Command.ExecuteReader();
                        if (sqlDataReader.Read())//有数据
                        {
                            DateTime start = (DateTime)sqlDataReader["startTime"];//获取开始记录时间
                            double allTime = (Now - start).TotalHours;//计算总时间，单位h
                            double startProTime = (double)sqlDataReader["startProTime"];
                            int oee_Val = (int)sqlDataReader["oee_Val"];
                            string mmd = updatecmd + string.Format("endTime='{0}',endProTime={1:f3},oee_Day={2:f3},oee_Now={3:f3}", Now.ToString("yyyy/MM/dd HH:mm:ss"), oee.ProTime, (Convert.ToDouble(string.Format("{0:f3}", oee.ProTime)) - startProTime) / oee_Val, (Convert.ToDouble(string.Format("{0:f3}", oee.ProTime)) - startProTime) / allTime);
                            Command.CommandText = string.Format("UPDATE Roeders_OeeNow SET {0} WHERE ip='{1}'", mmd, m.IP);
                            sqlDataReader.Close();
                            Command.ExecuteNonQuery();//获取受影响的行数
                            if (start.ToShortDateString() != Now.ToShortDateString())//记录的开始时间不是同一天的
                            {
                                Command.CommandText = string.Format("INSERT INTO Roeders_OeeHistory SELECT * FROM Roeders_OeeNow WHERE ip = '{0}'",  m.IP);
                                Command.ExecuteNonQuery();//获取受影响的行数
                                mmd = string.Format("MachineOutput=0,startTime='{0}',endTime='{0}',startProTime={1:f3},endProTime={1:f3},oee_Day=0.000,oee_Now=0.000", Now.ToString("yyyy/MM/dd HH:mm:ss"), oee.ProTime);
                                Command.CommandText = string.Format("UPDATE Roeders_OeeNow SET {0} WHERE ip='{1}'", mmd, m.IP);
                                Command.ExecuteNonQuery();//获取受影响的行数
                            }
                        }
                        else
                        {
                            sqlDataReader.Close();
                            /*
                            string mmd = updatecmd + string.Format("startTime='{0}',endTime='{0}',startProTime={1},endProTime={1},oee_Now=0.0,oee_Day=0.0", Now.ToString("yyyy/MM/dd HH:mm:ss"), oee.ProTime);
                            Command.CommandText = string.Format("UPDATE Roeders_OeeNow SET {0} WHERE ip='{1}'", mmd, Machine1_IP);
                            Command.ExecuteNonQuery();//获取受影响的行数
                            */
                            Command.CommandText = string.Format("INSERT INTO Roeders_OeeNow(ip,startTime,endTime,startProTime,endProTime,oee_Now,oee_Day,MachineOutput)VALUES('{0}','{1}','{1}',{2:f3},{2:f3},{3:f3},{3:f3},0)", m.IP, Now.ToString("yyyy/MM/dd HH:mm:ss"), oee.ProTime, 0.0);
                            Command.ExecuteNonQuery();//获取受影响的行数
                        }
                    }
                    catch (Exception ex)
                    { }
                    finally
                    {
                        con.Close();
                    }
                }
                catch (Exception ex) { }
            }
            timer3.Enabled = true;
        }

        /// <summary>
        /// 关闭并移除按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Button6_Click(object sender, EventArgs e)
        {
            if (server == null || server.Machines.Length == 0)
                return;
            int index = comboBox2.SelectedIndex;
            if (index < 0)
                return;
            server.RemoveMachine(index);
            comboBox2.Items.RemoveAt(index);
        }

        /// <summary>
        /// 定时器2中断，定时发送WDREQ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer2_Tick(object sender, EventArgs e)
        {
            if (server == null || server.Machines == null)
                return;
            foreach (Roders r in server.Machines)
            {
                string ip = r.MachineEndPoint.Address.ToString();
                try
                {
                    Rec_WDREQ rec = r.Send_WDREQ();
                    if (!rec.Ret && rec.Header == null)
                    {
                        richTextBox2.SelectionColor = Color.Red;
                        richTextBox2.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   " + r.MachineName + "\n");
                        richTextBox2.SelectionColor = Color.Red;
                        richTextBox2.AppendText(r.MachineType + "：" + ip + "已断开连接\n");
                        comboBox2.Items.Remove(ip);
                        server.RemoveMachine(r);
                        Write_Msg(ip, "已断开连接");
                    }
                }
                catch
                {
                    comboBox2.Items.Remove(ip);
                    server.RemoveMachine(r);
                }
            }
        }
        
        /// <summary>
        /// 选择机床
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ComboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            groupBox4.Text = comboBox3.Text;
        }
    }
}
