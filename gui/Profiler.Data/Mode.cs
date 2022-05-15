using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Profiler.Data
{
	public enum Mode
	{
		OFF = 0x0,
		EVENTS         = (1 << 1),
		GPU            = (1 << 2),
		TAGS           = (1 << 3),
		SAMPLING       = (1 << 4),
		SAVE_SPIKES    = (1 << 5),
		ETW            = (1 << 6),
		ETW_IO         = (1 << 7),
		ETW_SAMPLING   = (1 << 8),
		ETW_SYS_CALLS  = (1 << 9),
		ETW_PROCESSES  = (1 << 10),
		ETW_CTX_SWITCH = (1 << 11),
	}
}
