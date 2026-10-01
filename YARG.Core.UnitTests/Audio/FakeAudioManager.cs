using YARG.Core.Audio;

namespace YARG.Core.UnitTests.Audio;

internal sealed class FakeAudioManager : AudioManager
{
    protected internal override ReadOnlySpan<string> SupportedFormats => [];

    protected internal override StemMixer? CreateMixer(string name, float speed, double volume, bool clampStemVolume, bool normalize) => null;

    protected internal override MicDevice? GetInputDevice(string name) => null;

    protected internal override List<InputDeviceInfo> GetAllInputDevices() => [];

    protected internal override MicDevice? CreateInputDevice(InputDeviceInfo device) => null;

    protected internal override OutputChannel? CreateOutputChannel(int channelId) => null;

    protected internal override List<(int id, string name)> GetAllOutputDevices() => [];

    protected internal override int GetOutputChannelCount() => 0;

    protected internal override bool SetOutputDevice(string name) => false;

    protected internal override void SetMasterVolume(double volume) { }

    public override void LoadVenueSample(string name, byte[] sampleData, OutputChannel? outputChannel = null) { }
    public override void ClearVenueSamples() { }
    protected internal override void PlayMetronomeSoundEffectToChannel(MetronomeSample sample,
        MetronomePitch pitch, int channelId) { }


    protected override void SetBufferLength_Internal(int length) { }
}
