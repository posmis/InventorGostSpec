using Inventor;
using InventorGostSpec.AddIn.Commands;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>Вкладка «Спецификация ГОСТ» на лентах сборки и детали.</summary>
internal static class RibbonBuilder
{
    private const string TabId = "InventorGostSpec.Tab";
    private const string PanelId = "InventorGostSpec.Panel";

    public static void Build(Inventor.Application application, CommandRegistry commands)
    {
        var ribbons = application.UserInterfaceManager.Ribbons;

        AddButtons(ribbons["Assembly"], commands,
            AssignSectionCommand.Id,
            AutoAssignCommand.Id,
            UpdateSpecificationCommand.Id,
            WritePositionsCommand.Id);

        AddButtons(ribbons["Part"], commands, AssignSectionCommand.Id);
    }

    private static void AddButtons(Ribbon ribbon, CommandRegistry commands, params string[] commandIds)
    {
        var tab = FindTab(ribbon) ?? ribbon.RibbonTabs.Add(AddInInfo.Title, TabId, AddInInfo.ClientId);
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

    private static RibbonTab? FindTab(Ribbon ribbon)
    {
        foreach (RibbonTab tab in ribbon.RibbonTabs)
        {
            if (tab.InternalName == TabId)
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
