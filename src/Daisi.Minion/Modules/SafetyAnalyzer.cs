using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Daisi.Minion.Modules;

/// <summary>
/// Walks a Roslyn syntax tree and rejects modules that use forbidden APIs.
/// Modules must use the sandboxed tool interface — no direct I/O, process, network, or reflection.
/// </summary>
public sealed class SafetyAnalyzer : CSharpSyntaxWalker
{
    private static readonly HashSet<string> ForbiddenNamespaces = new(StringComparer.Ordinal)
    {
        "System.IO",
        "System.Diagnostics",
        "System.Net",
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Reflection",
        "System.Reflection.Emit",
        "System.Runtime.InteropServices",
        "System.Runtime.Loader",
    };

    /// <summary>
    /// Specific types that are allowed even though their namespace is forbidden.
    /// For example, System.IO.Path is safe (no I/O, just string manipulation).
    /// </summary>
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "System.IO.Path",
    };

    private readonly List<Diagnostic> _violations = [];

    public IReadOnlyList<Diagnostic> Violations => _violations;

    /// <summary>
    /// Analyze a syntax tree for safety violations.
    /// Returns the list of violations found.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Analyze(SyntaxTree tree)
    {
        var analyzer = new SafetyAnalyzer();
        analyzer.Visit(tree.GetRoot());
        return analyzer.Violations;
    }

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        var ns = node.Name?.ToString();
        if (ns != null && IsForbiddenNamespace(ns))
        {
            _violations.Add(CreateViolation(node, $"Forbidden using directive: {ns}. Modules must use sandboxed tool interfaces."));
        }
        base.VisitUsingDirective(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        // Catch direct references to forbidden types like File, Process, HttpClient
        var name = node.Identifier.Text;
        if (name is "Process" or "ProcessStartInfo" or "HttpClient" or "WebClient"
            or "Socket" or "TcpClient" or "UdpClient"
            or "Assembly" or "AppDomain")
        {
            // Check if it's a type reference (not just a variable named "Process")
            if (node.Parent is MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax
                or TypeSyntax or QualifiedNameSyntax)
            {
                _violations.Add(CreateViolation(node, $"Forbidden type reference: {name}. Use sandboxed tool interfaces instead."));
            }
        }
        base.VisitIdentifierName(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        // Check for unsafe modifier
        if (node.Modifiers.Any(SyntaxKind.UnsafeKeyword))
        {
            _violations.Add(CreateViolation(node, "Unsafe code is not allowed in modules."));
        }
        base.VisitClassDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Modifiers.Any(SyntaxKind.UnsafeKeyword))
        {
            _violations.Add(CreateViolation(node, "Unsafe code is not allowed in modules."));
        }
        base.VisitMethodDeclaration(node);
    }

    public override void VisitBlock(BlockSyntax node)
    {
        // Check for unsafe blocks
        if (node.Parent is UnsafeStatementSyntax)
        {
            _violations.Add(CreateViolation(node, "Unsafe code blocks are not allowed in modules."));
        }
        base.VisitBlock(node);
    }

    private static bool IsForbiddenNamespace(string ns)
    {
        foreach (var forbidden in ForbiddenNamespaces)
        {
            if (ns.Equals(forbidden, StringComparison.Ordinal) ||
                ns.StartsWith(forbidden + ".", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static Diagnostic CreateViolation(SyntaxNode node, string message)
    {
        return Diagnostic.Create(
            new DiagnosticDescriptor(
                "MINION001",
                "Module Safety Violation",
                message,
                "Safety",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true),
            node.GetLocation());
    }
}
