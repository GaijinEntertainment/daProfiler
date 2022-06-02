using Profiler.Data;
using Profiler.Controls.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Profiler.Controls
{
	/// <summary>
	/// Interaction logic for MergedEventThreadView.xaml
	/// </summary>
	public partial class MergedEventThreadView : UserControl
	{
		public MergedEventThreadView()
		{
			InitializeComponent();

			DataContextChanged += MergedEventThreadView_DataContextChanged;
			IsVisibleChanged += MergedEventThreadView_IsVisibleChanged;
		}

		private void VM_OnLoaded(EventFrame frame)
		{
			InitThreadList(frame);
		}

		private void MergedEventThreadView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			VM = DataContext as MergedEventViewModel;
			VM.OnLoaded += VM_OnLoaded;
		}

		MergedEventViewModel VM { get; set; }

		private void MergedEventThreadView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			VM?.SetActive(IsVisible);
		}

		double BuildEntryList(List<Entry> entries, EventNode node, double offset)
		{
			double duration = node.Entry.Duration, childrenDuration = 0;
			List<EventNode> children = new List<EventNode>(node.Children.Count);
			foreach (EventNode child in node.Children)
			{
				children.Add(child);
				childrenDuration += child.Entry.Duration;
			}
			children.Sort((c1, c2) => Math.Sign(c2.Entry.Finish - c1.Entry.Finish));

			duration = Math.Max(childrenDuration, duration);
			if (node.Description != null)
				entries.Add(new Entry(node.Entry.Description, Durable.MsToTick(offset), Durable.MsToTick(offset + duration), -1));
			offset += (duration - childrenDuration) * 0.5;

			foreach (EventNode child in children)
			{
				BuildEntryList(entries, child, offset);
				offset += child.Entry.Duration;
			}
			return duration;
		}

		private ThreadViewSettings Settings { get; set; } = new ThreadViewSettings();
		private double totalDuration = 0;

		void InitThreadList(EventFrame frame)
		{
			Frame = frame;

			List<ThreadRow> rows = new List<ThreadRow>();

			if (frame != null && frame.Root.Children.Count != 0)
			{
				List<Entry> entries = new List<Entry>();

				EventNode root = frame.Root.Children[0] as EventNode;

				totalDuration = BuildEntryList(entries, root, 0.0);

				EventFrame eventFrame = new EventFrame(new FrameHeader() { Start = 0, Finish = Durable.MsToTick(totalDuration) }, entries, frame.Group);
				ThreadData threadData = new ThreadData(null) { Events = new List<EventFrame> { eventFrame } };
				EventsThreadRow row = new EventsThreadRow(frame.Group, new ThreadDescription() { Name = "Event Node" }, threadData, Settings);
				row.LimitMaxDepth = false;
				row.EventNodeHover += Row_EventNodeHover;
				rows.Add(row);
				ThreadViewControl.Scroll.ViewUnit.Width = 1.0;
				ThreadViewControl.InitRows(rows, eventFrame.Header);
			}
			else
			{
				ThreadViewControl.InitRows(rows, null);
				totalDuration = 0;
			}

		}

		private void Row_EventNodeHover(Point mousePos, Rect rect, ThreadRow row, EventNode node)
		{
			if (node == null)
				ThreadViewControl.ToolTipPanel = null;
			else
			{
				ThreadViewControl.ToolTipPanel = new ThreadViewControl.TooltipInfo { Text = String.Format("{0}   {1} ({2:0.#}%)", node.Description.FullName, Utils.ConvertMsToString(node.Duration), 100.0 * node.Duration / totalDuration), Rect = rect };
				ThreadViewControl.ToolTipPanel.Texts.Add(node.Description.Path.ToString());
			}
		}

		private FrameGroup Group { get; set; }
		private EventDescription Description { get; set; }
		private EventFrame Frame { get; set; }
	}
}
