namespace Vice.Jobs;

public readonly record struct WorkerPoolHealth(
    int ConfiguredConcurrency,
    int LiveWorkerCount,
    bool IsDegraded);
