﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Media;

namespace Profiler.Data
{
	public class FileLine
	{
		public FileLine(String file, int line)
		{
			File = file;
			Line = line;

			if (!String.IsNullOrEmpty(File))
			{
				int index = File.LastIndexOfAny(new char[] { '\\', '/' });
				ShortName = index != -1 ? File.Substring(index + 1) : File;
				ShortPath = String.Format("{0}:{1}", ShortName, Line);
			}
		}

		public String File { get; private set; }
		public int Line { get; private set; }
		public String ShortName { get; private set; }
		public String ShortPath { get; private set; }

		public override string ToString()
		{
			return ShortPath;
		}

		public static FileLine Empty = new FileLine(String.Empty, 0);
	}

	public class EventDescription : Description
	{
		private int id;
		public int Id { get { return id; } }
		private Color forceColor;

		public Color Color { get; set; }
		public Color ForceColor
		{
			get
            {
                if (default != Color)
                    return Color;

                if (default == forceColor)
                    forceColor = GenerateRandomColor(FullName);

                return forceColor;
            }
        }
		public Brush Brush { get; private set; }

		public bool IsSleep { get { return (Flags&DescFlags.IS_WAIT) != 0; } }//Color == Colors.White

		public EventDescription() { }
		public EventDescription(String name, int id = -1)
		{
			FullName = name;
			this.id = id;
		}

		public enum DescFlags : byte
		{
			NONE = 0,
			IS_WAIT = 1 << 0,
		}

		public DescFlags Flags { get; private set; }

		public ThreadMask? Mask { get; set; }

		public override bool HasShortName { get { return true; } }

		public static Color GenerateRandomColor(String name, float variance = 0.0f)
		{
			return GenerateRandomColor(new Random(name.GetHashCode()), variance);
		}
		public static Color GenerateRandomColor(Random rnd, float variance = 0.0f)
		{
			Color color;
			do
			{
				color = Color.FromRgb((byte)rnd.Next(), (byte)rnd.Next(), (byte)rnd.Next());
			} while (Utils.GetLuminance(color) < Utils.LuminanceThreshold);

			if (variance > float.Epsilon)
			{
				byte shift = (byte)(new Random().Next() % (int)(variance * 255.0f));
				byte r = (byte)(color.R + Math.Min(255 - color.R, shift));
				byte g = (byte)(color.G + Math.Min(255 - color.G, shift));
				byte b = (byte)(color.B + Math.Min(255 - color.B, shift));
				return Color.FromRgb(r, g, b);
			}

			return color;
		}

		public void SetOverrideColor(Color color)
		{
			Color = color;
			Brush = new SolidColorBrush(color);
		}

		static public EventDescription Read(BinaryReader reader, int id, DescFlags flags)
		{
			EventDescription desc = new EventDescription();
			desc.FullName = Utils.ReadVlqString(reader);
			desc.id = id;
			desc.Flags = flags;

			String file = Utils.ReadVlqString(reader);
			desc.Path = new FileLine(file, reader.ReadInt32());
			UInt32 color = Utils.ReadVlqUInt(reader);
			desc.Color = Color.FromArgb((byte)(color >> 24),
										(byte)(color >> 16),
										(byte)(color >> 8),
										(byte)(color));

			desc.Brush = new SolidColorBrush(desc.Color);

			return desc;
		}

		public override Object GetSharedKey()
		{
			return this;
		}
	}

	public class FiberDescription
	{
		public UInt64 fiberID { get; set; }

		public static FiberDescription Read(DataResponse response)
		{
			BinaryReader reader = response.Reader;
			FiberDescription res = new FiberDescription();
			res.fiberID = reader.ReadUInt64();
			return res;
		}
	}

	public class ProcessDescription
	{
		public String Name { get; set; }
		public UInt32 ProcessID { get; set; }
		public UInt64 UniqueKey { get; set; }

		public static ProcessDescription Read(DataResponse response)
		{
			BinaryReader reader = response.Reader;
			ProcessDescription res = new ProcessDescription();
			res.ProcessID = reader.ReadUInt32();
			res.Name = Utils.ReadBinaryString(reader);
			res.UniqueKey = reader.ReadUInt64();
			return res;
		}
	}

	public enum ThreadMask
	{
		None,
		Main = 1 << 0,
		GPU = 1 << 1,
		IO = 1 << 2,
		Idle = 1 << 3,
		Render = 1 << 4,
	}

