using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Profiler.Data
{
	public class FileAttachment
	{
		public enum Type
		{
			IMAGE,
			TEXT,
			OTHER,
			CAPTURE,
		}
		public Type FileType { get; set; }
		public String Name { get; set; }
		public Stream Data { get; set; }

		public override string ToString()
		{
			return Name;
		}
	}

	public class SummaryPack : IResponseHolder
	{
		public override DataResponse Response { get; set; }

		public class Item
		{
			public String Name { get; set; }
			public String Value { get; set; }
		}

		public List<double> Frames { get; set; }
		public List<Item> SummaryTable { get; set; }
		public List<FileAttachment> Attachments { get; set; }

		public int BoardID { get; set; }

		public static SummaryPack Create(String path)
		{
			if (File.Exists(path))
			{
				using (Stream stream = Capture.Create(path))
				{
					DataResponse response = DataResponse.Create(stream);
					if (response != null)
					{
						if (response.ResponseType == DataResponse.Type.SummaryPack)
						{
							return new SummaryPack(response);
						}
					}
				}
			}

			return null;
		}

		public SummaryPack(DataResponse response)
		{
			Response = response;

			BoardID = response.Reader.ReadInt32();

			int frameCount = response.Reader.ReadInt32();
			Frames = new List<double>(frameCount);
			for (int i = 0; i < frameCount; ++i)
				Frames.Add(response.Reader.ReadSingle());

			SummaryTable = new List<Item>();
			while(true)
			{
				String Name = Utils.ReadVlqString(response.Reader);
				if (Name.Length == 0)
					break;
				SummaryTable.Add(new Item() { Name = Name, Value = Utils.ReadVlqString(response.Reader) });
			}

			int attachmentCount = response.Reader.ReadInt32();
			Attachments = new List<FileAttachment>(attachmentCount);
			for (int i = 0; i < attachmentCount; ++i)
			{
				FileAttachment attachment = new FileAttachment()
				{
					FileType = (FileAttachment.Type)response.Reader.ReadInt32(),
					Name = Utils.ReadBinaryString(response.Reader)
				};

				int fileSize = response.Reader.ReadInt32();
				attachment.Data = new MemoryStream(response.Reader.ReadBytes(fileSize));

				Attachments.Add(attachment);
			}
		}
	}
}
