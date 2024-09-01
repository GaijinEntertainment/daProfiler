using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows;
using System.Net;
using System.Net.Sockets;
using Profiler.Data;
using System.Security;

namespace Profiler.Data
{
	public struct NetworkProtocol
	{
		public const UInt32 NETWORK_PROTOCOL_VERSION_1 = 1; // Bumped version

		public const UInt32 NETWORK_PROTOCOL_VERSION = NETWORK_PROTOCOL_VERSION_1;
		public const UInt32 NETWORK_PROTOCOL_MIN_VERSION = NETWORK_PROTOCOL_VERSION_1;

		public const UInt16 OPTICK_APP_ID = 0xC50F;
	}

	public class DataResponse
	{
		public enum Type
		{
			FrameDescriptionBoard,
			EventFrame,
			SamplingFrame,
			NullFrame,
			ReportProgress,
			Handshake,
			UniqueName,
			SynchronizationData,
			TagsPack,
			CallstackDescriptionBoard,
			CallstackPack,
			SettingsPack,
			Heartbeat,
			ReportLiveFrameTime,
			Plugins,
			UniqueEvents,

			FiberSynchronizationData = 1 << 8,
			SyscallPack,
			SummaryPack,
			FramesPack,
		}
		public UInt16 ApplicationID { get; set; }
		public Type ResponseType { get; set; }
		public UInt32 Version { get; set; }
		public BinaryReader Reader { get; set; }

		public struct ConnectionSource
		{
			public IPAddress Address { get; set; }
			public UInt16 Port { get; set; }
		}
		public ConnectionSource Source;

		public DataResponse(UInt16 appID, Type type, UInt32 version, BinaryReader reader)
		{
			ApplicationID = appID;
			ResponseType = type;
			Version = version;
			Reader = reader;
		}

		public DataResponse(Type type, Stream stream)
		{
			ResponseType = type;
			Version = NetworkProtocol.NETWORK_PROTOCOL_VERSION;
			Reader = new BinaryReader(stream);
		}

		public String SerializeToBase64()
		{
			MemoryStream stream = new MemoryStream();
			Serialize(ApplicationID, ResponseType, Reader.BaseStream, stream);
			stream.Position = 0;

			byte[] data = new byte[stream.Length];
			stream.Read(data, 0, (int)stream.Length);
			return Convert.ToBase64String(data);
		}

		public static void Serialize(UInt16 appID, DataResponse.Type type, Stream data, Stream result)
		{
			BinaryWriter writer = new BinaryWriter(result);
			writer.Write(NetworkProtocol.NETWORK_PROTOCOL_VERSION);
			writer.Write((UInt32)data.Length);
			writer.Write((UInt16)type);
			writer.Write((UInt16)appID);

			long position = data.Position;
			data.Seek(0, SeekOrigin.Begin);
			data.CopyTo(result);
			data.Seek(position, SeekOrigin.Begin);
		}

		public void Serialize(Stream result)
		{
			BinaryWriter writer = new BinaryWriter(result);
			writer.Write((UInt32)Version);
			writer.Write((UInt32)Reader.BaseStream.Length);
			writer.Write((UInt16)ResponseType);
			writer.Write((UInt16)ApplicationID);

			long position = Reader.BaseStream.Position;
			Reader.BaseStream.Seek(0, SeekOrigin.Begin);
			Reader.BaseStream.CopyTo(result);
			Reader.BaseStream.Seek(position, SeekOrigin.Begin);
		}

		public static DataResponse Create(Stream stream)
		{
			if (stream == null || !stream.CanRead)
				return null;

			var reader = new BinaryReader(stream);

			try
			{
				uint version = reader.ReadUInt32();
				uint length = reader.ReadUInt32();
				UInt16 responseType = reader.ReadUInt16();
				UInt16 applicationId = reader.ReadUInt16();
				if (applicationId != NetworkProtocol.OPTICK_APP_ID)
					return null;
				byte[] bytes = reader.ReadBytes((int)length);

				return new DataResponse(applicationId, (DataResponse.Type)responseType, version, new BinaryReader(new MemoryStream(bytes)));
			}
			catch (EndOfStreamException) { }

			return null;
		}

