using System;

namespace ArrayImageViewer.Core
{
    internal sealed class FrameBuffer
    {
        private readonly long[] samples;

        public FrameBuffer(FrameConfiguration configuration, long[] samples)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            if (samples == null)
            {
                throw new ArgumentNullException("samples");
            }

            if (samples.LongLength < configuration.RequiredSampleCount)
            {
                throw new ArgumentException("The source buffer is smaller than the configured frame.");
            }

            Configuration = configuration;
            this.samples = samples;
        }

        public FrameConfiguration Configuration { get; private set; }

        public long GetRaw(int x, int y)
        {
            return samples[Configuration.GetSampleIndex(x, y)];
        }

        public static FrameBuffer CreateSynthetic(FrameConfiguration configuration)
        {
            var data = new long[configuration.RequiredSampleCount];
            for (var y = 0; y < configuration.Height; y++)
            {
                for (var x = 0; x < configuration.Width; x++)
                {
                    var ramp = ((long)x * 4095L / Math.Max(1, configuration.Width - 1)) +
                               ((long)y * 4095L / Math.Max(1, configuration.Height - 1));
                    var site = BayerLayout.GetSite(configuration.BayerPattern, x, y);
                    var channelOffset = site == BayerSite.R ? 1200 : site == BayerSite.B ? 300 : 700;
                    data[configuration.GetSampleIndex(x, y)] = Math.Min(8191, ramp / 2 + channelOffset);
                }
            }

            return new FrameBuffer(configuration, data);
        }
    }
}
