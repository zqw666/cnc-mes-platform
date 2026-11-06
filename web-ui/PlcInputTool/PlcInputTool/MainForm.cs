using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PLC;

namespace PlcInputTool
{
    public partial class MainForm : Form
    {
        private plc realPlc;          // 真实 PLC (PLC.dll)
        private VirtualPlc vPlc;      // 模拟 PLC (内存)
        private bool connected = false;
        private bool simMode = false; // true=模拟模式
        private int PlcDm = 100;

        private TextBox txtIp, txtPort, txtDmBase;
        private Button btnConnect;
        private Label lblStatus;
        private CheckBox chkSimMode;

        private TextBox txtDmAddr, txtDmValue;
        private Button btnDmRead, btnDmWrite;
        private TextBox txtMonAddr;
        private Button btnMonAdd, btnMonRemove, btnMonClear;
        private ListView lvMonitors;
        private System.Windows.Forms.Timer monTimer;
        private CheckBox chkMonAuto;

        private TextBox txtWrAddr;
        private Button btnWrRead;
        private Label lblWrValue;

        private TextBox txtBitAddr;
        private Button btnBitRead, btnBitOn, btnBitOff;
        private Label lblBitStatus;

        private TextBox txtTaskNum;
        private Button btnTaskExec;
        private CheckBox chkAutoPulse;

        private TextBox txtLog;
        private Button btnClearLog;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStripStatus;

        private const int OpTimeoutMs = 3000;

        public MainForm()
        {
            InitializeComponent();
            vPlc = new VirtualPlc();
        }

