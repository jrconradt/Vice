namespace Vice.Jobs;

internal readonly record struct WorkerPoolHealth(
    int ConfiguredConcurrency,
    int LiveWorkerCount,
    bool IsDegraded);
