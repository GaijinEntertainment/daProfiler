using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Profiler.Data;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.IO.Compression;

namespace Profiler
{
	public class ProfilerClient
	{
		private Object criticalSection = new Object();
		private static ProfilerClient profilerClient = new ProfilerClient();

		ProfilerClient()
		{

		}

		private void Reconnect(bool reuse_socket = true)
		{
			if (zlibStream != null)
				reuse_socket = false;
			if (client.Client.Connected)
				client.Client.Disconnect(reuse_socket);

			client.Close();
			zlibStream = null;
			client = new TcpClient();
		}

		public IPAddress IpAddress
		{
			get { return ipAddress; }
			set
			{
				if (!value.Equals(ipAddress))
				{
					ipAddress = value;
					Reconnect();
				}
			}
		}

		public UInt16 Port
		{
			get { return port; }
			set
			{
				if (port != value)
				{
					port = value;
					Reconnect();
				}
			}
		}

		public static ProfilerClient Get() { return profilerClient; }

		TcpClient client = new TcpClient();
        System.IO.Compression.DeflateStream zlibStream;
		#region SocketWork

		public DataResponse RecieveMessage()
		{
			try
			{
				Stream stream = null;

				lock (criticalSection)
				{
					if (!client.Connected)
					{
						zlibStream = null;
                        return null;
					}

					stream = zlibStream != null ? (Stream)zlibStream : (Stream)client.GetStream();
				}

				DataResponse response = DataResponse.Create(stream, IpAddress, Port);

                if (response != null && response.ResponseType == DataResponse.Type.Handshake)
                    ReceiveHandshakeResponse(response);
				return response;
			}
			catch (System.IO.IOException ex)
			{
				lock (criticalSection)
				{
					if (clientStatus == ClientStatus.Active)//if we asked for disconnect, it is normal to not be able to finish 
					{
						Application.Current.Dispatcher.BeginInvoke(new Action(() =>
						{
							ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, ex.Message);
						}));

					}

					Reconnect();
				}
			}

			return null;
		}

		private IPAddress ipAddress;
		private UInt16 port = UInt16.MaxValue;

		const UInt16 PORT_RANGE = 4;
		public bool IsConnected()
		{
			lock (criticalSection)
			{
				return client.Connected;
			}
		}
		public bool IsCompressedInputNetwork()
		{
			lock (criticalSection)
			{
				return zlibStream != null;
			}
		}
		private bool CheckConnection()
		{
			if (!client.Connected)
			{
				zlibStream = null;
				for (UInt16 currentPort = port; currentPort < port + PORT_RANGE; ++currentPort)
				{
					try
					{
						Application.Current.Dispatcher.BeginInvoke(new Action(() =>
						{
							ConnectionChanged?.Invoke(IpAddress, currentPort, State.Connecting, String.Empty);
						}));
						var task = client.ConnectAsync(ipAddress, currentPort);
						var waited = task.Wait(TimeSpan.FromSeconds(2));
						if (client.Connected)
						{
							Application.Current.Dispatcher.BeginInvoke(new Action(() =>
							{
								ConnectionChanged?.Invoke(ipAddress, currentPort, State.Connected, String.Empty);
							}));
							clientStatus = ClientStatus.Active;
							return true;
						}
						else
						{
							zlibStream = null;
							client = new TcpClient();
						}
					}
					catch (SocketException ex)
					{
						Debug.Print(ex.Message);
					}
				}
				Application.Current.Dispatcher.BeginInvoke(new Action(() =>
				{
					ConnectionChanged?.Invoke(ipAddress, port, State.Disconnected, "Can't connect");
				}));
			}
			return false;
		}

		public enum State
		{
			Connecting,
			Connected,
			Disconnected,
		}
		public delegate void ConnectionStateEventHandler(IPAddress address, UInt16 port, State state, String message);
		public event ConnectionStateEventHandler ConnectionChanged;

		public bool ReceiveHandshakeResponse(DataResponse response)
		{
			if (response == null || response.ResponseType != DataResponse.Type.Handshake)
				return false;
			lock (criticalSection)
			{
				response.Reader.ReadUInt16();//skip
				uint compressionAlgo = response.Reader.ReadUInt16();

				if ((compressionAlgo & (UInt16)CompressionAlgorithm.ZLIB_ALGO) != 0)
				{
					client.GetStream().ReadByte();
					client.GetStream().ReadByte();
					zlibStream = new System.IO.Compression.DeflateStream(client.GetStream(), CompressionMode.Decompress);
				}
				response.Reader.BaseStream.Seek(0, SeekOrigin.Begin);
			}
			return true;
		}

		public bool SendMessage(Message message, bool autoconnect = false)
		{
			try
			{
				lock (criticalSection)
				{
					if (!client.Connected && !autoconnect)
						return false;
					CheckConnection();
					if (!client.Connected)
						return false;
					MemoryStream buffer = new MemoryStream();
					message.Write(new BinaryWriter(buffer));
					buffer.Flush();

					UInt32 length = (UInt32)buffer.Length;

					NetworkStream stream = client.GetStream();

					BinaryWriter writer = new BinaryWriter(stream);
					writer.Write(Message.MESSAGE_MARK);
					writer.Write(length);

					buffer.WriteTo(stream);
					stream.Flush();
				}

				return true;
			}
			catch (Exception ex)
			{
				lock (criticalSection)
				{
					if (clientStatus == ClientStatus.Active)//if we asked for disconnect, it is normal to not be able to finish 
					{
						Application.Current.Dispatcher.BeginInvoke(new Action(() =>
						{
							ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, ex.Message);
						}));
					}

					Reconnect();
				}
			}

			return false;
		}
		enum ClientStatus { Closing, Closed, Active};
		ClientStatus clientStatus = ClientStatus.Closed;
		public void Close()
		{
			lock (criticalSection)
			{
				clientStatus = ClientStatus.Closing;
				SendMessage(new DisconnectMessage());
				Reconnect(false);
				clientStatus = ClientStatus.Closed;
				//ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, String.Empty);
				Application.Current.Dispatcher.BeginInvoke(new Action(() =>
				{
					ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, String.Empty);
				}));
			}
		}

		#endregion
	}
}
