using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Profiler.Data
{
	public class UniqueEvent : IComparable<UniqueEvent>
	{
		public EventDescription Description { get; set; }
		public double TotalTime { get; set; }
		public double MinTime { get; set; }
		public double MaxTime { get; set; }
		public uint Frames { get; set; }
		public Int64 Calls { get; set; }
		public String Name => Description.FullName;
		public virtual String FormattedValue { get; }
		public double AvgCall { get {return Calls > 0L ? TotalTime / Calls : 0;} }
		public double AvgFrame { get { return TotalTime / (Frames > 0L ? Frames : 1L); } }

		public int CompareTo(UniqueEvent other)
		{
			int result = TotalTime.CompareTo(other.TotalTime);
			return result == 0 ? Name.CompareTo(other.Name) : result;
		}

		public virtual bool Read(BinaryReader reader, EventDescriptionBoard board)
		{
			Calls = reader.ReadInt64();
			if (Calls == -1L)
				return false;
			MinTime = board.TimeSettings.TicksToMs * reader.ReadUInt64();
			MaxTime = board.TimeSettings.TicksToMs * reader.ReadUInt64();
			TotalTime = board.TimeSettings.TicksToMs * reader.ReadUInt64();
            uint descriptionID = Utils.ReadVlqUInt(reader);
			Description = (descriptionID < board.Board.Count) ? board.Board[(int)descriptionID] : null;
			Frames = Utils.ReadVlqUInt(reader);
			return true;
		}
	}

	public class UniqueEventsPack : IResponseHolder
	{
		public override DataResponse Response { get; set; }
		private FrameGroup Group { get; set; }

		List<UniqueEvent> uniqueEvents = new List<UniqueEvent>();
		public List<UniqueEvent> UniqueEvents { get { return uniqueEvents; } }

		bool IsLoaded { get; set; }

		public UniqueEventsPack(DataResponse response, FrameGroup group)
		{
			Response = response;
			Group = group;
			if (response != null)
			{
				Load();
			}
		}

		public UniqueEventsPack(List<UniqueEvent> t)
		{
			uniqueEvents = t;
		}

		void Load()
		{
			if (Response == null)
				return;

			lock (Response)
			{
				if (!IsLoaded)
				{
					uniqueEvents = new List<UniqueEvent>();
					BinaryReader reader = Response.Reader;

					LoadList();

					uniqueEvents.Sort();

					IsLoaded = true;
				}

			}
		}

		void LoadList()
		{
			BinaryReader reader = Response.Reader;

			while (true)
			{
				UniqueEvent val = new UniqueEvent();
				if (!val.Read(reader, Group.Board))
				  break;
				uniqueEvents.Add(val);
			}
		}
	}
}
