using Vice.Logging;

namespace Vice.Streaming;

internal static class StagePairReconciler
{
    public static Exception? ResolvePrimary(Exception? producerEx, Exception? consumerEx)
    {
        if (producerEx is not null
            && consumerEx is not null)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "secondary stage exception (consumer)", consumerEx);
            return producerEx;
        }

        if (producerEx is not null)
        {
            return producerEx;
        }

        if (consumerEx is not null)
        {
            return consumerEx;
        }

        return null;
    }

    public static int ResolveExit(int producerExit, int consumerExit)
    {
        return producerExit != 0 ? producerExit : consumerExit;
    }
}
