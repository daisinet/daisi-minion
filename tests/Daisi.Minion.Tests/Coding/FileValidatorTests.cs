using Daisi.Minion.Coding;

namespace Daisi.Minion.Tests.Coding;

public class FileValidatorTests
{
    [Fact]
    public void Html_ValidPage_NoErrors()
    {
        var html = "<!DOCTYPE html><html><head><title>Test</title></head><body><h1>Hello</h1></body></html>";
        var errors = FileValidator.Validate("test.html", html);
        Assert.Null(errors);
    }

    [Fact]
    public void Html_UnclosedDiv_ReportsError()
    {
        var html = "<!DOCTYPE html><html><body><div><p>hello</p></body></html>";
        var errors = FileValidator.Validate("test.html", html);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Contains("div"));
    }

    [Fact]
    public void Html_MalformedIndex_CatchesMissingDivs()
    {
        // The actual malformed HTML from our test run
        var html = File.Exists(@"C:\minion-dev\summoner-test\index.html")
            ? File.ReadAllText(@"C:\minion-dev\summoner-test\index.html")
            : "<html><body><div><div><p>test</p></div></body></html>";
        var errors = FileValidator.Validate("index.html", html);
        Assert.NotNull(errors);
        Assert.True(errors.Count > 0, $"Expected errors but got none");
    }

    [Fact]
    public void Css_Balanced_NoErrors()
    {
        var css = "body { color: red; } .card { border: 1px solid; }";
        Assert.Null(FileValidator.Validate("style.css", css));
    }

    [Fact]
    public void Css_UnclosedBrace_ReportsError()
    {
        var css = "body { color: red; .card { border: 1px;";
        var errors = FileValidator.Validate("style.css", css);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Contains("unclosed"));
    }

    [Fact]
    public void Js_Balanced_NoErrors()
    {
        var js = "function hello() { console.log('hi'); }";
        Assert.Null(FileValidator.Validate("app.js", js));
    }

    [Fact]
    public void Js_UnclosedBrace_ReportsError()
    {
        var js = "function hello() { if (true) { console.log('hi'); }";
        var errors = FileValidator.Validate("app.js", js);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Contains("brace"));
    }

    [Fact]
    public void Json_Valid_NoErrors()
    {
        Assert.Null(FileValidator.Validate("config.json", "{\"key\": \"value\"}"));
    }

    [Fact]
    public void Json_Invalid_ReportsError()
    {
        var errors = FileValidator.Validate("config.json", "{key: value}");
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Contains("Invalid JSON"));
    }

    [Fact]
    public void CSharp_Balanced_NoErrors()
    {
        var cs = "namespace Foo { public class Bar { public void Baz() { } } }";
        Assert.Null(FileValidator.Validate("test.cs", cs));
    }

    [Fact]
    public void CSharp_UnclosedBrace_ReportsError()
    {
        var cs = "namespace Foo { public class Bar { public void Baz() { }";
        var errors = FileValidator.Validate("test.cs", cs);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Contains("brace"));
    }

    [Fact]
    public void UnknownExtension_ReturnsNull()
    {
        Assert.Null(FileValidator.Validate("readme.txt", "anything"));
    }
}
