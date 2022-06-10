using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Profiler.Data
{
	public class Callstack : ITick, IComparable<Callstack>, IList<SamplingDescription>
	{
		public SamplingDescription[] stack;
		public long Start { get; set; }
		public CallStackReason Reason { get; set; }
		#region IList Interface
		public SamplingDescription this[int i] { get => stack[i]; set { } }
		public int Count { get => stack.Count(); }
		public void Clear() { }
		public void CopyTo(SamplingDescription[] s, int i){}
		public void Add(SamplingDescription s) { }
		public void Insert(int a, SamplingDescription s) { }
		public bool Remove(SamplingDescription s) { return false; }
		public void RemoveAt(int a) { }
		public bool Contains(SamplingDescription s) { return false; }
		#region Implementation of IEnumerable
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return stack.GetEnumerator();
		}

		public IEnumerator<SamplingDescription> GetEnumerator()
		{
			return ((IEnumerable<SamplingDescription>)stack).GetEnumerator();
		}

		#endregion
		public int IndexOf(SamplingDescription value) { return 0; }
		public bool IsFixedSize {get => true;}
		public bool IsReadOnly {get => true;}
		#endregion

		public int CompareTo(Callstack other)
		{
			return Start.CompareTo(other.Start);
		}

    }

	public class CallstackPack : IResponseHolder
	{
		public Dictionary<UInt64, List<Callstack>> CallstackMap { get; set; }
		public override DataResponse Response { get; set; }

		public static CallstackPack Create(DataResponse response, ISamplingBoard board, SysCallBoard sysCallBoard)
		{
			CallstackPack result = new CallstackPack() { Response = response, CallstackMap = new Dictionary<ulong, List<Callstack>>() };

			while (true)
			{
				UInt64 count = response.Reader.ReadUInt64();
				if (count == ~0UL)
				  break;
				UInt64 timestamp = response.Reader.ReadUInt64();
				UInt64 threadID = response.Reader.ReadUInt64();

				Callstack callstack = new Callstack { Start = (long)timestamp, Reason = CallStackReason.AutoSample };
				callstack.stack = new SamplingDescription[count];

				if (sysCallBoard != null)
				{
					if (sysCallBoard.HasSysCall(threadID, callstack.Start))
					{
						callstack.Reason = CallStackReason.SysCall;
					}
				}

				for (ulong addressIndex = 0; addressIndex < count; ++addressIndex)
					callstack.stack[count - addressIndex - 1] = board.GetDescription(response.Reader.ReadUInt64());

				List<Callstack> callstacks;
				if (!result.CallstackMap.TryGetValue(threadID, out callstacks))
				{
					callstacks = new List<Callstack>(1024);
					result.CallstackMap.Add(threadID, callstacks);
				}

				callstacks.Add(callstack);
			}

			foreach (List<Callstack> cs in result.CallstackMap.Values)
			{
				cs.Sort();
			}

			return result;
		}
	}
}
