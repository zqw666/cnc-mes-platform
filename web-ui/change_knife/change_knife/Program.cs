using System;
using System.IO;
using System.Threading;
using DataCommonOperate;
using PLC;

namespace change_knife
{
    class Program
    {
        static readonly string ApiBase = "http://localhost:8888";
        static readonly string StepName = "change_knife";
        static readonly string DisplayName = "换刀流程";

        // 停止标志位：前端停止按钮触发后，后端创建 stop.flag，子程序检测到即终止
        static readonly string StopFlagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stop.flag");
        static bool IsStopRequested() { return File.Exists(StopFlagFile); }

        // ==================== MC1刀库旋转控制信号 ====================
        static readonly string MC1DoorClosed  = "101.01";  // MC1刀库门关闭信号
        static int PlcDm = 100;   // 任务号地址（DM区，从 plc.signals.robot.taskNumber 读取）
        static readonly string MC1RotateDone  = "105.00";  // MC1刀库转到位信号
        static readonly string MC1RotateStart = "104.01";  // MC1刀库旋转开始信号

        static void Main(string[] args)
        {
            LogHelper.Info( "【" + DisplayName + "】分包启动");

            try
            {
                var cfg = ConfigClient.FetchConfig(ApiBase);
                if (cfg.Count == 0) { LogHelper.Error("获取配置失败，配置来源: " + ConfigClient.LastSourceText); return; }
                var plcCfg = ConfigClient.GetDict(cfg, "plc");
                string PlcIp = ConfigClient.GetStr(plcCfg, "ip", "192.168.1.1");
                int PlcPort = ConfigClient.GetInt(plcCfg, "port", 9600);
                // 读取任务号地址（plc.signals.robot.taskNumber）
                var signals = ConfigClient.GetDict(plcCfg, "signals");
                var robotSig = ConfigClient.GetDict(signals, "robot");
                if (robotSig.Count > 0)
                {
                    string taskNoStr = ConfigClient.GetStr(robotSig, "taskNumber", "D100");
                    int.TryParse(taskNoStr.Replace("D", "").Replace("DM", "").Trim(), out PlcDm);
                }

                var plc = new plc();
                if (!plc.Connect(PlcIp, PlcPort))
                {
                    LogHelper.Error( "PLC 连接失败: " + PlcIp + ":" + PlcPort);
                    return;
                }
                LogHelper.Info( "PLC 连接成功: " + PlcIp);

                // 刀库旋转：依次旋转1~10号刀
                for (int t = 1; t <= 10; t++)
                {
                    if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止换刀流程"); break; }
                    RunMC1Magazine(plc, t);
                }

                LogHelper.Success( "刀库旋转完成");
            }
            catch (Exception ex)
            {
                LogHelper.Error( "执行异常: " + ex.Message);
            }

            LogHelper.Info( "【" + DisplayName + "】分包结束");
        }

        
        static bool RunMC1Magazine(plc plc, int toolNo)
        {
            LogHelper.Info( "【MC1刀库】检查门关闭信号 " + MC1DoorClosed);
            if (plc.ReadWR(MC1DoorClosed))
            {
                LogHelper.Warn( "【MC1刀库】刀库门未关闭(信号=1)，旋转失败");
                return false;
            }
            plc.WriteWR(MC1RotateStart, 0);
            LogHelper.Info( "【MC1刀库】旋转至 " + toolNo + " 号刀");
            plc.WriteDM(PlcDm, toolNo);
            plc.WriteWR(MC1RotateStart, 1);

            LogHelper.Info( "【MC1刀库】等待转到位信号 " + MC1RotateDone);
            while (!plc.ReadWR(MC1RotateDone))
            {
                if (IsStopRequested()) { LogHelper.Warn("收到停止信号，终止刀库旋转等待"); return false; }
                Thread.Sleep(200);
            }
            plc.WriteWR(MC1RotateStart, 0);
            LogHelper.Success( "【MC1刀库】" + toolNo + " 号刀旋转到位");
            return true;
        }
    }
}
