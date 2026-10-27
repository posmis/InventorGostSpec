using InventorGostSpec.Core.Model;

namespace InventorGostSpec.Core.Tests;

public class DrawingFilesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "proj");
    private static string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    [Fact]
    public void Same_name_next_to_model_wins_then_same_name_elsewhere()
    {
        var model = P("Модели", "Корпус.ipt");
        var files = new[] { P("Чертежи", "Корпус.idw"), P("Модели", "Корпус.idw"), P("Модели", "АБВГ.715431.001.idw") };

        Assert.Equal(P("Модели", "Корпус.idw"), DrawingFiles.BestByName(files, model, "АБВГ.715431.001"));
        Assert.Equal(P("Чертежи", "Корпус.idw"), DrawingFiles.BestByName(files.Skip(0).Where(f => !f.Contains("Модели")), model, ""));
    }

    [Fact]
    public void Drawing_in_another_project_folder_is_found_by_designation()
    {
        var model = P("Модели", "Привод.iam");
        var files = new[] { P("Чертежи", "АБВГ.301234.000СБ.idw"), P("Чертежи", "АБВГ.301234.001 СБ.idw"), P("Чертежи", "АБВГ.301234.0010.idw") };

        Assert.Equal(P("Чертежи", "АБВГ.301234.001 СБ.idw"), DrawingFiles.BestByName(files, model, "АБВГ.301234.001СБ"));
    }

    [Fact]
    public void Prefix_matches_and_wrong_files_are_ignored()
    {
        var model = P("Привод.iam");

        Assert.Equal(4, DrawingFiles.Rank(P("АБВГ.301234.001СБ Привод.idw"), model, "АБВГ.301234.001"));
        Assert.Equal(4, DrawingFiles.Rank(P("Привод СБ.dwg"), model, ""));
        Assert.Null(DrawingFiles.Rank(P("Привод ленточный.idw"), model, ""));
        Assert.Null(DrawingFiles.Rank(P("АБВГ.301234.001-01.idw"), model, "АБВГ.301234.001"));
        Assert.Null(DrawingFiles.Rank(P("Привод.ipt"), model, ""));
        Assert.Null(DrawingFiles.BestByName(Array.Empty<string>(), model, "А.001"));
    }

    [Fact]
    public void By_content_part_needs_drawing_of_only_this_part()
    {
        var part = P("Детали", "Вал.ipt");
        var assembly = P("Привод.iam");
        var contents = new Dictionary<string, IReadOnlyList<string>>
        {
            [P("Чертежи", "АБВГ.301234.001СБ.idw")] = new[] { assembly, part },
            [P("Чертежи", "Лист 7.idw")] = new[] { part },
        };

        Assert.Equal(P("Чертежи", "Лист 7.idw"), DrawingFiles.BestByContent(contents, part, isAssembly: false));

        contents.Remove(P("Чертежи", "Лист 7.idw"));
        Assert.Null(DrawingFiles.BestByContent(contents, part, isAssembly: false));

        // Сборочный чертёж с листами деталей подходит сборке.
        Assert.Equal(P("Чертежи", "АБВГ.301234.001СБ.idw"), DrawingFiles.BestByContent(contents, assembly.ToUpperInvariant(), isAssembly: true));
    }
}
