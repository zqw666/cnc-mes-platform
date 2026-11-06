using System;

namespace MachineMonitor
{
    public class LogEntry
    {
        public long Id { get; set; }
        public string Step { get; set; }
        public string Display { get; set; }
        public string Level { get; set; }
        public string Content { get; set; }
        public DateTime Time { get; set; }
    }
}
