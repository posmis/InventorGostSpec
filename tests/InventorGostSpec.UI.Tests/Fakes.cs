using InventorGostSpec.Core.Model;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.UI.Tests;

internal sealed class FakeHost : ISpecificationHost
{
    public List<ComponentData> Components { get; } = new();

    public SpecSettings Settings { get; set; } = new();

    public int SaveCount { get; private set; }

    public Specification? Saved { get; private set; }

    public int WritePositionsCount { get; private set; }

    public string DocumentName => "Сборка.iam";

    public SpecSettings LoadSettings() => Settings;

    public void SaveSettings(SpecSettings settings) => Settings = settings;

    public IReadOnlyList<ComponentData> ReadComponents() => Components.ToList();

    public void Save(Specification specification)
    {
        SaveCount++;
        Saved = specification;
    }

    public string WritePositions(Specification specification)
    {
        WritePositionsCount++;
        return "ok";
    }

    public void ReportError(Exception exception) => throw exception;
}

internal sealed class FakeDialogs : IDialogService
{
    public bool ConfirmAnswer { get; set; } = true;

    public bool? SaveAnswer { get; set; } = true;

    public List<string> Warnings { get; } = new();

    public List<string> Infos { get; } = new();

    public Action<NewSectionViewModel>? OnNewSection { get; set; }

    public Action<ReserveSettingsViewModel>? OnReserves { get; set; }

    public Action<GroupViewModel>? OnGroup { get; set; }

    public bool Confirm(string text) => ConfirmAnswer;

    public void Info(string text) => Infos.Add(text);

    public void Warning(string text) => Warnings.Add(text);

    public bool? AskSave() => SaveAnswer;

    public bool EditNewSection(NewSectionViewModel viewModel)
    {
        OnNewSection?.Invoke(viewModel);
        return OnNewSection is not null;
    }

    public bool EditGroup(GroupViewModel viewModel)
    {
        OnGroup?.Invoke(viewModel);
        return OnGroup is not null;
    }

    public bool EditReserves(ReserveSettingsViewModel viewModel)
    {
        OnReserves?.Invoke(viewModel);
        return OnReserves is not null;
    }
}
