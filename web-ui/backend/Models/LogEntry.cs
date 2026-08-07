using System;

namespace MachineMonitor
{
    public class LogEntry
    {
        public string Step { get; set; }
        public string Display { get; set; }
        public string Level { get; set; }
        public string Content { get; set; }
        public DateTime Time { get; set; }
    }
}
