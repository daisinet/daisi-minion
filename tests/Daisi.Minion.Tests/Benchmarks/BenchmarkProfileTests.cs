using Daisi.Minion.Benchmarks;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Benchmarks;

public class BenchmarkProfileTests
{
    [Fact]
    public void Score_PerfectOutcome_HighScore()
    {
        var profile = new BenchmarkProfile();
        var outcome = new TaskOutcome
        {
            Succeeded = true,
            TotalTokens = 100,
            IterationsUsed = 1,
            IterationBudget = 20,
            ToolCalls = 2,
            ContextUtilization = 0.1,
            Duration = TimeSpan.FromSeconds(5),
            TestsPass = true,
        };

        var score = profile.Score(outcome);
        Assert.True(score > 0.7, $"Expected > 0.7, got {score}");
    }

    [Fact]
    public void Score_FailedOutcome_LowScore()
    {
        var profile = new BenchmarkProfile();
        var outcome = new TaskOutcome
        {
            Succeeded = false,
            TotalTokens = 7000,
            IterationsUsed = 20,
            IterationBudget = 20,
            ToolCalls = 45,
            ContextUtilization = 0.95,
            Duration = TimeSpan.FromSeconds(280),
            TestsPass = false,
        };

        var score = profile.Score(outcome);
        Assert.True(score < 0.3, $"Expected < 0.3, got {score}");
    }

    [Fact]
    public void Score_ReturnsBetweenZeroAndOne()
    {
        var profile = new BenchmarkProfile();
        var outcome = new TaskOutcome
        {
            Succeeded = true,
            TotalTokens = 4000,
            IterationsUsed = 10,
            IterationBudget = 20,
            ToolCalls = 25,
            ContextUtilization = 0.5,
            Duration = TimeSpan.FromSeconds(150),
        };

        var score = profile.Score(outcome);
        Assert.InRange(score, 0.0, 1.0);
    }

    [Fact]
    public void Score_FewerTokens_HigherScoreWithNegativeWeight()
    {
        var profile = new BenchmarkProfile { TokensPerTask = -1.0 };
        // Zero out other weights to isolate
        profile.IterationsToCompletion = 0;
        profile.ToolCallsPerTask = 0;
        profile.ContextUtilization = 0;
        profile.DurationSeconds = 0;
        profile.TestPassRate = 0;
        profile.SuccessRate = 0;

        var fewTokens = new TaskOutcome { TotalTokens = 100 };
        var manyTokens = new TaskOutcome { TotalTokens = 7000 };

        Assert.True(profile.Score(fewTokens) > profile.Score(manyTokens));
    }

    [Fact]
    public void Score_SuccessWeighted_SuccessScoresHigher()
    {
        var profile = new BenchmarkProfile { SuccessRate = 1.0 };
        // Zero out others
        profile.TokensPerTask = 0;
        profile.IterationsToCompletion = 0;
        profile.ToolCallsPerTask = 0;
        profile.ContextUtilization = 0;
        profile.DurationSeconds = 0;
        profile.TestPassRate = 0;

        var success = new TaskOutcome { Succeeded = true };
        var failure = new TaskOutcome { Succeeded = false };

        Assert.True(profile.Score(success) > profile.Score(failure));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var profile = new BenchmarkProfile
            {
                TokensPerTask = -0.5,
                IterationsToCompletion = -0.8,
            };
            profile.Save(tempFile);

            var loaded = BenchmarkProfile.Load(tempFile);
            Assert.NotNull(loaded);
            Assert.Equal(-0.5, loaded.TokensPerTask);
            Assert.Equal(-0.8, loaded.IterationsToCompletion);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsNull()
    {
        Assert.Null(BenchmarkProfile.Load("/nonexistent/path.json"));
    }
}
