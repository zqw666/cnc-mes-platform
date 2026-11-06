using System;
using System.Collections.Generic;
using System.Linq;

namespace PlcInputTool
{
    /// <summary>
    /// 虚拟 PLC — 模拟 PLC.dll 的 plc 类，纯内存操作，无需网络
    /// </summary>
    public class VirtualPlc
    {
        // DM 区 (0-32767)
        private Dictionary<int, int> dmArea = new Dictionary<int, int>();

        // W 区 (0-511)
        private int[] wArea = new int[512];

        // W/R 位状态 (通道.位, 例如 "200.00" → true/false)
        private Dictionary<string, bool> bitArea = new Dictionary<string, bool>();

        private string lastHex = "";

        public bool Connect(string ip, int port)
        {
            // 虚拟 PLC 总是连接成功
            return true;
        }

        public void close()
        {
            // 虚拟 PLC 无需清理
        }

        // ---- DM 区 ----

        public bool WriteDM(int addr, int val)
        {
            dmArea[addr] = val & 0xFFFF; // 16位
            lastHex = val.ToString("X4");
            return true;
        }

        public bool ReadDM(int addr)
        {
            int val = 0;
            if (dmArea.ContainsKey(addr))
                val = dmArea[addr];
            lastHex = val.ToString("X4");
            return true;
        }

        public string GetHex()
        {
            return lastHex;
        }

        // ---- W 区字 ----

        public int ReadWr(ushort addr)
        {
            if (addr < 0 || addr >= 512) return 0;
            return wArea[addr];
        }

        public int ReadWrDWord(ushort addr)
        {
            if (addr < 0 || addr >= 511) return 0;
            return (wArea[addr + 1] << 16) | wArea[addr];
        }

        // ---- W/R 位操作 ----

        public bool ReadWR(string addr)
        {
            string key = NormalizeBitAddr(addr);
            if (bitArea.ContainsKey(key))
                return bitArea[key];
            return false;
        }

        public bool WriteWR(string addr, byte val)
        {
            string key = NormalizeBitAddr(addr);
            bitArea[key] = val != 0;

            // 同时更新对应的 W 区字值
            UpdateWrFromBits(key);
            return true;
        }

        // ---- 辅助 ----

        private string NormalizeBitAddr(string addr)
        {
            // 统一格式: "200.00"
            return addr.Replace(" ", "").Trim();
        }

        private void UpdateWrFromBits(string key)
        {
            // 解析 "200.00" → 通道=200, 位=0
            var parts = key.Split('.');
            if (parts.Length != 2) return;

            int channel = 0, bit = 0;
            if (!int.TryParse(parts[0], out channel)) return;
            if (!int.TryParse(parts[1], out bit)) return;
            if (channel < 0 || channel >= 512 || bit < 0 || bit > 15) return;

            // 重新计算该通道所有位的值
            int val = 0;
            for (int b = 0; b < 16; b++)
            {
                string bk = channel + "." + b.ToString("D2");
                if (bitArea.ContainsKey(bk) && bitArea[bk])
                    val |= (1 << b);
            }
            wArea[channel] = val;
        }

        // ---- 模拟专用：重置/初始化 ----

        public void Reset()
        {
            dmArea.Clear();
            wArea = new int[512];
            bitArea.Clear();
            lastHex = "";
        }

        public void SetWrValue(ushort addr, int val)
        {
            if (addr < 0 || addr >= 512) return;
            wArea[addr] = val & 0xFFFF;

            // 同步更新对应的位
            for (int b = 0; b < 16; b++)
            {
                string key = addr + "." + b.ToString("D2");
                bitArea[key] = (val & (1 << b)) != 0;
            }
        }

        public void SetBit(string addr, bool val)
        {
            string key = NormalizeBitAddr(addr);
            bitArea[key] = val;
            UpdateWrFromBits(key);
        }
    }
}
