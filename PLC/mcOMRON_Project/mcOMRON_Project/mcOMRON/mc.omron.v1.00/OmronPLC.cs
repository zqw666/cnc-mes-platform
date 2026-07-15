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
	///		- WriteDM		(write one DM)
	///		- ClearDMs		(clear, set to 0 various DM)
	///		- ReadCIOBit	(read 1 bit from CIO area)
	///		- WriteCioBit	(write one bit at CIO area)
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
		/// <param name="finsCommand"></param>
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
		/// 
		/// CONNECTION DATA READ
		/// 
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
		/// <param name="position">要写入的DM区域</param>
		/// <param name="value">要写入的值</param>
		/// <returns></returns>
		public bool ReadDM(UInt16 position, ref UInt16 value)
		{
			// FINS command
			//
			if (!finsMemoryAreadRead(MemoryArea.DM, position, 0, 1)) return false;

			// value
			//
			value = BTool.BytesToUInt16(this._finsCmd.Response[0], this._finsCmd.Response[1]);

			return true;
		}



		/// <summary>
		/// read one DM using signed values
		/// </summary>
		/// <param name="position"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public bool ReadDM(UInt16 position, ref Int16 value)
		{
			// FINS command
			//
			if (!finsMemoryAreadRead(MemoryArea.DM, position, 0, 1)) return false;

			// value
			//
			value = BTool.BytesToInt16(this._finsCmd.Response[0], this._finsCmd.Response[1]);

			return true;
		}



		/// <summary>
		/// read various DM
		/// </summary>
		/// <param name="position"></param>
		/// <param name="data"></param>
		/// <param name="count"></param>
		/// <returns></returns>
		public bool ReadDMs(UInt16 position, ref UInt16[] data, UInt16 count)
		{
			// FINS command
			//
			if (!finsMemoryAreadRead(MemoryArea.DM, position, 0, count)) return false;

			// fills the array
			//
			for (int x = 0; x < count; x++)
			{
				data[x] = BTool.BytesToUInt16(this._finsCmd.Response[(x * 2)], this._finsCmd.Response[(x * 2) + 1]);
			}

			return true;
		}



		/// <summary>
		/// write one DM
		/// </summary>
		/// <param name="position"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public bool WriteDM(UInt16 position, UInt16 value)
		{
			// get the array
			//
			Byte[] cmd = BTool.Uint16toBytes(value);

			// fins command
			//
			return finsMemoryAreadWrite(MemoryArea.DM, position, 0, 1, cmd);
		}

        /// <summary>
        /// write one DM
        /// </summary>
        /// <param name="position">要写入的DM区域</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        public bool WriteDM(UInt16 position,UInt32 value)
        {
            ushort[] CMD = BTool.Uint32toUInt16(value);
            Byte[] cmd = BTool.Uint16toBytes(Convert.ToUInt16(CMD));
            return finsMemoryAreadWrite(MemoryArea.DM,position,0,1,cmd);
        }



		/// <summary>
		/// write one DM
		/// </summary>
		/// <param name="position"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public bool WriteDM(UInt16 position, Int16 value)
		{
			// get the array
			//
			Byte[] cmd = BTool.Int16toBytes(value);

			// fins command
			//
			return finsMemoryAreadWrite(MemoryArea.DM, position, 0, 1, cmd);
		}




		/// <summary>
		/// fills with 0 a mamory area of the PLC
		/// </summary>
		/// <param name="position"></param>
		/// <param name="count"></param>
		/// <returns></returns>
		public bool ClearDMs(UInt16 position, UInt16 count)
		{
			// zeroed array (each DM requieres 2 bytes)
			//
			Byte[] cmd = new Byte[count * 2];
			for (int x = 0; x < (count * 2); cmd[x++] = 0) ;

			// fins command
			//
			return finsMemoryAreadWrite(MemoryArea.DM, position, 0, count, cmd);
		}

		#endregion



		#region **** predefined CIO commands

		/// <summary>
		/// reads an specifit bit of CIO area
		/// </summary>
		/// <param name="position"></param>
		/// <param name="bit_position"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public bool ReadCIOBit(UInt16 position, Byte bit_position, ref Byte value)
		{
			// FINS command
			//
			if (!finsMemoryAreadRead(MemoryArea.CIO_Bit, position, bit_position, 1)) return false;

			// value
			//
			//value = BTool.BytesToUInt16(this._finsCmd.Response[0], this._finsCmd.Response[1]);
			value = this._finsCmd.Response[0];

			return true;
		}



		/// <summary>
		/// write one specific bit of CIO area
		/// </summary>
		/// <param name="position"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public bool WriteCIOBit(UInt16 position, Byte bit_position, Byte value)
		{
			// get the array
			//
			Byte[] cmd = new Byte[1];
			cmd[0] = value;

			// fins command
			//
			return finsMemoryAreadWrite(MemoryArea.CIO_Bit, position, bit_position, 1, cmd);
		}

		#endregion

        #region **** predefined WR commands

        /// <summary>
        /// reads an specifit bit of WR area
        /// </summary>
        /// <param name="position"></param>
        /// <param name="bit_position"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool ReadWRBit(UInt16 position, Byte bit_position, ref Byte value)
        {
            // FINS command
            //
            if (!finsMemoryAreadRead(MemoryArea.WR_Bit, position, bit_position, 1)) return false;

            // value
            //
            //value = BTool.BytesToUInt16(this._finsCmd.Response[0], this._finsCmd.Response[1]);
            value = this._finsCmd.Response[0];

            return true;
        }



        /// <summary>
        /// write one specific bit of CIO area
        /// </summary>
        /// <param name="position"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool WriteWRBit(UInt16 position, Byte bit_position, Byte value)
        {
            // get the array
            //
            Byte[] cmd = new Byte[1];
            cmd[0] = value;

            // fins command
            //
            return finsMemoryAreadWrite(MemoryArea.WR_Bit, position, bit_position, 1, cmd);
        }

        #endregion



		#region **** dialog

		/// <summary>
		/// return last dialog between PC & PLC
		/// </summary>
		/// <param name="Caption"></param>
		/// <returns></returns>
		public string LastDialog(string Caption)
		{
			return this.FinsCommand.LastDialog(Caption);
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

