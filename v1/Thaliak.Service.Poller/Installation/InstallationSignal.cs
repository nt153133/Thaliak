using System.Threading.Channels;

namespace Thaliak.Service.Poller.Installation;

public sealed class InstallationSignal
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(250);

    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public void Notify() => _signals.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _signals.Reader.ReadAsync(cancellationToken);
        while (true)
        {
            await Task.Delay(QuietPeriod, cancellationToken);

            var receivedAnotherSignal = false;
            while (_signals.Reader.TryRead(out _))
            {
                receivedAnotherSignal = true;
            }

            if (!receivedAnotherSignal)
            {
                return;
            }
        }
    }
}
