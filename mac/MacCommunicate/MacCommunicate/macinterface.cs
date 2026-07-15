using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MacCommunicate
{
    public interface macinterface
    {
        bool ConnectMac();

        string GetMacStatus();

        string GetKnifeMes(int macindex);

        bool MacStartProcess(int macindex);

        bool OpenDoor(int macindex);

        bool closeDoor(int macindex);
    }
}
