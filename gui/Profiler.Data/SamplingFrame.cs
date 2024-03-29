﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections.ObjectModel;

namespace Profiler.Data
{

	class SamplingSharedKey
	{
		public SamplingDescription Description { get; private set; }

		public override int GetHashCode()
		{
			return Description.Name.GetHashCode() ^ (Description.Path != null ? Description.Path.File.GetHashCode() : 0);
		}

		public SamplingSharedKey(SamplingDescription desc)
		{
			Description = desc;
		}

		public override bool Equals(Object obj)
		{
			if (obj is SamplingSharedKey)
			{
				SamplingSharedKey other = obj as SamplingSharedKey;
				return Description.Name == other.Description.Name && Description.Path.File == other.Description.Path.File;
			}
			return false;
		}
	}

	public class SamplingDescription : Description
	{
		public UInt64 Address { get; private set; }
		public Module Module { get; set; }

		public String ModuleShortName
		{
			get { return Module != null ? Module.Name : "Unresolved"; }
		}

		static HashSet<String> IgnoreList = new HashSet<string>(new String[] {
			"setjmpex",
			"RtlUserThreadStart",
			"(__cdecl*)",
			"BaseThreadInitThunk",
			"__scrt_common_main_seh",
			"invoke_main",
			"wmainCRTStartup",
			"__scrt_common_main"
		});
		private bool? isIgnore = null;
		public bool IsIgnore
		{
			get
			{
				if (Path == FileLine.Empty)
					return false;
				if (isIgnore == null)
					isIgnore = IgnoreList.Contains(Name);

				return (bool)isIgnore;
			}
		}
		public bool IsResolved
		{
			get { return Path != FileLine.Empty; }
		}

		public override bool HasShortName => true;

		public static SamplingDescription UnresolvedDescription = new SamplingDescription() { FullName = "Unresolved", Address = 0, Path = FileLine.Empty };

		public void Read(BinaryReader reader, uint version)
		{
			int line = reader.ReadInt32();
			if (line < 0)//unresolved
				return;
			FullName = Utils.ReadVlqString(reader);
			Path = new FileLine(Utils.ReadVlqString(reader), line);
		}

		public static SamplingDescription Create(UInt64 address)
		{
			return new SamplingDescription() { FullName = String.Format("0x{0:x}", address), Address = address, Path = FileLine.Empty };
		}

		public override Object GetSharedKey()
		{
			return new SamplingSharedKey(this);
		}
	}

	public abstract class ISamplingBoard
	{
		public abstract SamplingDescription GetDescription(UInt64 address);
	}


	public class Module : IComparable<Module>
	{
		private String path = String.Empty;
		public String Path
		{
			get { return path; }
			set {
				path = value;
				try
				{
					Name = System.IO.Path.GetFileName(value);
				}
				catch (System.ArgumentException ex)
				{
					Name = ex.Message;
				}
			}
		}
		public String Name { get; private set; }
		public UInt64 Address { get; set; }
		public UInt64 Size { get; set; }

		public int CompareTo(Module other)
		{
			return Address.CompareTo(other.Address);
		}

		public bool Contains(UInt64 addr)
		{
			return (Address <= addr) && (addr < Address + Size);
		}
	}

	public class SamplingDescriptionBoard : ISamplingBoard
	{
		public List<Module> Modules { get; private set; }
		public Dictionary<UInt64, SamplingDescription> Descriptions { get; private set; }
		public SamplingDescriptionBoard()
        {
			Modules = new List<Module>(8);
			Descriptions = new Dictionary<UInt64, SamplingDescription>(1024);
		}

