using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;


namespace mcOMRON
{
    /// <summary>
    /// Implemented transport's type  使用的通讯协议类型
    /// </summary>
    public enum TransportType
    {
        Tcp,
        udp
    };



	/// <summary>
	/// 
	/// Version:	1.0
	/// Author:		Joan Magnet
	/// Date:		02/2015
	/// 
	/// manage communications with an OMRON PLC
	/// 
	/// Implemented FINS commands:
	/// 
	///		- [1,1] MEMORY AREA READ
	///		- [1,2] MEMORY AREA WRITE
	///		- [5,1] CONTROLLER DATA READ
	/// 
	/// Specific DM commands:
	/// 
	///		- ReadDM		(read one DM)
	///		- ReadDMs		(read various DM)
	///		- WriteDM		(write one DM, 16-bit / 32-bit)
	///		- ClearDMs		(clear, set to 0 various DM)
	///		- ReadCIOBit	(read 1 bit from CIO area)
	///		- WriteCIOBit	(write one bit at CIO area)
	///		- ReadWR		(read one WR word)
	///		- WriteWR		(write one WR word)
	///		- ReadWRLReal	(read WR as 64-bit double)
	///		- ReadWRBit		(read 1 bit from WR area)
	///		- WriteWRBit	(write one bit at WR area)
	/// 
	/// </summary>
	public class OmronPLC
	{
		#region **** properties 属性

		/// <summary>
		/// return the connection status
		/// </summary>
		public bool Connected
		{
			get { return this._finsCmd.Connected; }
		}


		/// <summary>
		/// last detected error
		/// </summary>
		public string LastError
		{
			get { return this._finsCmd.LastError; }
		}


		/// <summary>
		/// current FINS command object
		/// </summary>
		public mcOMRON.IFINSCommand FinsCommand
		{
			get { return this._finsCmd; }
		}

		#endregion



		#region **** constructor 构造函数

		/// <summary>
		/// constructor, a IFinsCommand layer required
		/// </summary>
		public OmronPLC(TransportType TType)
		{
			switch(TType)
			{
				case TransportType.Tcp:
					this._finsCmd = new tcpFINSCommand();
					break;
                case TransportType.udp:
                    this._finsCmd = new UdpFinsCommand();
                    break;
			}
		}

		#endregion



		#region **** connect & close 与PLC连接与断开链接方法

		/// <summary>
		/// try to connect with the plc
		/// </summary>
		public bool Connect()
		{
			return this._finsCmd.Connect();
		}



		/// <summary>
		/// close the communication with the plc
		/// </summary>
		public void Close()
		{
			this._finsCmd.Close();
		}

        #endregion



        #region **** FINS commands


        /// <summary>
        /// MEMORY AREA READ
        /// </summary>
        /// <param name="area">寄存器类型</param>
        /// <param name="address">起始地址</param>
        /// <param name="bit_position">位地址：00-15,字地址则为00</param>
        /// <param name="count">地址个数,按位读写只能是1</param>
        /// <returns></returns>
        public bool finsMemoryAreadRead(MemoryArea area, UInt16 address, Byte bit_position, UInt16 count)
		{
			return _finsCmd.MemoryAreaRead(area, address, bit_position, count);
		}



        /// <summary>
        /// MEMORY AREA WRITE
        /// </summary>
        /// <param name="area">寄存器类型</param>
        /// <param name="address">起始地址</param>
        /// <param name="bit_position">位地址：00-15,字地址则为00</param>
        /// <param name="count">地址个数,按位读写只能是1</param>
        /// <param name="data">写入值</param>
        /// <returns></returns>
        public bool finsMemoryAreadWrite(MemoryArea area, UInt16 address, Byte bit_position, UInt16 count, Byte[] data)
		{
			return _finsCmd.MemoryAreaWrite(area, address, bit_position, count, ref data);
		}



		/// <summary>
		/// CONNECTION DATA READ
		/// </summary>
		/// <param name="area"></param>
		/// <returns></returns>
		public bool finsConnectionDataRead(Byte area)
		{
			return _finsCmd.ConnectionDataRead(area);
		}

		#endregion



		#region **** predefined DM commands

		/// <summary>
		/// read one DM
		/// </summary>
		public bool ReadDM(UInt16 position, ref UInt16 value)
		{
			if (!finsMemoryAreadRead(MemoryArea.DM, position, 0, 1)) return false;

			value = BTool.BytesToUInt16(this._finsCmd.Response[0], this._finsCmd.Response[1]);

			return true;
		}


		/// <summary>
		/// read one DM using signed values
		/// </summary>
		public bool ReadDM(UInt16 position, ref Int16 value)
		{
			if (!finsMemoryAreadRead(MemoryArea.DM, position, 0, 1)) return false;

			value = BTool.BytesToInt16(this._finsCmd.Response[0], this._finsCmd.Response[1]);

			return true;
		}


		/// <summary>
		/// read various DM
		/// </summary>
		public bool ReadDMs(UInt16 position, ref UInt16[] data, UInt16 count)
		{
			if (!finsMemoryAreadRead(MemoryArea.DM, position, 0, count)) return false;

			for (int x = 0; x < count; x++)
			{
				data[x] = BTool.BytesToUInt16(this._finsCmd.Response[(x * 2)], this._finsCmd.Response[(x * 2) + 1]);
			}

			return true;
		}


