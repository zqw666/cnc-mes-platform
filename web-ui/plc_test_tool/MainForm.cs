using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using PLC;

namespace PlcTestTool
{
    public partial class MainForm : Form
    {
        private plc Plc;
        private bool connected = false;
        private int PlcDm = 100;
        private string TaskStartAddr = "";
        private string TaskFinishAddr = "";

        // 控件
        private TextBox txtConfigPath;
        private TextBox txtPlcIp;
        private TextBox txtPlcPort;
        private TextBox txtPlcDm;
        private TextBox txtTaskStart;
        private TextBox txtTaskFinish;
        private Button btnConnect;
        private Label lblStatus;
        private TextBox txtTaskNum;
        private Button btnExecute;
        private TextBox txtLog;
        private Button btnClearLog;
        private Button btnLoadConfig;

        public MainForm()
        {
            InitializeComponent();
            LoadConfig();
        }

        private void InitializeComponent()
        {
            this.Text = "PLC 任务号测试工具";
            this.Size = new Size(780, 620);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微软雅黑", 9F);

            // ===== 配置区域 =====
            var grpConfig = new GroupBox { Text = "PLC 配置", Location = new Point(12, 12), Size = new Size(500, 180) };

            var lbl1 = new Label { Text = "配置文件:", Location = new Point(10, 25), Size = new Size(70, 23) };
            txtConfigPath = new TextBox { Location = new Point(85, 23), Size = new Size(320, 23), ReadOnly = true };
            btnLoadConfig = new Button { Text = "加载", Location = new Point(410, 22), Size = new Size(70, 25) };
            btnLoadConfig.Click += (s, e) => LoadConfig();

            var lbl2 = new Label { Text = "PLC IP:", Location = new Point(10, 55), Size = new Size(70, 23) };
            txtPlcIp = new TextBox { Location = new Point(85, 53), Size = new Size(120, 23) };

            var lbl3 = new Label { Text = "端口:", Location = new Point(215, 55), Size = new Size(40, 23) };
            txtPlcPort = new TextBox { Location = new Point(255, 53), Size = new Size(60, 23), Text = "9600" };

            var lbl4 = new Label { Text = "DM:", Location = new Point(325, 55), Size = new Size(35, 23) };
            txtPlcDm = new TextBox { Location = new Point(360, 53), Size = new Size(50, 23), Text = "100" };

            btnConnect = new Button { Text = "连接 PLC", Location = new Point(420, 52), Size = new Size(70, 25) };
            btnConnect.Click += BtnConnect_Click;

            var lbl5 = new Label { Text = "开始信号:", Location = new Point(10, 85), Size = new Size(70, 23) };
            txtTaskStart = new TextBox { Location = new Point(85, 83), Size = new Size(100, 23) };

            var lbl6 = new Label { Text = "完成信号:", Location = new Point(195, 85), Size = new Size(70, 23) };
            txtTaskFinish = new TextBox { Location = new Point(265, 83), Size = new Size(100, 23) };

            lblStatus = new Label { Text = "● 未连接", Location = new Point(10, 115), Size = new Size(200, 23), ForeColor = Color.Gray };

            grpConfig.Controls.AddRange(new Control[] {
                lbl1, txtConfigPath, btnLoadConfig,
                lbl2, txtPlcIp, lbl3, txtPlcPort, lbl4, txtPlcDm, btnConnect,
                lbl5, txtTaskStart, lbl6, txtTaskFinish, lblStatus
            });

            // ===== 执行区域 =====
            var grpExecute = new GroupBox { Text = "执行任务", Location = new Point(12, 200), Size = new Size(500, 80) };

            var lblTask = new Label { Text = "任务号:", Location = new Point(10, 30), Size = new Size(60, 23), Font = new Font("微软雅黑", 12F) };
            txtTaskNum = new TextBox { Location = new Point(75, 27), Size = new Size(120, 30), Font = new Font("微软雅黑", 12F) };
            txtTaskNum.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) BtnExecute_Click(null, null); };

            btnExecute = new Button { Text = "▶ 执行", Location = new Point(205, 25), Size = new Size(100, 35) };
            btnExecute.Font = new Font("微软雅黑", 10F, FontStyle.Bold);
            btnExecute.Click += BtnExecute_Click;

            grpExecute.Controls.AddRange(new Control[] { lblTask, txtTaskNum, btnExecute });

            // ===== 日志区域 =====
            var grpLog = new GroupBox { Text = "执行日志", Location = new Point(12, 290), Size = new Size(740, 280) };

            txtLog = new TextBox
            {
                Location = new Point(10, 20),
                Size = new Size(720, 220),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(0, 255, 0)
            };

            btnClearLog = new Button { Text = "清空日志", Location = new Point(10, 248), Size = new Size(80, 25) };
            btnClearLog.Click += (s, e) => txtLog.Clear();

            grpLog.Controls.AddRange(new Control[] { txtLog, btnClearLog });

