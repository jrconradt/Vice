using System.Collections.Generic;
using Vice.Jobs;
using Xunit;

namespace Vice.Tests;

public class JobStateTransitionsTests
{
    private static readonly HashSet<(JobStatus Current, JobStatus Target)> ValidTransitions = new()
    {
        (JobStatus.Queued, JobStatus.Running),
        (JobStatus.Queued, JobStatus.Failed),
        (JobStatus.Running, JobStatus.Paused),
        (JobStatus.Running, JobStatus.Completed),
        (JobStatus.Running, JobStatus.Failed),
        (JobStatus.Paused, JobStatus.Running),
        (JobStatus.Paused, JobStatus.Failed),
    };

    public static IEnumerable<object[]> AllPairs()
    {
        foreach (var current in Enum.GetValues<JobStatus>())
        {
            foreach (var target in Enum.GetValues<JobStatus>())
            {
                yield return new object[] { current, target };
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void IsValid_MatchesDocumentedTransitionSet(JobStatus current, JobStatus target)
    {
        var expected = ValidTransitions.Contains((current, target));

        var actual = JobStateTransitions.IsValid(current, target);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsValid_ReturnsTrueForExactlyTheDocumentedTransitions()
    {
        var actual = new HashSet<(JobStatus Current, JobStatus Target)>();
        foreach (var current in Enum.GetValues<JobStatus>())
        {
            foreach (var target in Enum.GetValues<JobStatus>())
            {
                if (JobStateTransitions.IsValid(current, target))
                {
                    actual.Add((current, target));
                }
            }
        }

        Assert.Equal(ValidTransitions, actual);
    }
}