        private void InitializeComponent()
        {
            this.Text = "PLC 输入/监控工具";
            this.Size = new Size(1100, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("微软雅黑", 9F);
            this.MinimumSize = new Size(900, 600);
            this.FormClosing += (object s, FormClosingEventArgs e) => { monTimer.Enabled = false; Disconnect(); };

            Panel leftPanel = new Panel { Location = new Point(0, 0), Size = new Size(540, 720), AutoScroll = true };

            // ===== 1. 连接面板 =====
            GroupBox grpConnect = new GroupBox { Text = "1. PLC 连接", Location = new Point(12, 12), Size = new Size(510, 120) };

            txtIp = new TextBox { Location = new Point(45, 23), Size = new Size(120, 23), Text = "192.168.1.1" };
            txtPort = new TextBox { Location = new Point(215, 23), Size = new Size(60, 23), Text = "9600" };
            txtDmBase = new TextBox { Location = new Point(335, 23), Size = new Size(50, 23), Text = "100" };

            btnConnect = new Button { Text = "连接", Location = new Point(400, 22), Size = new Size(90, 28) };
            btnConnect.Click += BtnConnect_Click;

            chkSimMode = new CheckBox { Text = "模拟模式 (无需 PLC)", Location = new Point(10, 55), Size = new Size(180, 23), Checked = true };
            chkSimMode.CheckedChanged += (object s, EventArgs ea) => {
                if (connected) { Log("⚠ 请先断开后再切换模式"); chkSimMode.Checked = simMode; }
            };

            lblStatus = new Label { Text = "● 就绪 (模拟模式)", Location = new Point(10, 80), Size = new Size(400, 23), ForeColor = Color.Orange, Font = new Font("微软雅黑", 9F, FontStyle.Bold) };

            grpConnect.Controls.AddRange(new Control[] {
                new Label { Text = "IP:", Location = new Point(10, 25), Size = new Size(30, 23) }, txtIp,
                new Label { Text = "端口:", Location = new Point(175, 25), Size = new Size(40, 23) }, txtPort,
                new Label { Text = "DM基址:", Location = new Point(285, 25), Size = new Size(50, 23) }, txtDmBase,
                btnConnect, chkSimMode, lblStatus
            });

            // ===== 2. DM 区写入 =====
            GroupBox grpDm = new GroupBox { Text = "2. DM 区读写", Location = new Point(12, 140), Size = new Size(510, 80) };

            txtDmAddr = new TextBox { Location = new Point(60, 25), Size = new Size(70, 23) };
            txtDmValue = new TextBox { Location = new Point(165, 25), Size = new Size(80, 23) };
            btnDmWrite = new Button { Text = "写入", Location = new Point(255, 24), Size = new Size(60, 25) };
            btnDmWrite.Click += BtnDmWrite_Click;
            btnDmRead = new Button { Text = "读取", Location = new Point(320, 24), Size = new Size(60, 25) };
            btnDmRead.Click += BtnDmRead_Click;

            grpDm.Controls.AddRange(new Control[] {
                new Label { Text = "DM地址:", Location = new Point(10, 27), Size = new Size(50, 23) }, txtDmAddr,
                new Label { Text = "值(0-65535):", Location = new Point(135, 27), Size = new Size(30, 23) }, txtDmValue,
                btnDmWrite, btnDmRead
            });

            // ===== 3. W 区字读取 =====
            GroupBox grpWr = new GroupBox { Text = "3. W 区字读取", Location = new Point(12, 228), Size = new Size(510, 70) };

            txtWrAddr = new TextBox { Location = new Point(80, 25), Size = new Size(80, 23) };
            btnWrRead = new Button { Text = "读取", Location = new Point(170, 24), Size = new Size(60, 25) };
            btnWrRead.Click += BtnWrRead_Click;
            lblWrValue = new Label { Text = "", Location = new Point(240, 25), Size = new Size(250, 23), Font = new Font("Consolas", 10F, FontStyle.Bold) };

            grpWr.Controls.AddRange(new Control[] {
                new Label { Text = "W地址(0-511):", Location = new Point(10, 27), Size = new Size(70, 23) }, txtWrAddr,
                btnWrRead, lblWrValue
            });

            // ===== 4. W/R 位操作 =====
            GroupBox grpBit = new GroupBox { Text = "4. W/R 位操作", Location = new Point(12, 306), Size = new Size(510, 80) };

            txtBitAddr = new TextBox { Location = new Point(120, 25), Size = new Size(100, 23) };
            btnBitRead = new Button { Text = "读取", Location = new Point(230, 24), Size = new Size(55, 25) };
            btnBitRead.Click += BtnBitRead_Click;
            btnBitOn = new Button { Text = "置 1", Location = new Point(290, 24), Size = new Size(55, 25), BackColor = Color.LightGreen };
            btnBitOn.Click += BtnBitOn_Click;
            btnBitOff = new Button { Text = "置 0", Location = new Point(350, 24), Size = new Size(55, 25), BackColor = Color.LightCoral };
            btnBitOff.Click += BtnBitOff_Click;
            lblBitStatus = new Label { Text = "", Location = new Point(415, 25), Size = new Size(80, 23), Font = new Font("微软雅黑", 9F, FontStyle.Bold) };

            grpBit.Controls.AddRange(new Control[] {
                new Label { Text = "地址(如 200.00):", Location = new Point(10, 27), Size = new Size(110, 23) }, txtBitAddr,
                btnBitRead, btnBitOn, btnBitOff, lblBitStatus
            });

            // ===== 5. 任务执行 =====
            GroupBox grpTask = new GroupBox { Text = "5. 任务执行", Location = new Point(12, 394), Size = new Size(510, 80) };

            txtTaskNum = new TextBox { Location = new Point(70, 25), Size = new Size(100, 25), Font = new Font("微软雅黑", 10F) };
            txtTaskNum.KeyDown += (object s, KeyEventArgs ke) => { if (ke.KeyCode == Keys.Enter) BtnTaskExec_Click(null, null); };
            btnTaskExec = new Button { Text = "▶ 执行", Location = new Point(180, 24), Size = new Size(80, 28), Font = new Font("微软雅黑", 9F, FontStyle.Bold) };
            btnTaskExec.Click += BtnTaskExec_Click;
            chkAutoPulse = new CheckBox { Text = "自动脉冲(200.00)", Location = new Point(275, 27), Size = new Size(150, 23), Checked = true };

            grpTask.Controls.AddRange(new Control[] {
                new Label { Text = "任务号:", Location = new Point(10, 28), Size = new Size(55, 23), Font = new Font("微软雅黑", 10F) },
                txtTaskNum, btnTaskExec, chkAutoPulse
            });

            // ===== 6. 信号监视器 =====
            GroupBox grpMon = new GroupBox { Text = "6. 信号监视器", Location = new Point(12, 482), Size = new Size(510, 230) };

            txtMonAddr = new TextBox { Location = new Point(78, 22), Size = new Size(100, 23) };
            btnMonAdd = new Button { Text = "添加", Location = new Point(185, 21), Size = new Size(55, 25) };
            btnMonAdd.Click += BtnMonAdd_Click;
            btnMonRemove = new Button { Text = "删除选中", Location = new Point(245, 21), Size = new Size(75, 25) };
            btnMonRemove.Click += BtnMonRemove_Click;
            btnMonClear = new Button { Text = "清空", Location = new Point(325, 21), Size = new Size(55, 25) };
            btnMonClear.Click += (object s, EventArgs ea) => { lvMonitors.Items.Clear(); };

            chkMonAuto = new CheckBox { Text = "自动刷新(500ms)", Location = new Point(390, 22), Size = new Size(110, 23), Checked = false };
            chkMonAuto.CheckedChanged += (object s, EventArgs ea) => {
                if (connected) monTimer.Enabled = chkMonAuto.Checked;
                else chkMonAuto.Checked = false;
            };

            lvMonitors = new ListView
            {
                Location = new Point(10, 54),
                Size = new Size(485, 165),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lvMonitors.Columns.Add("地址", 80);
            lvMonitors.Columns.Add("值", 60);
            lvMonitors.Columns.Add("类型", 60);
            lvMonitors.Columns.Add("状态", 130);
            lvMonitors.Columns.Add("备注", 140);

            grpMon.Controls.AddRange(new Control[] {
                new Label { Text = "地址:", Location = new Point(10, 24), Size = new Size(65, 23) }, txtMonAddr,
                btnMonAdd, btnMonRemove, btnMonClear, chkMonAuto, lvMonitors
            });

            leftPanel.Controls.AddRange(new Control[] { grpConnect, grpDm, grpWr, grpBit, grpTask, grpMon });

            // ================================================================
            // 右侧日志面板
            // ================================================================
            GroupBox grpLog = new GroupBox { Text = "操作日志", Location = new Point(560, 12), Size = new Size(510, 700) };

            txtLog = new TextBox
            {
                Location = new Point(10, 22),
                Size = new Size(490, 640),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGreen
            };

            btnClearLog = new Button { Text = "清空日志", Location = new Point(400, 668), Size = new Size(100, 25) };
            btnClearLog.Click += (object s, EventArgs ea) => { txtLog.Clear(); };

            grpLog.Controls.AddRange(new Control[] { txtLog, btnClearLog });

            // ================================================================
            // 状态栏
            // ================================================================
            statusStrip = new StatusStrip();
            lblStripStatus = new ToolStripStatusLabel("就绪 (模拟模式)");
            statusStrip.Items.Add(lblStripStatus);

            // ================================================================
            monTimer = new System.Windows.Forms.Timer { Interval = 500 };
            monTimer.Tick += MonTimer_Tick;

            this.Controls.AddRange(new Control[] { leftPanel, grpLog, statusStrip });
        }

        // ====================================================================
        // 连接
        // ====================================================================

        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            if (connected) { Disconnect(); return; }

            simMode = chkSimMode.Checked;

            if (simMode)
            {
                // 模拟模式：直接连接虚拟 PLC
                vPlc.Reset();
                connected = true;
                btnConnect.Text = "断开";
                lblStatus.Text = "● 已连接 (模拟模式)";
                lblStatus.ForeColor = Color.Green;
                lblStripStatus.Text = "已连接 (模拟模式)";
                Log("✓ 虚拟 PLC 已启动（纯内存，无需真实 PLC）");
                Log("提示：所有操作均在内存中模拟，可自由测试");
                if (chkMonAuto.Checked) monTimer.Enabled = true;
                return;
            }

            // 真实模式
            string ip = txtIp.Text.Trim();
            int port = 9600, dm = 100;
            int.TryParse(txtPort.Text.Trim(), out port);
            int.TryParse(txtDmBase.Text.Trim(), out dm);
            PlcDm = dm;

            btnConnect.Enabled = false;
            lblStatus.Text = "● 正在连接...";
            lblStatus.ForeColor = Color.Orange;
            Log("正在连接 PLC " + ip + ":" + port + " ...");

            bool ok = false;
            await Task.Run(() =>
            {
                try
                {
                    realPlc = new plc();
                    var t = Task.Run(() => realPlc.Connect(ip, port));
                    if (t.Wait(5000) && t.Result)
                    {
                        Log("  ✓ Connect 成功");
                        ok = true;
                    }
                    else
                    {
                        Log("  ✗ 连接超时或失败");
                        try { realPlc.close(); } catch { }
                        realPlc = null;
                    }
                }
                catch (Exception ex)
                {
                    Log("  ✗ 异常: " + ex.Message);
                    try { if (realPlc != null) realPlc.close(); } catch { }
                    realPlc = null;
                }
            });

            btnConnect.Enabled = true;

            if (ok)
            {
                connected = true;
                btnConnect.Text = "断开";
                lblStatus.Text = "● 已连接";
                lblStatus.ForeColor = Color.Green;
                lblStripStatus.Text = "已连接 " + ip + ":" + port;
                Log("✓ PLC 连接成功");
                if (chkMonAuto.Checked) monTimer.Enabled = true;
            }
            else
            {
                lblStatus.Text = "● 连接失败";
                lblStatus.ForeColor = Color.Red;
                lblStripStatus.Text = "未连接";
                Log("✗ PLC 连接失败");
            }
        }

        private void Disconnect()
        {
            monTimer.Enabled = false;
            try { if (realPlc != null) realPlc.close(); } catch { }
            realPlc = null;
            vPlc = new VirtualPlc();
            connected = false;
            btnConnect.Text = "连接";
            lblStatus.Text = "● 就绪 (模拟模式)";
            lblStatus.ForeColor = Color.Orange;
            lblStripStatus.Text = "就绪 (模拟模式)";
            Log("已断开连接，回到模拟模式");
        }

        // ====================================================================
        // DM 操作
        // ====================================================================

        private void BtnDmWrite_Click(object sender, EventArgs e)
        {
            if (!CheckConnected()) return;
            int addr = 0, val = 0;
            if (!int.TryParse(txtDmAddr.Text.Trim(), out addr)) { Log("⚠ 请输入有效的 DM 地址"); return; }
            if (!int.TryParse(txtDmValue.Text.Trim(), out val)) { Log("⚠ 请输入有效的值"); return; }

            try
            {
                Log("写入 DM[" + addr + "] = " + val + " (0x" + val.ToString("X4") + ") ...");
                bool ok = false;
                if (simMode)
                    ok = vPlc.WriteDM(addr, val);
                else
                {
                    var t = Task.Run(() => realPlc.WriteDM(addr, val));
                    ok = t.Wait(OpTimeoutMs) && t.Result;
                }
                Log(ok ? "✓ 写入成功" : "✗ 写入失败");
            }
            catch (Exception ex) { Log("✗ 异常: " + ex.Message); }
        }

        private void BtnDmRead_Click(object sender, EventArgs e)
        {
            if (!CheckConnected()) return;
            int addr = 0;
            if (!int.TryParse(txtDmAddr.Text.Trim(), out addr)) { Log("⚠ 请输入有效的 DM 地址"); return; }

            try
            {
                if (simMode)
                {
                    vPlc.ReadDM(addr);
                    string hex = vPlc.GetHex();
                    if (!string.IsNullOrEmpty(hex))
                    {
                        int val = Convert.ToInt32(hex, 16);
                        Log("DM[" + addr + "] = " + val + " (0x" + val.ToString("X4") + ")");
                        txtDmValue.Text = val.ToString();
                    }
                }
                else
                {
                    string hexBefore = "";
                    try { hexBefore = realPlc.GetHex(); } catch { }
                    bool ok = false;
                    var t = Task.Run(() => ok = realPlc.ReadDM(addr));
                    if (t.Wait(OpTimeoutMs) && ok)
                    {
                        string hex = "";
                        try { hex = realPlc.GetHex(); } catch { }
                        if (!string.IsNullOrEmpty(hex) && hex != hexBefore)
                        {
                            int val = Convert.ToInt32(hex.Replace(" ", "").Replace("\r", "").Replace("\n", ""), 16);
                            Log("DM[" + addr + "] = " + val + " (0x" + val.ToString("X4") + ")");
                            txtDmValue.Text = val.ToString();
                        }
                        else Log("DM[" + addr + "] 存在");
                    }
                    else Log("✗ 读取失败");
                }
            }
            catch (Exception ex) { Log("✗ 异常: " + ex.Message); }
        }

        // ====================================================================
        // W 区操作
        // ====================================================================

        private void BtnWrRead_Click(object sender, EventArgs e)
        {
            if (!CheckConnected()) return;
            int addr = 0;
            if (!int.TryParse(txtWrAddr.Text.Trim(), out addr) || addr < 0 || addr > 511)
            { Log("⚠ 请输入有效的 W 地址（0-511）"); return; }

            try
            {
                int val = 0;
                if (simMode)
                    val = vPlc.ReadWr((ushort)addr);
                else
                {
                    var t = Task.Run(() => val = realPlc.ReadWr((ushort)addr));
                    if (!t.Wait(OpTimeoutMs)) { Log("✗ 读取超时"); return; }
                }
                string display = "W[" + addr + "] = " + val + "  (0x" + val.ToString("X4") + ")  (bin: " + Convert.ToString(val, 2).PadLeft(16, '0') + ")";
                lblWrValue.Text = display;
                Log(display);
            }
            catch (Exception ex) { Log("✗ 异常: " + ex.Message); }
        }

        // ====================================================================
        // W/R 位操作
        // ====================================================================

        private void BtnBitRead_Click(object sender, EventArgs e)
        {
            if (!CheckConnected()) return;
            string addr = txtBitAddr.Text.Trim();
            if (string.IsNullOrEmpty(addr)) { Log("⚠ 请输入位地址"); return; }

            try
            {
                bool val = false;
                if (simMode)
                    val = vPlc.ReadWR(addr);
                else
                {
                    var t = Task.Run(() => val = realPlc.ReadWR(addr));
                    if (!t.Wait(OpTimeoutMs)) { Log("✗ 读取超时"); return; }
                }
                lblBitStatus.Text = val ? "● 1 (ON)" : "○ 0 (OFF)";
                lblBitStatus.ForeColor = val ? Color.Red : Color.Gray;
                Log("读取 " + addr + " = " + (val ? "1 (ON)" : "0 (OFF)"));
            }
            catch (Exception ex) { Log("✗ 异常: " + ex.Message); }
        }

        private void BtnBitOn_Click(object sender, EventArgs e)
        {
            if (!CheckConnected()) return;
            string addr = txtBitAddr.Text.Trim();
            if (string.IsNullOrEmpty(addr)) { Log("⚠ 请输入位地址"); return; }

            try
            {
                if (simMode)
                {
                    vPlc.SetBit(addr, true);
                    Log("✓ " + addr + " 已置 1");
                    lblBitStatus.Text = "● 1 (ON)";
                    lblBitStatus.ForeColor = Color.Red;
                }
                else
                {
                    bool ok = false;
                    var t = Task.Run(() => ok = realPlc.WriteWR(addr, 1));
                    if (!t.Wait(OpTimeoutMs)) { Log("✗ 置 1 超时"); return; }
                    if (ok) { Log("✓ " + addr + " 已置 1"); lblBitStatus.Text = "● 1 (ON)"; lblBitStatus.ForeColor = Color.Red; }
                    else Log("✗ 置 1 失败");
                }
            }
            catch (Exception ex) { Log("✗ 异常: " + ex.Message); }
        }

        private void BtnBitOff_Click(object sender, EventArgs e)
        {
            if (!CheckConnected()) return;
            string addr = txtBitAddr.Text.Trim();
            if (string.IsNullOrEmpty(addr)) { Log("⚠ 请输入位地址"); return; }

            try
            {
                if (simMode)
                {
                    vPlc.SetBit(addr, false);
                    Log("✓ " + addr + " 已置 0");
                    lblBitStatus.Text = "○ 0 (OFF)";
                    lblBitStatus.ForeColor = Color.Gray;
                }
                else
                {
                    bool ok = false;
                    var t = Task.Run(() => ok = realPlc.WriteWR(addr, 0));
                    if (!t.Wait(OpTimeoutMs)) { Log("✗ 置 0 超时"); return; }
                    if (ok) { Log("✓ " + addr + " 已置 0"); lblBitStatus.Text = "○ 0 (OFF)"; lblBitStatus.ForeColor = Color.Gray; }
                    else Log("✗ 置 0 失败");
                }
            }
            catch (Exception ex) { Log("✗ 异常: " + ex.Message); }
        }

        // ====================================================================
        // 任务执行
        // ====================================================================

        private async void BtnTaskExec_Click(object sender, EventArgs e)
        {
            if (!CheckConnected()) return;
            int taskNum = 0;
            if (!int.TryParse(txtTaskNum.Text.Trim(), out taskNum)) { Log("⚠ 请输入有效的任务号"); return; }

            btnTaskExec.Enabled = false;
            try
            {
                await Task.Run(() => ExecuteTask(taskNum));
            }
            finally
            {
                btnTaskExec.Enabled = true;
                txtTaskNum.Focus();
                txtTaskNum.SelectAll();
            }
        }

        private bool ExecuteTask(int taskNum)
        {
            Log("── 执行任务号: " + taskNum + " ──");
            try
            {
                // 写入任务号
                bool writeOk = false;
                if (simMode)
                    writeOk = vPlc.WriteDM(PlcDm, taskNum);
                else
                {
                    var t = Task.Run(() => realPlc.WriteDM(PlcDm, taskNum));
                    writeOk = t.Wait(OpTimeoutMs) && t.Result;
                }

                if (!writeOk) { Log("  ✗ 写入任务号失败"); return false; }
                Log("  ✓ 任务号写入成功");

                // 自动脉冲
                if (chkAutoPulse.Checked)
                {
                    Log("  发送开始脉冲（200.00 = 1 ...）");
                    if (simMode)
                    {
                        vPlc.SetBit("200.00", true);
                        Thread.Sleep(100);
                        vPlc.SetBit("200.00", false);
                        Log("  ✓ 脉冲发送完成");
                    }
                    else
                    {
                        var t = Task.Run(() => realPlc.WriteWR("200.00", 1));
                        if (t.Wait(OpTimeoutMs) && t.Result)
                        {
                            Thread.Sleep(100);
                            realPlc.WriteWR("200.00", 0);
                            Log("  ✓ 脉冲发送完成");
                        }
                        else Log("  ⚠ 脉冲发送失败");
                    }
                }

                Log("  ✔ 任务 " + taskNum + " 已下发执行");
                return true;
            }
            catch (Exception ex) { Log("  ✗ 异常: " + ex.Message); return false; }
        }

        // ====================================================================
        // 监视器
        // ====================================================================

        private void BtnMonAdd_Click(object sender, EventArgs e)
        {
            string addr = txtMonAddr.Text.Trim();
            if (string.IsNullOrEmpty(addr)) { Log("⚠ 请输入监视地址"); return; }

            foreach (ListViewItem item in lvMonitors.Items)
                if (item.Text == addr) { Log("⚠ 地址 " + addr + " 已在监视列表中"); return; }

            string type = "位";
            string addrUpper = addr.ToUpper().Replace(" ", "");
            if (addrUpper.StartsWith("W") && !addrUpper.Contains("."))
                type = "W字";
            else if (addrUpper.StartsWith("DM"))
                type = "DM";

            ListViewItem newItem = new ListViewItem(addr);
            newItem.SubItems.Add("---");
            newItem.SubItems.Add(type);
            newItem.SubItems.Add("");
            newItem.SubItems.Add("");
            lvMonitors.Items.Add(newItem);

            Log("添加监视: " + addr + " (" + type + ")");
            txtMonAddr.Clear();
            txtMonAddr.Focus();
        }

        private void BtnMonRemove_Click(object sender, EventArgs e)
        {
            if (lvMonitors.SelectedItems.Count > 0)
                foreach (ListViewItem item in lvMonitors.SelectedItems)
                    lvMonitors.Items.Remove(item);
        }

        private void MonTimer_Tick(object sender, EventArgs e)
        {
            if (!connected || lvMonitors.Items.Count == 0) return;

            foreach (ListViewItem item in lvMonitors.Items)
            {
                string addr = item.Text;
                string type = item.SubItems[2].Text;

                try
                {
                    if (type == "位")
                    {
                        bool val = simMode ? vPlc.ReadWR(addr) : realPlc.ReadWR(addr);
                        item.SubItems[1].Text = val ? "1" : "0";
                        item.SubItems[3].Text = val ? "● ON" : "○ OFF";
                        item.BackColor = val ? Color.LightPink : Color.White;
                    }
                    else if (type == "W字")
                    {
                        string numStr = addr.Replace("W", "").Replace("w", "").Trim();
                        ushort wAddr = 0;
                        if (ushort.TryParse(numStr, out wAddr))
                        {
                            int val = simMode ? vPlc.ReadWr(wAddr) : realPlc.ReadWr(wAddr);
                            item.SubItems[1].Text = val.ToString();
                            item.SubItems[3].Text = "0x" + val.ToString("X4");
                            item.SubItems[4].Text = "bin: " + Convert.ToString(val, 2).PadLeft(16, '0');
                            item.BackColor = Color.White;
                        }
                    }
                    else if (type == "DM")
                    {
                        string numStr = addr.Replace("DM", "").Replace("dm", "").Trim();
                        int dmAddr = 0;
                        if (int.TryParse(numStr, out dmAddr))
                        {
                            if (simMode)
                            {
                                vPlc.ReadDM(dmAddr);
                                string hex = vPlc.GetHex();
                                int val = Convert.ToInt32(hex, 16);
                                item.SubItems[1].Text = val.ToString();
                                item.SubItems[3].Text = "0x" + val.ToString("X4");
                            }
                            else
                            {
                                string hexBefore = "";
                                try { hexBefore = realPlc.GetHex(); } catch { }
                                bool ok = false;
                                var t = Task.Run(() => ok = realPlc.ReadDM(dmAddr));
                                if (t.Wait(500) && ok)
                                {
                                    string hex = "";
                                    try { hex = realPlc.GetHex(); } catch { }
                                    if (!string.IsNullOrEmpty(hex) && hex != hexBefore)
                                    {
                                        int val = Convert.ToInt32(hex.Replace(" ", "").Replace("\r", "").Replace("\n", ""), 16);
                                        item.SubItems[1].Text = val.ToString();
                                        item.SubItems[3].Text = "0x" + val.ToString("X4");
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // ====================================================================

        private bool CheckConnected()
        {
            if (!connected)
            {
                Log("⚠ 请先点 [连接] 按钮");
                return false;
            }
            return true;
        }

        private void Log(string msg)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(Log), msg);
                return;
            }
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            txtLog.AppendText("[" + time + "] " + msg + "\r\n");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }
    }
}