            // ===== 添加到窗体 =====
            this.Controls.AddRange(new Control[] { grpConfig, grpExecute, grpLog });
        }

        private void Log(string msg)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(Log), msg);
                return;
            }
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void LoadConfig()
        {
            string[] paths = {
                "config.json",
                @"..\..\..\backend\bin\Any CPU\Debug\net472\config.json",
                @"D:\模块化软件\前后端分离\前后端分离\web-ui\backend\bin\Any CPU\Debug\net472\config.json"
            };

            string configPath = null;
            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    configPath = Path.GetFullPath(p);
                    break;
                }
            }

            if (configPath == null)
            {
                Log("错误: 找不到 config.json");
                return;
            }

            try
            {
                txtConfigPath.Text = configPath;
                var json = File.ReadAllText(configPath);
                var js = new JavaScriptSerializer();
                var cfg = js.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);

                if (cfg.ContainsKey("plc") && cfg["plc"] is System.Collections.Generic.Dictionary<string, object> plcCfg)
                {
                    txtPlcIp.Text = plcCfg.ContainsKey("ip") ? plcCfg["ip"].ToString() : "192.168.1.1";
                    txtPlcPort.Text = plcCfg.ContainsKey("port") ? plcCfg["port"].ToString() : "9600";

                    if (plcCfg.ContainsKey("signals") && plcCfg["signals"] is System.Collections.Generic.Dictionary<string, object> signals)
                    {
                        if (signals.ContainsKey("robot") && signals["robot"] is System.Collections.Generic.Dictionary<string, object> robot)
                        {
                            txtTaskStart.Text = robot.ContainsKey("taskStart") ? robot["taskStart"].ToString() : "";
                            txtTaskFinish.Text = robot.ContainsKey("taskDone") ? robot["taskDone"].ToString() : "";
                            string taskNumStr = robot.ContainsKey("taskNumber") ? robot["taskNumber"].ToString() : "D100";
                            if (string.IsNullOrEmpty(taskNumStr)) taskNumStr = "D100";
                            txtPlcDm.Text = int.Parse(taskNumStr.Replace("D", "").Replace("DM", "")).ToString();
                        }
                    }
                }
                Log("配置已加载: " + configPath);
            }
            catch (Exception ex)
            {
                Log("加载配置失败: " + ex.Message);
            }
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (connected)
            {
                // 断开
                try { Plc?.close(); } catch { }
                Plc = null;
                connected = false;
                btnConnect.Text = "连接 PLC";
                lblStatus.Text = "● 未连接";
                lblStatus.ForeColor = Color.Gray;
                Log("PLC 已断开");
                return;
            }

            string ip = txtPlcIp.Text.Trim();
            int port = int.TryParse(txtPlcPort.Text.Trim(), out int p) ? p : 9600;
            PlcDm = int.TryParse(txtPlcDm.Text.Trim(), out int dm) ? dm : 100;
            TaskStartAddr = txtTaskStart.Text.Trim();
            TaskFinishAddr = txtTaskFinish.Text.Trim();

            Log("正在连接 PLC " + ip + ":" + port + " ...");
            try
            {
                Plc = new plc();
                if (Plc.Connect(ip, port))
                {
                    connected = true;
                    btnConnect.Text = "断开";
                    lblStatus.Text = "● 已连接";
                    lblStatus.ForeColor = Color.Green;
                    Log("PLC 连接成功");
                }
                else
                {
                    Log("PLC 连接失败（不可达）");
                }
            }
            catch (Exception ex)
            {
                Log("连接异常: " + ex.Message);
            }
        }

        private async void BtnExecute_Click(object sender, EventArgs e)
        {
            if (!connected)
            {
                Log("请先连接 PLC");
                return;
            }

            string input = txtTaskNum.Text.Trim();
            if (!int.TryParse(input, out int taskNum))
            {
                Log("请输入有效的任务号");
                return;
            }

            btnExecute.Enabled = false;
            txtTaskNum.Enabled = false;
            try
            {
                await Task.Run(() => ExecuteTask(taskNum));
            }
            finally
            {
                btnExecute.Enabled = true;
                txtTaskNum.Enabled = true;
                txtTaskNum.Focus();
                txtTaskNum.SelectAll();
            }
        }

        private bool ExecuteTask(int tasknum)
        {
            Log("── 执行任务号: " + tasknum + " ──");

            try
            {
                // 1. 复位任务开始信号
                if (!string.IsNullOrEmpty(TaskStartAddr))
                {
                    Log("复位开始信号: " + TaskStartAddr);
                    Plc.WriteWR(TaskStartAddr, 0);
                }

                // 2. 写入任务号
                Log("写入任务号 " + tasknum + " 至 DM" + PlcDm + " ...");
                if (!Plc.WriteDM(PlcDm, tasknum))
                {
                    Log("✗ 发送任务号失败");
                    return false;
                }
                Log("✓ 任务号写入成功");

                // 3. 发送任务开始信号
                if (!string.IsNullOrEmpty(TaskStartAddr))
                {
                    Log("发送开始信号: " + TaskStartAddr);
                    if (!Plc.WriteWR(TaskStartAddr, 1))
                    {
                        Log("✗ 发送开始信号失败");
                        return false;
                    }
                    Log("✓ 开始信号已发送");
                }
                else
                {
                    Log("⚠ 未配置开始信号，跳过");
                }

                // 4. 等待任务完成信号
                if (!string.IsNullOrEmpty(TaskFinishAddr))
                {
                    Log("等待完成信号 " + TaskFinishAddr + " ...（最长 300 秒）");
                    int waitCount = 0;
                    while (!Plc.ReadWR(TaskFinishAddr) && waitCount < 3000)
                    {
                        Thread.Sleep(100);
                        waitCount++;
                        if (waitCount % 50 == 0)
                            Log("  已等待 " + (waitCount / 10) + " 秒...");
                    }

                    if (!Plc.ReadWR(TaskFinishAddr))
                    {
                        Log("✗ 等待完成信号超时，任务: " + tasknum);
                        return false;
                    }

                    try { Plc.WriteWR(TaskFinishAddr, 0); } catch { }
                    Log("✓ 完成信号已收到");
                }
                else
                {
                    Log("⚠ 未配置完成信号，无法确认任务完成");
                }

                Log("✔ 任务 " + tasknum + " 执行成功！");
                return true;
            }
            catch (Exception ex)
            {
                Log("✗ 异常: " + ex.Message);
                return false;
            }
        }
    }
}
