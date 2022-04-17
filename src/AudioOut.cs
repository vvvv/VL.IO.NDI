using System;
using System.Collections.Generic;
using System.Text;
using VL.Audio;

namespace VL.IO.NDI
{
    class AudioOut : AudioSignal
    {

		public int sampleRate;
		public int bufferSize;
		public AudioOut() : base()
		{
			bufferSize = base.BufferSize;
			sampleRate = base.SampleRate;			
		}
	}
}
