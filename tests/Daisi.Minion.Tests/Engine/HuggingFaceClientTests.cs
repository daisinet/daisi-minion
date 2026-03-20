using Daisi.Minion.Engine;

namespace Daisi.Minion.Tests.Engine;

public class HuggingFaceClientTests
{
    [Theory]
    [InlineData("https://huggingface.co/Qwen/Qwen3.5-0.8B-GGUF", "Qwen/Qwen3.5-0.8B-GGUF")]
    [InlineData("https://huggingface.co/Qwen/Qwen3.5-0.8B-GGUF/", "Qwen/Qwen3.5-0.8B-GGUF")]
    [InlineData("Qwen/Qwen3.5-0.8B-GGUF", "Qwen/Qwen3.5-0.8B-GGUF")]
    [InlineData("https://huggingface.co/bartowski/Llama-3-70B-Instruct-GGUF/tree/main", "bartowski/Llama-3-70B-Instruct-GGUF")]
    public void ParseRepoId_ValidInputs(string input, string expected)
    {
        Assert.Equal(expected, HuggingFaceClient.ParseRepoId(input));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("https://example.com")]
    public void ParseRepoId_InvalidInputs(string input)
    {
        Assert.Null(HuggingFaceClient.ParseRepoId(input));
    }
}
