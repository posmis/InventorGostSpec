using Inventor;
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

        AddButtons(ribbons["Assembly"], AssembleTabId, commands,
            AssignSectionCommand.Id,
            AutoAssignCommand.Id,
            SpecificationCommand.Id,
            WritePositionsCommand.Id);

        AddButtons(ribbons["Part"], ManageTabId, commands, AssignSectionCommand.Id);
    }

    private static void AddButtons(Ribbon ribbon, string targetTabId, CommandRegistry commands, params string[] commandIds)
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

        var panel = FindPanel(tab) ?? tab.RibbonPanels.Add(AddInInfo.Title, PanelId, AddInInfo.ClientId);

        var existing = new HashSet<string>();
        foreach (CommandControl control in panel.CommandControls)
        {
            existing.Add(control.InternalName);
        }

        foreach (var id in commandIds)
        {
            if (!existing.Contains(id))
            {
                panel.CommandControls.AddButton(commands[id], false, true);
            }
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
