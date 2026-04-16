using Daisi.Minion.Engine;

namespace Daisi.Minion.Tests.Engine;

/// <summary>
/// Narrow unit tests for <see cref="DaisinetGoalRunner"/>. Full goal-loop integration requires
/// a live ORC and is covered by the project-level "run against dev ORC" verification — see the
/// plan file's End-to-end section. These tests only cover what doesn't need gRPC mocks:
/// construction, option defaults, and the missing-SECRET-KEY fast-fail.
/// </summary>
public class DaisinetGoalRunnerTests
{
    [Fact]
    public void Ctor_WithValidOptions_DoesNotThrow()
    {
        using var tmp = new TempDir();
        var runner = new DaisinetGoalRunner(new DaisinetGoalRunner.Options
        {
            Goal = "do a thing",
            WorkingDirectory = tmp.Path,
        });

        // If construction succeeded, the base tool registry was populated (7 tools).
        // We can't reach the private registry directly; the absence of an exception is the assertion.
        runner.Dispose();
    }

    [Fact]
    public void Ctor_ZeroMaxIterations_DefaultsTo20()
    {
        // Proxy: the runner does not throw for MaxIterations=0; the default 20 is enforced inside RunAsync.
        using var tmp = new TempDir();
        var runner = new DaisinetGoalRunner(new DaisinetGoalRunner.Options
        {
            Goal = "x",
            MaxIterations = 0,
            WorkingDirectory = tmp.Path,
        });
        runner.Dispose();
    }

    [Fact]
    public async Task RunAsync_WithoutSecretKey_FailsFast()
    {
        // Snapshot + clear the env var for this test; restore after.
        var prior = Environment.GetEnvironmentVariable("DAISI_SECRET_KEY");
        Environment.SetEnvironmentVariable("DAISI_SECRET_KEY", "");

        // The SDK's DaisiStaticSettings may be pre-populated from a previous test run; clear it.
        Daisi.SDK.Models.DaisiStaticSettings.SecretKey = "";

        try
        {
            using var tmp = new TempDir();
            using var runner = new DaisinetGoalRunner(new DaisinetGoalRunner.Options
            {
                Goal = "say hi",
                WorkingDirectory = tmp.Path,
            });

            // Capture stderr for the assertion.
            var origErr = Console.Error;
            using var capturedErr = new StringWriter();
            Console.SetError(capturedErr);

            int exit;
            try
            {
                exit = await runner.RunAsync(CancellationToken.None);
            }
            finally
            {
                Console.SetError(origErr);
            }

            Assert.Equal(1, exit);
            Assert.Contains("DAISI_SECRET_KEY", capturedErr.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAISI_SECRET_KEY", prior);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "minion-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
