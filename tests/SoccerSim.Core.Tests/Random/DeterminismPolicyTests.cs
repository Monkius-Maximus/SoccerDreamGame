using System.Text;
using Xunit;

namespace SoccerSim.Core.Tests.Random;

/// <summary>
/// O policiamento da disciplina de determinismo, feito varrendo o próprio fonte.
///
/// <para>
/// Um analisador Roslyn resolveria isto com muito mais cerimônia — projeto novo, empacotamento,
/// versionamento — para o mesmo resultado. Uma varredura de texto é simples, roda em
/// <c>dotnet test</c> junto com todo o resto e falha nomeando arquivo e linha. Simples &gt;
/// complexo.
/// </para>
/// </summary>
public sealed class DeterminismPolicyTests
{
    /// <summary>
    /// Proibido em qualquer lugar de <c>src/</c>: o gerador da BCL. A sequência dele é detalhe de
    /// implementação não especificado — muda entre versões de runtime e entre plataformas —, então
    /// não pode sustentar save, replay nem harness de calibração.
    /// </summary>
    private static readonly string[] ForbiddenEverywhereInSrc =
    [
        "System.Random",
        "new Random(",
        "Random.Shared",
    ];

    /// <summary>
    /// Proibido dentro de <c>Core/Random/</c>: transcendentais. A IEEE 754 só garante
    /// arredondamento correto para <c>+ − × ÷</c> e raiz quadrada; log, exp e trigonometria ficam
    /// a cargo da libm da plataforma e podem diferir no último bit. Um único bit diferente aqui é
    /// um replay quebrado. Potência se escreve como multiplicação à mão.
    /// </summary>
    private static readonly string[] ForbiddenInRandomNamespace =
    [
        "Math.Log",
        "Math.Exp",
        "Math.Sin",
        "Math.Cos",
        "Math.Tan",
        "Math.Pow",
    ];

    [Fact]
    public void Source_DoesNotUseTheBclRandomGenerator()
        => AssertNoneOf(ForbiddenEverywhereInSrc, Path.Combine(RepositoryRoot, "src"));

    [Fact]
    public void RandomNamespace_DoesNotUseTranscendentalFunctions()
        => AssertNoneOf(ForbiddenInRandomNamespace,
            Path.Combine(RepositoryRoot, "src", "SoccerSim.Core", "Random"));

    /// <summary>
    /// Guarda da própria guarda: se o caminho varrido ficar vazio (pasta renomeada, layout mudado),
    /// os testes acima passariam sem ler nada. Aqui a varredura tem que ter encontrado fonte.
    /// </summary>
    [Fact]
    public void ThePolicyScan_ActuallyReadsSource()
    {
        Assert.NotEmpty(SourceFilesUnder(Path.Combine(RepositoryRoot, "src")));
        Assert.NotEmpty(SourceFilesUnder(Path.Combine(RepositoryRoot, "src", "SoccerSim.Core", "Random")));
    }

    private static void AssertNoneOf(string[] forbidden, string directory)
    {
        var violations = new StringBuilder();

        foreach (string file in SourceFilesUnder(directory))
        {
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string token in forbidden)
                {
                    if (lines[i].Contains(token, StringComparison.Ordinal))
                    {
                        violations.AppendLine(
                            $"{Path.GetRelativePath(RepositoryRoot, file)}:{i + 1}: '{token}' -> {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(violations.Length == 0,
            $"Violações da política de determinismo:{Environment.NewLine}{violations}");
    }

    private static IReadOnlyList<string> SourceFilesUnder(string directory)
    {
        Assert.True(Directory.Exists(directory), $"Diretório varrido não existe: {directory}");

        return Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            // bin/ e obj/ carregam fonte gerado pelo build, que não é nosso para policiar.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Sobe a partir do diretório do assembly até achar o arquivo de solução. Falha explicitamente
    /// se não achar, em vez de varrer silenciosamente um caminho errado e passar por vacuidade.
    /// </summary>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SoccerDreamGame.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate SoccerDreamGame.sln walking up from '{AppContext.BaseDirectory}'.");
    }
}
