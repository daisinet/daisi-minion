using Daisi.Llogos.Chat;
using Daisi.Llogos.Training;
using Daisi.Llogos.Training.Lora;

namespace Daisi.Minion.Evolution;

/// <summary>
/// Loads and merges a .llra LoRA adapter into model weights at startup.
/// Handles the CPU-merge then GPU-upload pattern required when the model is on GPU.
/// </summary>
public static class AdapterLoader
{
    /// <summary>
    /// Merge a LoRA adapter into a loaded model's weights.
    /// Returns the adapter for logging, or null if no adapter was loaded.
    /// </summary>
    public static LoraAdapter? MergeIfConfigured(
        string? adapterPath,
        DaisiLlogosModelHandle modelHandle,
        Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(adapterPath))
            return null;

        if (!File.Exists(adapterPath))
        {
            log?.Invoke($"LoRA adapter not found: {adapterPath}");
            return null;
        }

        try
        {
            log?.Invoke($"Loading LoRA adapter: {Path.GetFileName(adapterPath)}");

            var adapter = LoraFile.Load(adapterPath);

            // Merge on CPU — works regardless of whether weights are on CPU or GPU
            var cpuBackend = new Daisi.Llogos.Cpu.CpuBackend();
            LoraInference.MergeAdapter(modelHandle.Weights, adapter, cpuBackend, modelHandle.Config);

            // Re-upload to GPU if the model backend is not CPU
            if (modelHandle.Backend is not Daisi.Llogos.Cpu.CpuBackend)
            {
                log?.Invoke("Re-uploading merged weights to GPU...");
                LoraInference.UploadWeights(modelHandle.Weights, modelHandle.Backend, modelHandle.Config);
            }

            log?.Invoke($"LoRA adapter merged: {adapter.ParameterCount:N0} params, rank={adapter.Config.Rank}");
            return adapter;
        }
        catch (Exception ex)
        {
            log?.Invoke($"LoRA adapter failed: {ex.Message}");
            return null;
        }
    }
}
