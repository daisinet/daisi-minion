using System.Diagnostics;

namespace Daisi.Minion.Tests.Engine;

/// <summary>
/// Integration tests that launch daisi-minion --cli as a subprocess.
/// Tests requiring model inference only run when a GPU is available (CPU is too slow).
/// Tests that don't need inference (arg validation, error paths) run everywhere.
/// </summary>
public class CliIntegrationTests
{
    private static readonly string ModelsDir = @"C:\GGUFS";
    private static readonly string TestModel = Path.Combine(ModelsDir, "Qwen3.5-0.8B-Q8_0.gguf");
    private static readonly string BitNetModel = Path.Combine(ModelsDir, "ggml-model-i2_s.gguf");

    private static string? FindMinionExe()
    {
        // Check from test bin directory up to the src directory
        var testBin = AppContext.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", "..", "src", "Daisi.Minion", "bin", "Debug", "net10.0", "daisi-minion.exe"));
        if (File.Exists(candidate)) return candidate;

        // Also try from the repo root
        candidate = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", "..", "..", "..", "daisi-minion", "src", "Daisi.Minion", "bin", "Debug", "net10.0", "daisi-minion.exe"));
        if (File.Exists(candidate)) return candidate;

        return null;
    }

    private static bool HasGpu()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null || !p.WaitForExit(5000)) return false;
            return p.ExitCode == 0 && !string.IsNullOrWhiteSpace(p.StandardOutput.ReadToEnd());
        }
        catch { return false; }
    }

    private static bool CanRunExeTests() => FindMinionExe() != null;
    private static bool CanRunInferenceTests() => CanRunExeTests() && File.Exists(TestModel) && HasGpu();
    private static bool CanRunBitNetTests() => CanRunExeTests() && File.Exists(BitNetModel);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string args, string? stdin = null, int timeoutMs = 60_000)
    {
        var exe = FindMinionExe()!;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            await process.StandardInput.FlushAsync();
        }
        process.StandardInput.Close();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"daisi-minion did not exit within {timeoutMs}ms");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    // ── Tests that don't need inference (fast, run everywhere) ──

    [Fact]
    public async Task Cli_InvalidModelPath_ExitsWithCode1()
    {
        if (!CanRunExeTests()) return;

        var (exitCode, _, stderr) = await RunCliAsync(
            "--cli --model /nonexistent/fake-model.gguf",
            timeoutMs: 30_000);

        Assert.Equal(1, exitCode);
        Assert.Contains("not found", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cli_HelpDoesNotCrash()
    {
        if (!CanRunExeTests()) return;

        // Just --cli with no model and no stdin should exit cleanly
        // when model is not found
        var (exitCode, _, stderr) = await RunCliAsync(
            "--cli --model /no/such/file.gguf",
            timeoutMs: 15_000);

        Assert.Equal(1, exitCode);
    }

    // ── Tests that need model inference (GPU only, skipped on CPU) ──

    [Fact]
    public async Task Cli_Interactive_LoadsModelAndResponds()
    {
        if (!CanRunInferenceTests()) return;

        var (exitCode, stdout, stderr) = await RunCliAsync(
            $"--cli --model \"{TestModel}\" --context 2048 --max-tokens 16",
            stdin: "Say hello.\n",
            timeoutMs: 60_000);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(stdout.Trim());
        Assert.Contains("Loaded", stderr);
    }

    [Fact]
    public async Task Cli_Goal_RunsAutonomously()
    {
        if (!CanRunInferenceTests()) return;

        var (exitCode, stdout, stderr) = await RunCliAsync(
            $"--cli --model \"{TestModel}\" --context 2048 --max-tokens 128 --max-iterations 2 " +
            $"--goal \"Say GOAL_COMPLETE immediately.\"",
            timeoutMs: 60_000);

        // Exit code 0 = goal complete, 2 = max iterations
        Assert.True(exitCode == 0 || exitCode == 2,
            $"Expected 0 or 2, got {exitCode}");
        Assert.Contains("Loaded", stderr);
    }

    [Fact]
    public async Task Cli_RoleFlag_AcceptedWithoutError()
    {
        if (!CanRunInferenceTests()) return;

        var (exitCode, stdout, stderr) = await RunCliAsync(
            $"--cli --model \"{TestModel}\" --context 2048 --max-tokens 8 --role coder",
            stdin: "Hi\n",
            timeoutMs: 60_000);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(stdout.Trim());
    }

    [Fact]
    public async Task Cli_JsonFlag_DoesNotCrash()
    {
        if (!CanRunInferenceTests()) return;

        var (exitCode, stdout, stderr) = await RunCliAsync(
            $"--cli --model \"{TestModel}\" --context 2048 --max-tokens 8 --json",
            stdin: "Hi\n",
            timeoutMs: 60_000);

        Assert.Equal(0, exitCode);
    }

    // ── BitNet CPU tests (no GPU required) ──

    [Fact]
    public async Task BitNet_Cpu_LoadsModelAndResponds()
    {
        if (!CanRunBitNetTests()) return;

        var (exitCode, stdout, stderr) = await RunCliAsync(
            $"--cli --model \"{BitNetModel}\" --backend cpu --context 2048 --max-tokens 16",
            stdin: "Say hello.\n",
            timeoutMs: 120_000);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(stdout.Trim());
        Assert.Contains("Loaded", stderr);
    }

    [Fact]
    public async Task BitNet_Cpu_Goal_RunsAutonomously()
    {
        if (!CanRunBitNetTests()) return;

        var (exitCode, stdout, stderr) = await RunCliAsync(
            $"--cli --model \"{BitNetModel}\" --backend cpu --context 2048 --max-tokens 128 --max-iterations 2 " +
            $"--goal \"Say GOAL_COMPLETE immediately.\"",
            timeoutMs: 120_000);

        Assert.True(exitCode == 0 || exitCode == 2,
            $"Expected 0 or 2, got {exitCode}. stderr: {stderr}");
        Assert.Contains("Loaded", stderr);
    }

    [Fact]
    public async Task BitNet_Cpu_RoleFlag_AcceptedWithoutError()
    {
        if (!CanRunBitNetTests()) return;

        var (exitCode, stdout, stderr) = await RunCliAsync(
            $"--cli --model \"{BitNetModel}\" --backend cpu --context 2048 --max-tokens 8 --role coder",
            stdin: "Hi\n",
            timeoutMs: 120_000);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(stdout.Trim());
    }
}