	public class ThreadDescription
	{
		public String Name { get; set; }
		public UInt64 ThreadID { get; set; }
		public UInt32 ProcessID { get; set; }
		public Int32 MaxDepth { get; set; }
		public Int32 Priority { get; set; }
		public Int32 Mask { get; set; }
		public ProcessDescription Process { get; set; }
		public int ThreadIndex { get; set; }

		public enum Source
		{
			Core,
			Game,
			GameAuto,
			Sampling,
		}
		public Source Origin { get; set; }

        public bool IsIdle { get { return (Mask & (int)ThreadMask.Idle) != 0; } }

		public String FullName
		{
			get
			{
				if (!String.IsNullOrEmpty(Name))
					return Name;

				if (Process != null)
					return String.Format(Process.Name);

				return String.Format("Pid 0x{0:X}", ProcessID);
			}
		}

		public const UInt64 InvalidThreadID = UInt64.MaxValue;

		public static ThreadDescription Read(DataResponse response)
		{
			BinaryReader reader = response.Reader;
			ThreadDescription res = new ThreadDescription();

			res.ThreadID = reader.ReadUInt64();
			res.ProcessID = reader.ReadUInt32();
			res.Name = Utils.ReadVlqString(reader);
			res.MaxDepth = reader.ReadInt32();
			res.Priority = reader.ReadInt32();
			res.Mask = reader.ReadInt32();
			return res;
		}
	}

	public class EventDescriptionBoard : IResponseHolder
	{
		public Stream BaseStream { get; private set; }
		public int ID { get; private set; }
		public Durable TimeSlice { get; set; }
		public int MainThreadIndex { get; set; } = -1;
		public TimeSettings TimeSettings { get; set; }
		public List<ThreadDescription> Threads { get; private set; } = new List<ThreadDescription>();
		public Dictionary<UInt64, int> ThreadID2ThreadIndex { get; private set; } = new Dictionary<ulong, int>();
		public List<FiberDescription> Fibers { get; private set; }
		public UInt32 Mode { get; set; }

		public Dictionary<UInt64, ThreadDescription> ThreadDescriptions { get; set; }
		public Dictionary<UInt32, ProcessDescription> ProcessDescritpions { get; set; }

		public int ProcessID { get; private set; }
		public int CPUCoreCount { get; set; }

		private List<EventDescription> board = new List<EventDescription>();
		public List<EventDescription> Board
		{
			get { return board; }
		}
		public EventDescriptionBoard()
		{
			ThreadDescriptions = new Dictionary<ulong, ThreadDescription>();
			ProcessDescritpions = new Dictionary<uint, ProcessDescription>();
		}

		public EventDescription this[int pos]
		{
			get
			{
				return board[pos];
			}
		}