		public static DataResponse Create(String base64)
		{
			MemoryStream stream = new MemoryStream(Convert.FromBase64String(base64));
			return DataResponse.Create(stream);
		}

		public static DataResponse Create(Stream stream, IPAddress ipAddress, UInt16 port)
		{
			DataResponse response = Create(stream);
			if (response != null)
			{
				response.Source.Address = ipAddress;
				response.Source.Port = port;
			}
			return response;
		}
	}

	public enum MessageType
	{
		Connected,
		Disconnect,
		SetSettings,
		StartInfiniteCapture,
		StopInfiniteCapture,
		CancelInfiniteCapture,
		Capture,
		TurnSampling,
		CancelProfiling,
		Heartbeat,
		PluginCommand,
		ConnectedCompression,
		COUNT
	}

	public abstract class Message
	{
		public static UInt32 MESSAGE_MARK = 0xC50FC50F;

		public abstract Int16 GetMessageType();
		public virtual void Write(BinaryWriter writer)
		{
			writer.Write(GetMessageType());
		}
	}

	public class ConnectMessage : Message
	{
		public override Int16 GetMessageType()
		{
			return (Int32)MessageType.Connected;
		}
	}
	public class ConnectedCompressionMessage : Message
	{
		public override Int16 GetMessageType()
		{
			return (Int32)MessageType.ConnectedCompression;
		}
		public override void Write(BinaryWriter writer)
		{
			base.Write(writer);
			writer.Write((UInt16)Profiler.Data.CompressionAlgorithm.ZLIB_ALGO);

		}
	}
	public class HeartbeatMessage : Message
	{
		public override Int16 GetMessageType()
		{
			return (Int32)MessageType.Heartbeat;
		}
	}
	public class PluginCommandMessage : Message
	{
		public Dictionary<String, bool> pluginCommands;
		public override Int16 GetMessageType()
		{
			return (Int32)MessageType.PluginCommand;
		}
		public override void Write(BinaryWriter writer)
		{
			base.Write(writer);
			writer.Write(pluginCommands.Count);
			foreach (KeyValuePair<String,bool> kv in pluginCommands)
			{
				Utils.WriteBinaryString(writer, kv.Key);
				writer.Write(kv.Value);
			}

		}
	}

	public class DisconnectMessage : Message
	{
		public override Int16 GetMessageType()
		{
			return (Int32)MessageType.Disconnect;
		}
	}

	public class SetSettingsMessage : Message
	{
		public CaptureSettings Settings {get;set;}
		public SetSettingsMessage() { }

		public override Int16 GetMessageType()
		{
			return (Int32)MessageType.SetSettings;
		}

		public override void Write(BinaryWriter writer)
		{
			base.Write(writer);
            Settings.Write(writer);
		}
	}


	public class StartCaptureMessage : Message
	{
        public StartCaptureMessage()
		{
		}

		public override Int16 GetMessageType()
		{
			return (Int32)MessageType.StartInfiniteCapture;
		}

		public override void Write(BinaryWriter writer)
		{
			base.Write(writer);
		}
	}

	public class StopMessage : Message
	{
		public override Int16 GetMessageType()
		{
			return (Int16)MessageType.StopInfiniteCapture;
		}
	}

	public class CancelInfiniteMessage: Message
	{
		public override Int16 GetMessageType()
		{
			return (Int16)MessageType.CancelInfiniteCapture;
		}
	}

	public class GetCaptureMessage : Message
	{
		public override Int16 GetMessageType()
		{
			return (Int16)MessageType.Capture;
		}
	}

	public class TurnSamplingMessage : Message
	{
		bool isActive;

		public TurnSamplingMessage(bool isActive)
		{
			this.isActive = isActive;
		}

		public override Int16 GetMessageType()
		{
			return (Int16)MessageType.TurnSampling;
		}

		public override void Write(BinaryWriter writer)
		{
			base.Write(writer);
			writer.Write(isActive);
		}
	}
}
