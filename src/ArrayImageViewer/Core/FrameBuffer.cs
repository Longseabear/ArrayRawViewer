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
            return Configuration.NormalizeRawValue(samples[Configuration.GetSampleIndex(x, y)]);
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
                    var site = configuration.GetBayerSite(x, y);
                    var channelOffset = site == BayerSite.R ? 1200 : site == BayerSite.B ? 300 : 700;
                    var synthetic13BitValue = Math.Min(8191, ramp / 2 + channelOffset);
                    data[configuration.GetSampleIndex(x, y)] = configuration.RawMinimum +
                        synthetic13BitValue * (configuration.RawMaximum - configuration.RawMinimum) / 8191L;
                }
            }

            return new FrameBuffer(configuration, data);
        }
    }
}
