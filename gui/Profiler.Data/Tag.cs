using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Profiler.Data
{
	public struct Vec3
	{
		public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }
	}

	public struct DrawStat
	{
		public UInt64 tri { get; set; }
		public uint   locks { get; set; }
		public uint   dip { get; set; }
		public uint   rt { get; set; }
		public uint   prog { get; set; }
		public uint   instances { get; set; }
		public uint   render_passes { get; set; }
	}

	public class Tag : ITick, IComparable<Tag>
	{
		public EventDescription Description { get; set; }
		public Tick Time { get; set; }
		public String Name => Description.FullName;
		public virtual String FormattedValue { get; }

		public long Start => Time.Start;

		public int CompareTo(Tag other)
		{
			int result = Start.CompareTo(other.Start);
			return result == 0 ? Name.CompareTo(other.Name) : result;
		}

		public virtual bool Read(BinaryReader reader, EventDescriptionBoard board)
		{
			Time = new Tick { Start = Durable.ReadTime(reader) };
			if (Start == -1L)
				return false;
			uint descriptionID = Utils.ReadVlqUInt(reader);
			Description = (descriptionID < board.Board.Count) ? board.Board[(int)descriptionID] : null;
			return true;
		}
	}

	public class TagInt32 : Tag
	{
		public Int32 Value { get; set; }
		public override bool Read(BinaryReader reader, EventDescriptionBoard board)
		{
			if (!base.Read(reader, board))
				return false;
			Value = reader.ReadInt32();
			return true;
		}

		public override String FormattedValue => Value.ToString("N0").Replace(',', ' ');
	}

	public class TagString : Tag
	{
		public String Value { get; set; }
		public override bool Read(BinaryReader reader, EventDescriptionBoard board)
		{
			if (!base.Read(reader, board))
				return false;
			int len = (int)Utils.ReadVlqUInt(reader);
			var bytes = reader.ReadBytes(len);
			int strLen = 0;
			for (; strLen < len; ++strLen)
				if (bytes[strLen] == 0)
					break;
			int argsStart = strLen+1;
			int argsCount = (len - argsStart)/4;
			String st = "";
			for (int i = 0; i < strLen; ++i)
			{
				if (bytes[i] == '%' && i < strLen - 1 && argsCount > 0 )
				{
					switch(bytes[i+1])
					{
						case (byte)'d': st += $"{BitConverter.ToInt32 (bytes, argsStart)}"; argsStart += 4; argsCount--; i++; break;
						case (byte)'u': st += $"{BitConverter.ToUInt32(bytes, argsStart)}"; argsStart += 4; argsCount--; i++; break;
						case (byte)'X': case (byte)'x':
							st += $"{BitConverter.ToUInt32(bytes, argsStart):X}"; argsStart += 4; argsCount--; i++; break;
						case (byte)'f': case (byte)'g':
							st += String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", BitConverter.ToSingle(bytes, argsStart)); argsStart += 4; argsCount--; i++; break;
						case (byte)'%': st += "%"; i++; break;
						default: st += (char)bytes[i]; break;
					}
				} else
					st += (char)bytes[i];
			}  
			Value = st;
			return true;
		}

		public override String FormattedValue => Value;
	}

	public class TagDrawStat : Tag
	{
		public DrawStat Value { get; set; }
		public override bool Read(BinaryReader reader, EventDescriptionBoard board)
		{
			base.Time = new Tick { Start = Durable.ReadTime(reader) };
			if (Start == -1L)
 				return false;
			base.Description = new EventDescription("ds");
			Value = new DrawStat { locks = Utils.ReadVlqUInt(reader), dip = Utils.ReadVlqUInt(reader), rt = Utils.ReadVlqUInt(reader), prog = Utils.ReadVlqUInt(reader), instances = Utils.ReadVlqUInt(reader), render_passes = Utils.ReadVlqUInt(reader), tri = reader.ReadUInt64() };
			return true;
		}

		public override String FormattedValue => $"(tris={Value.tri}, locks={Value.locks}, dip={Value.dip}, rt_change={Value.rt}, prog_change={Value.prog}, instances={Value.instances}, render_passes={Value.render_passes})";
	}



	public class TagsPack : IResponseHolder
	{
		public override DataResponse Response { get; set; }
		private FrameGroup Group { get; set; }
		public int ThreadIndex { get; private set; } = -1;
		public int CoreIndex { get; set; } = -1;

		List<Tag> tags = new List<Tag>();
		public List<Tag> Tags { get { return tags; } }

		bool IsLoaded { get; set; }

		public TagsPack(DataResponse response, FrameGroup group)
		{
			Response = response;
			Group = group;
			if (response != null)
			{
				ThreadIndex = response.Reader.ReadInt32();
				Load();
			}
		}

		public TagsPack(List<Tag> t)
		{
			tags = t;
		}

		void Load()
		{
			if (Response == null)
				return;

			lock (Response)
			{
				if (!IsLoaded)
				{
					tags = new List<Tag>();
					BinaryReader reader = Response.Reader;

					LoadTags<TagInt32>();
					LoadTags<TagDrawStat>();
					LoadTags<TagString>();

					tags.Sort();

					IsLoaded = true;
				}

			}
		}

		void LoadTags<T>() where T : Tag, new()
		{
			BinaryReader reader = Response.Reader;

			while (true)
			{
				T val = new T();
				if (!val.Read(reader, Group.Board))
				  break;
				tags.Add(val);
			}
		}
	}
}