		public void Read(DataResponse response)
		{
			BinaryReader reader = response.Reader;

			while (true)
			{
				ulong address = reader.ReadUInt64();
				if (address == 0ul)
					break;
				Module module = new Module() { Address = address, Size = reader.ReadUInt64(), Path = Utils.ReadVlqString(reader) };
				Modules.Add(module);
			}
			if (Modules.Count != 0)
				Modules.Sort();

			while (true)
			{
				ulong address = reader.ReadUInt64();
				if (address == 0ul)
					break;
				SamplingDescription desc = GetDescription(address);
				desc.Read(reader, response.Version);
				if (desc.Module == null)
					desc.Module = GetModule(address);
			}
		}

		public override SamplingDescription GetDescription(ulong address)
		{
			SamplingDescription result = null;
			if (!Descriptions.TryGetValue(address, out result))
			{
				result = SamplingDescription.Create(address);
				result.Module = GetModule(address);
				Descriptions[address] = result;
			}
			return result;
		}

		Module GetModule(UInt64 address)
		{
			int index = Utils.BinarySearchIndex(Modules, address, m => m.Address);
			if (index >= 0 && Modules[index].Contains(address))
				return Modules[index];
			return null;
		}
	}

	public class DummySamplingBoard : ISamplingBoard
	{
		public override SamplingDescription GetDescription(ulong address)
		{
			return SamplingDescription.Create(address);
		}

		public static DummySamplingBoard Instance = new DummySamplingBoard();
	}

	public class SamplingDescriptionPack : SamplingDescriptionBoard
	{
        public SamplingDescriptionPack() { }
		public static SamplingDescriptionPack CreatePack(SamplingDescriptionPack cPack, DataResponse response)
		{
		    if (cPack == null)
				cPack = new SamplingDescriptionPack();
			cPack.Read(response);
			return cPack;
		}
	}

	public class SamplingNode : TreeNode<SamplingDescription>
	{
		public String NameWithModule { get { return String.Format("{0} [{1}]", Name, Description.ModuleShortName); } }

		private List<BaseTreeNode> shadowNodes = new List<BaseTreeNode>();
		public override List<BaseTreeNode> ShadowNodes { get { return shadowNodes; } }

		// Participated in sampling process
		public double Passed { get; private set; }

		// Stopped at this function
		public double Sampled
		{
			get
			{
				double total = Passed;
				foreach (SamplingNode child in Children)
				{
					total -= child.Passed;
				}
				return total;
			}
		}

		SamplingNode(SamplingNode root, SamplingDescription desc, double passed)
			: base(root, desc, passed)
		{
			Passed = passed;
		}

		public static SamplingNode Create(BinaryReader reader, SamplingDescriptionBoard board, SamplingNode root)
		{
			UInt64 address = reader.ReadUInt64();

			SamplingDescription desc = null;
			if (!board.Descriptions.TryGetValue(address, out desc))
				desc = SamplingDescription.UnresolvedDescription;

			UInt32 passed = reader.ReadUInt32();

			SamplingNode node = new SamplingNode(root, desc, passed);

			UInt32 childrenCount = reader.ReadUInt32();
			for (UInt32 i = 0; i < childrenCount; ++i)
				node.AddChild(SamplingNode.Create(reader, board, root != null ? root : node));

			return node;
		}


		bool IsSimilar(SamplingDescription a, SamplingDescription b)
		{
			return a.FullName == b.FullName; //(a.Address == b.Address || (a.Path.Line == b.Path.Line && a.Name == b.Name);
		}