		/// <summary>
		/// write one DM (16-bit)
		/// </summary>
		public bool WriteDM(UInt16 position, UInt16 value)
		{
			Byte[] cmd = BTool.Uint16toBytes(value);

			return finsMemoryAreadWrite(MemoryArea.DM, position, 0, 1, cmd);
		}

        /// <summary>
        /// write one DM (32-bit, 2 consecutive DM words)
        /// </summary>
        public bool WriteDM(UInt16 position, UInt32 value)
        {
            UInt16[] words = BTool.Uint32toUInt16(value);
            Byte[] highWord = BTool.Uint16toBytes(words[0]);
            Byte[] lowWord = BTool.Uint16toBytes(words[1]);
            Byte[] cmd = new Byte[4];

            highWord.CopyTo(cmd, 0);
            lowWord.CopyTo(cmd, 2);

            return finsMemoryAreadWrite(MemoryArea.DM, position, 0, 2, cmd);
        }


		/// <summary>
		/// write one DM (signed 16-bit)
		/// </summary>
		public bool WriteDM(UInt16 position, Int16 value)
		{
			Byte[] cmd = BTool.Int16toBytes(value);

			return finsMemoryAreadWrite(MemoryArea.DM, position, 0, 1, cmd);
		}


		/// <summary>
		/// fills with 0 a memory area of the PLC
		/// </summary>
		public bool ClearDMs(UInt16 position, UInt16 count)
		{
			Byte[] cmd = new Byte[count * 2];
			for (int x = 0; x < (count * 2); cmd[x++] = 0) ;

			return finsMemoryAreadWrite(MemoryArea.DM, position, 0, count, cmd);
		}

		#endregion



		#region **** predefined CIO commands

		/// <summary>
		/// reads an specifit bit of CIO area
		/// </summary>
		public bool ReadCIOBit(UInt16 position, Byte bit_position, ref Byte value)
		{
			if (!finsMemoryAreadRead(MemoryArea.CIO_Bit, position, bit_position, 1)) return false;

			value = this._finsCmd.Response[0];

			return true;
		}


		/// <summary>
		/// write one specific bit of CIO area
		/// </summary>
		public bool WriteCIOBit(UInt16 position, Byte bit_position, Byte value)
		{
			Byte[] cmd = new Byte[1];
			cmd[0] = value;

			return finsMemoryAreadWrite(MemoryArea.CIO_Bit, position, bit_position, 1, cmd);
		}

		#endregion



        #region **** predefined WR commands

        /// <summary>
        /// read one WR word
        /// </summary>
        public bool ReadWR(UInt16 position, ref UInt16 value)
        {
            if (!finsMemoryAreadRead(MemoryArea.WR, position, 0, 1)) return false;

            value = BTool.BytesToUInt16(this._finsCmd.Response[0], this._finsCmd.Response[1]);

            return true;
        }


        /// <summary>
        /// write one WR word
        /// </summary>
        public bool WriteWR(UInt16 position, UInt16 value)
        {
            Byte[] cmd = BTool.Uint16toBytes(value);

            return finsMemoryAreadWrite(MemoryArea.WR, position, 0, 1, cmd);
        }


        /// <summary>
        /// read one WR LREAL value. LREAL uses four consecutive words.
        /// </summary>
        public bool ReadWRLReal(UInt16 position, ref Double value)
        {
            if (!finsMemoryAreadRead(MemoryArea.WR, position, 0, 4)) return false;

            Byte[] bytes = new Byte[8];
            for (int x = 0; x < 4; x++)
            {
                bytes[x * 2] = this._finsCmd.Response[(x * 2) + 1];
                bytes[(x * 2) + 1] = this._finsCmd.Response[x * 2];
            }

            value = BitConverter.ToDouble(bytes, 0);

            return true;
        }


        /// <summary>
        /// reads an specifit bit of WR area
        /// </summary>
        public bool ReadWRBit(UInt16 position, Byte bit_position, ref Byte value)
        {
            if (!finsMemoryAreadRead(MemoryArea.WR_Bit, position, bit_position, 1)) return false;

            value = this._finsCmd.Response[0];

            return true;
        }


        /// <summary>
        /// write one specific bit of WR area
        /// </summary>
        public bool WriteWRBit(UInt16 position, Byte bit_position, Byte value)
        {
            Byte[] cmd = new Byte[1];
            cmd[0] = value;

            return finsMemoryAreadWrite(MemoryArea.WR_Bit, position, bit_position, 1, cmd);
        }

        #endregion



		#region **** dialog

		/// <summary>
		/// return last dialog between PC & PLC
		/// </summary>
		public string LastDialog(string Caption)
		{
			return this.FinsCommand.LastDialog(Caption);
		}


        /// <summary>
        /// return the FINS response data as hex string
        /// </summary>
        public string ResultMess()
        {
            return BitConverter.ToString(this._finsCmd.Response);
        }
		
		#endregion		



		#region **** variables
		
		#region **** FINS command

		// FINS command object
		//
		private mcOMRON.IFINSCommand _finsCmd;

		#endregion

		#endregion
	}
}
