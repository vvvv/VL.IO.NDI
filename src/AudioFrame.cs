using System;

namespace VL.IO.NDI
{
    public class AudioFrame
    {
        public AudioFrame(Memory<float> planarBuffer, int noSamples, int noChannels, int sampleRate, string metadata)
        {
            PlanarBuffer = planarBuffer;
            NoSamples = noSamples;
            NoChannels = noChannels;
            SampleRate = sampleRate;
            Metadata = metadata;
        }

        public Memory<float> PlanarBuffer { get; }

        public int NoSamples { get; }

        public int NoChannels { get; }

        public int SampleRate { get; }

        public string Metadata { get; }

        public Memory<float> GetChannel(int index) => PlanarBuffer.Slice(index * NoSamples, NoSamples);
    }
}