		void AppendMerge(Callstack callstack, int index, SamplingNode root)
		{
			//ignore preambles
			int lastIsIgnore = -1;
			for (int i = 0; i < callstack.Count; ++i)
			{
				if (callstack[i].IsIgnore)
					lastIsIgnore = i;
			}
			index = Math.Max(index, lastIsIgnore+1);

			//do not show unresolved tail
			int firstResolved = callstack.Count-1;
			for (; firstResolved > 0; --firstResolved)
			{
				if (callstack[firstResolved].IsResolved)
					break;
			}
			int lastIndex = Math.Min(callstack.Count-1, firstResolved+1);
			if (index > lastIndex)
				return;

			SamplingDescription desc = callstack[index];
			SamplingNode rootNode = root == null ? this : root;

			foreach (SamplingNode node in Children)
			{
				if (IsSimilar(node.Description, desc))
				{
					node.shadowNodes.Add(new SamplingNode(rootNode, desc, 1) { Parent = this, ChildrenDuration = index == lastIndex ? 0 : 1 });
					++node.Passed;
					node.AppendMerge(callstack, index + 1, rootNode);
					return;
				}
			}

			SamplingNode child = new SamplingNode(rootNode, desc, 1);
			child.shadowNodes.Add(new SamplingNode(rootNode, desc, 1) { Parent = child, ChildrenDuration = index == lastIndex ? 0 : 1 });
			AddChild(child);
			child.AppendMerge(callstack, index + 1, rootNode);
		}

		void Update(double mul)
		{
			ForEach((node, level) =>
			{
				SamplingNode sNode = (node as SamplingNode);
				sNode.Duration = sNode.Passed * mul;

				double passedChildren = 0;
				sNode.Children.ForEach(child => passedChildren += (child as SamplingNode).Passed);
				sNode.ChildrenDuration = passedChildren * mul;

				return true;
			});
		}

		public static SamplingNode Create(List<Callstack> callstacks, double totalDuration)
		{
			SamplingNode node = new SamplingNode(null, null, totalDuration);
			callstacks.ForEach(c => node.AppendMerge(c, 0, node));
			node.Update(totalDuration/callstacks.Count);
			node.Sort();
			return node;
		}
	}

	public class SamplingFrame : Frame
	{
		public override DataResponse.Type ResponseType { get { return DataResponse.Type.SamplingFrame; } }

		public SamplingDescriptionBoard DescriptionBoard { get; private set; }

		private Board<SamplingBoardItem, SamplingDescription, SamplingNode> board;
		public Board<SamplingBoardItem, SamplingDescription, SamplingNode> Board
		{
			get
			{
				Load();
				return board;
			}
		}

		private const int MAX_ROOT_SKIP_DEPTH = 3;
		private SamplingNode root = null;
		public SamplingNode Root
		{
			get
			{
				Load();

				SamplingNode result = root;
				int depth = 0;

				while (depth < MAX_ROOT_SKIP_DEPTH && result.Children.Count == 1 && (result.Children[0] as SamplingNode).Sampled == 0)
				{
					++depth;
					result = result.Children[0] as SamplingNode;
				}

				return result;
			}

			set
			{
				root = value;
				root.CalculateRecursiveExcludeFlag(new Dictionary<Object, int>());
				board = new Board<SamplingBoardItem, SamplingDescription, SamplingNode>(root);
				IsLoaded = true;
			}
		}

		public int SampleCount { get; private set; }

		public override string Description { get { return String.Format("{0} Sampling Data", SampleCount); } }

		public override string FilteredDescription
		{
			get
			{
				return "";
			}
		}

        public string DetailedDescription
        {
            get
            {
                return String.Format("Collected Callstacks: {0}", SampleCount);
            }
        }

        public override double Duration { get { return Root == null ? 130.0 : Root.Duration; } }

		public override void Load()
		{
			if (!IsLoaded)
			{
				if (DescriptionBoard == null)
					DescriptionBoard = new SamplingDescriptionBoard();
				DescriptionBoard.Read(Response);
				Root = SamplingNode.Create(Response.Reader, DescriptionBoard, null);
			}
		}

		public SamplingFrame(DataResponse response, FrameGroup group) : base(response, group)
		{
			SampleCount = response.Reader.ReadInt32();
		}

		public SamplingFrame(List<Callstack> callstacks, FrameGroup group, double totalDuration) : base(null, group)
		{
			SampleCount = callstacks.Count;
			Root = SamplingNode.Create(callstacks, totalDuration);
		}
	}
}
