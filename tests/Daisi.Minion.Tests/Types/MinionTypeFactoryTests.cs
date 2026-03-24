using Daisi.Minion.Types;

namespace Daisi.Minion.Tests.Types;

public class MinionTypeFactoryTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("test")]
    [InlineData("research")]
    public void Get_KnownType_ReturnsConfig(string typeName)
    {
        var config = MinionTypeFactory.Get(typeName);
        Assert.Equal(typeName, config.Name);
        Assert.NotEmpty(config.Description);
    }

    [Fact]
    public void Get_UnknownType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => MinionTypeFactory.Get("nonexistent"));
        Assert.Contains("Unknown minion type", ex.Message);
        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void Get_CaseInsensitive()
    {
        var config = MinionTypeFactory.Get("CODE");
        Assert.Equal("code", config.Name);
    }

    [Fact]
    public void Exists_KnownType_ReturnsTrue()
    {
        Assert.True(MinionTypeFactory.Exists("code"));
        Assert.True(MinionTypeFactory.Exists("test"));
        Assert.True(MinionTypeFactory.Exists("research"));
    }

    [Fact]
    public void Exists_UnknownType_ReturnsFalse()
    {
        Assert.False(MinionTypeFactory.Exists("nonexistent"));
    }

    [Fact]
    public void Available_ContainsAllTypes()
    {
        var available = MinionTypeFactory.Available;
        Assert.Contains("code", available);
        Assert.Contains("test", available);
        Assert.Contains("research", available);
    }

    [Fact]
    public void CodeType_HasCoderDefaultRole()
    {
        var config = MinionTypeFactory.Get("code");
        Assert.Equal("coder", config.DefaultRole);
    }

    [Fact]
    public void CodeType_HasAllTools()
    {
        var config = MinionTypeFactory.Get("code");
        Assert.Null(config.AllowedTools); // null = all tools
    }

    [Fact]
    public void ResearchType_HasReadOnlyTools()
    {
        var config = MinionTypeFactory.Get("research");
        Assert.NotNull(config.AllowedTools);
        Assert.Contains("file_read", config.AllowedTools);
        Assert.Contains("grep", config.AllowedTools);
        Assert.Contains("glob", config.AllowedTools);
        Assert.Contains("git", config.AllowedTools);
        Assert.DoesNotContain("file_write", config.AllowedTools);
        Assert.DoesNotContain("file_edit", config.AllowedTools);
        Assert.DoesNotContain("shell", config.AllowedTools);
    }

    [Fact]
    public void ResearchType_HasChatDefaultRole()
    {
        var config = MinionTypeFactory.Get("research");
        Assert.Equal("chat", config.DefaultRole);
    }

    [Fact]
    public void TestType_HasPromptExtension()
    {
        var config = MinionTypeFactory.Get("test");
        Assert.NotNull(config.SystemPromptExtension);
        Assert.Contains("testing", config.SystemPromptExtension, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_CustomType_Available()
    {
        MinionTypeFactory.Register(new MinionTypeConfig
        {
            Name = "custom-test-type",
            Description = "Custom type for testing",
        });

        Assert.True(MinionTypeFactory.Exists("custom-test-type"));
        var config = MinionTypeFactory.Get("custom-test-type");
        Assert.Equal("Custom type for testing", config.Description);
    }
}
