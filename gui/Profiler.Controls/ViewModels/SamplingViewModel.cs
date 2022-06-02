using Profiler.Data;
using Profiler.InfrastructureMvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Profiler.Controls.ViewModels
{
	public class FunctionOnDemandViewModel : BaseViewModel
	{
		private FrameGroup Group { get; set; }
		private EventDescription Description { get; set; }
		private FrameList.Type Type { get; set; }

		public void Load(FrameGroup group, EventDescription desc, FrameList.Type fType = FrameList.Type.None)
		{
			if (group != Group || desc != Description || Type != fType)
			{
				Group = group;
				Description = desc;
				Type = fType;
				IsDirty = true;
				Update();
			}
		}

		public bool IsDirty { get; set; }

		protected virtual void Update(FrameGroup group, EventDescription desc, FrameList.Type fType) { }

		public void Update()
		{
			if (IsDirty && IsActive)
			{
				Update(Group, Description, Type);
				IsDirty = false;
			}
		}

		private int activeCounter;
		public bool IsActive { get { return activeCounter > 0; } }
		public void SetActive(bool isActive) { activeCounter += isActive ? 1 : -1; Update(); }


	}

	public class SamplingViewModel : FunctionOnDemandViewModel
	{
		public CallStackReason Reason { get; set; }

		public delegate void OnLoadedHandler(SamplingFrame frame);
		public event OnLoadedHandler OnLoaded;

		protected override void Update(FrameGroup group, EventDescription desc, FrameList.Type fType = FrameList.Type.None)
		{
			SamplingFrame frame = (group != null && desc != null) ? group.CreateSamplingFrame(desc, Reason) : null;
			OnLoaded?.Invoke(frame);
			base.Update(group, desc, fType);
		}
	}

	public class MergedEventViewModel : FunctionOnDemandViewModel
	{
		public delegate void OnLoadedHandler(EventFrame frame);
		public event OnLoadedHandler OnLoaded;

		protected override void Update(FrameGroup group, EventDescription desc, FrameList.Type fType = FrameList.Type.None)
		{
			EventFrame frame = (group != null && desc != null) ? group.CreateMergedEventFrame(desc, fType) : null;
			OnLoaded?.Invoke(frame);
			base.Update(group, desc, fType);
		}
	}
}
