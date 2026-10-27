using InventorGostSpec.AddIn.Commands;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>
/// Панель «Спецификация ГОСТ» на встроенных вкладках: «Сборка» в сборке, «Управление» в детали.
/// Если встроенная вкладка не найдена, создаётся своя.
/// </summary>
internal static class RibbonBuilder
{
    private const string AssembleTabId = "id_TabAssemble";
    private const string ManageTabId = "id_TabManage";

    private const string OwnTabId = "InventorGostSpec.Tab";
    private const string PanelId = "InventorGostSpec.Panel";

    public static void Build(Inventor.Application application, CommandRegistry commands)
    {
        var ribbons = application.UserInterfaceManager.Ribbons;

        // Главные действия — большие кнопки, подготовка компонентов — маленькие, в одну колонку.
        AddButtons(ribbons["Assembly"], AssembleTabId, commands,
            new Button(SpecificationCommand.Id, Large: true),
            new Button(WritePositionsCommand.Id, Large: true),
            new Button(AssignSectionCommand.Id, Large: false),
            new Button(AutoAssignCommand.Id, Large: false),
            new Button(StandardNamesCommand.Id, Large: false));

        AddButtons(ribbons["Part"], ManageTabId, commands, new Button(AssignSectionCommand.Id, Large: true));
    }

    private sealed record Button(string CommandId, bool Large);

    private static void AddButtons(Ribbon ribbon, string targetTabId, CommandRegistry commands, params Button[] buttons)
    {
        var tab = FindTab(ribbon, targetTabId);
        if (tab is not null)
        {
            // Своя вкладка из предыдущих версий аддина больше не нужна.
            FindTab(ribbon, OwnTabId)?.Delete();
        }
        else
        {
            tab = FindTab(ribbon, OwnTabId) ?? ribbon.RibbonTabs.Add(AddInInfo.Title, OwnTabId, AddInInfo.ClientId);
        }

        // Панель пересоздаётся, чтобы раскладка кнопок обновлялась вместе с аддином.
        FindPanel(tab)?.Delete();
        var panel = tab.RibbonPanels.Add(AddInInfo.Title, PanelId, AddInInfo.ClientId);
        foreach (var button in buttons)
        {
            panel.CommandControls.AddButton(commands[button.CommandId], button.Large, true);
        }
    }

    private static RibbonTab? FindTab(Ribbon ribbon, string internalName)
    {
        foreach (RibbonTab tab in ribbon.RibbonTabs)
        {
            if (tab.InternalName == internalName)
            {
                return tab;
            }
        }

        return null;
    }

    private static RibbonPanel? FindPanel(RibbonTab tab)
    {
        foreach (RibbonPanel panel in tab.RibbonPanels)
        {
            if (panel.InternalName == PanelId)
            {
                return panel;
            }
        }

        return null;
    }
}