		public static EventDescriptionBoard Read(DataResponse response)
		{
			BinaryReader reader = response.Reader;

			EventDescriptionBoard desc = new EventDescriptionBoard();
			desc.Response = response;
			desc.BaseStream = reader.BaseStream;
			desc.ID = reader.ReadInt32();

			desc.TimeSettings = new TimeSettings();
			desc.TimeSettings.TicksToMs = 1000.0 / (double)reader.ReadInt64();
			desc.TimeSettings.Origin = reader.ReadInt64();
			desc.TimeSettings.PrecisionCut = reader.ReadInt32();
			Durable.InitSettings(desc.TimeSettings);

			desc.TimeSlice = new Durable();
			desc.TimeSlice.ReadDurable(reader);

			int threadCount = reader.ReadInt32();
			desc.Threads = new List<ThreadDescription>(threadCount);
			desc.ThreadID2ThreadIndex = new Dictionary<UInt64, int>();

			for (int i = 0; i < threadCount; ++i)
			{
				ThreadDescription threadDesc = ThreadDescription.Read(response);
				threadDesc.Origin = ThreadDescription.Source.Game;
				threadDesc.ThreadIndex = i;
				desc.Threads.Add(threadDesc);

				if (!desc.ThreadID2ThreadIndex.ContainsKey(threadDesc.ThreadID))
				{
					desc.ThreadID2ThreadIndex.Add(threadDesc.ThreadID, i);
					desc.ThreadDescriptions.Add(threadDesc.ThreadID, threadDesc);
				}
				else
				{
					// The old thread was finished and the new thread was started 
					// with the same threadID during one profiling session.
					// Can't do much here - lets show information for the new thread only.
					desc.ThreadID2ThreadIndex[threadDesc.ThreadID] = i;
				}
			}

			if (response.ApplicationID == NetworkProtocol.OPTICK_APP_ID)
			{
				int fibersCount = reader.ReadInt32();
				desc.Fibers = new List<FiberDescription>(fibersCount);
				for (int i = 0; i < fibersCount; ++i)
				{
					FiberDescription fiberDesc = FiberDescription.Read(response);
					desc.Fibers.Add(fiberDesc);
				}
			}

			desc.MainThreadIndex = reader.ReadInt32();

            for (int i = 0; ; i++)
			{
				byte b = reader.ReadByte();
				if (b == 0xFF)
					break;
				desc.board.Add(EventDescription.Read(reader, i, (EventDescription.DescFlags)b));
			}


			// TODO: Mode
			desc.Mode = reader.ReadUInt32();

			// TODO: Thread Descriptions
			int processDescCount = reader.ReadInt32();
			for (int i = 0; i < processDescCount; ++i)
			{
				ProcessDescription process = ProcessDescription.Read(response);
				if (!desc.ProcessDescritpions.ContainsKey(process.ProcessID))
					desc.ProcessDescritpions.Add(process.ProcessID, process);
			}

			int threadDescCount = reader.ReadInt32();
			for (int i = 0; i < threadDescCount; ++i)
			{
				ThreadDescription thread = ThreadDescription.Read(response);
				thread.Origin = ThreadDescription.Source.GameAuto;
				if (!desc.ThreadDescriptions.ContainsKey(thread.ThreadID))
					desc.ThreadDescriptions.Add(thread.ThreadID, thread);
				//else if (!String.IsNullOrEmpty(thread.Name))
				//	desc.ThreadDescriptions[thread.ThreadID] = thread;

				ProcessDescription process = null;
				if (desc.ProcessDescritpions.TryGetValue(thread.ProcessID, out process))
					thread.Process = process;
			}

			desc.ProcessID = response.Reader.ReadInt32();
			desc.CPUCoreCount = response.Reader.ReadInt32();

			return desc;
		}

		public override DataResponse Response { get; set; }
	}

	public class Entry : EventData, IComparable<Entry>
	{
		public EventDescription Description { get; private set; }
		public int Depth { get; private set; }
		public EventFrame Frame { get; set; }

		protected Entry() { }

		public Entry(EventDescription desc, long start, long finish, int depth) : base(start, finish)
		{
			this.Description = desc;
			this.Depth = depth;
		}

		public void SetOverrideColor(Color color)
		{
			Description.SetOverrideColor(color);
		}

		protected void ReadEntry(BinaryReader reader, EventDescriptionBoard board)
		{
			ReadEventData(reader);
			uint index = Utils.ReadVlqUInt(reader);
			Depth = (int)Utils.ReadVlqUInt(reader);
			Description = (index < board.Board.Count) ? board[(int)index] : null;
		}

		public static Entry Read(BinaryReader reader, EventDescriptionBoard board)
		{
			Entry res = new Entry();
			res.ReadEntry(reader, board);
			return res;
		}

		public static Entry TryReadStoppable(BinaryReader reader, EventDescriptionBoard board)
		{
			int depth = Utils.ReadVlqInt(reader);
			if (depth < 0)
				return null;
			uint index = Utils.ReadVlqUInt(reader);
			Entry res = new Entry();
			res.Depth = depth;
			res.Description = (index < board.Board.Count) ? board[(int)index] : null;
			res.ReadEventData(reader);
			return res;
		}

		private double work_ = -1;
		public double CalculateWork()
		{
			if (work_ < 0)
				work_ = DoCalculateWork();
			return work_;
		}

		public double DoCalculateWork()
		{
			return Description.IsSleep ? 0 : (Frame == null ? Duration : Frame.CalculateWork(this));
		}

		public int CompareTo(Entry other)
		{
			if (other.Start != Start)
				return Start < other.Start ? -1 : 1;
			else
				return Finish == other.Finish ? 0 : Finish > other.Finish ? -1 : 1;
		}
	}

	public class FrameData : Entry
	{
		public UInt64 ThreadID { get; set; }
		public FrameData(UInt64 threadID, EventDescription desc, long start, long finish) : base(desc, start, finish, -1){}
	}
}
