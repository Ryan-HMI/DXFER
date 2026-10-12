using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DXFER.Blazor.Components;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: DXFER.IconCatalog <output-directory> <source-ref>");
    return 2;
}

const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
var workbench = new DrawingWorkbench();
typeof(DrawingWorkbench).GetProperty("ToolHotkeys", flags)!.SetValue(workbench, new ToolHotkeyService());
var appearances = new List<Appearance>();
foreach (var (property, mode) in new[] { ("AllToolGroups", "Full editor"), ("ProductionToolGroups", "Cleanup-only") })
{
    var groups = (IReadOnlyList<WorkbenchToolGroup>)typeof(DrawingWorkbench).GetProperty(property, flags)!.GetValue(workbench)!;
    foreach (var group in groups)
    {
        foreach (var command in group.Commands)
        {
            string? prompt = null;
            if (command.Tool is not null)
            {
                typeof(DrawingWorkbench).GetField("_activeTool", flags)!.SetValue(workbench, command.Tool);
                prompt = (string?)typeof(DrawingWorkbench).GetProperty("CommandPromptText", flags)!.GetValue(workbench);
                if (prompt?.Contains("follow canvas prompts", StringComparison.Ordinal) == true)
                    prompt = null;
            }

            appearances.Add(new(mode, group.Name, command.Id.ToString(), command.Label,
                command.Icon.ToString(), command.Tool?.ToString(), command.TooltipText, prompt, command.IsFuture));
        }
    }
}

// These are not palette metadata: keep audited non-palette controls explicit.
var chrome = new[]
{
    new Chrome("ChevronUp", "Collapse docked tool group", "WorkbenchToolPalette.razor", "Upward chevron; collapse the group's commands, retaining its header.", true),
    new Chrome("ChevronDown", "Expand docked tool group", "WorkbenchToolPalette.razor", "Downward chevron; show the group's commands.", true),
    new Chrome("ApplicationMenu", "Open application menu", "MainLayout.razor", "Three horizontal menu bars; currently CSS, not CadIconName.", false),
    new Chrome("SubmenuRight", "Open File/Cleanup submenu", "MainLayout.razor", "Right chevron; currently a text greater-than symbol.", false),
    new Chrome("Close", "Close dialog / dismiss error", "MainLayout.razor; ToolHotkeyOptions.razor", "Simple X; shared asset across dismiss/close controls. Currently text.", false),
    new Chrome("DragGrip", "Move or dock tool group", "WorkbenchToolPalette.razor.css", "Compact grip marks; currently CSS, not a command enum.", false)
};

var commands = Enum.GetValues<WorkbenchCommandId>().Select(id => new
{
    Id = id.ToString(),
    Name = ToolHotkeyResolver.FormatCommandName(id),
    PaletteAppearances = appearances.Where(a => a.Command == id.ToString()).ToArray()
}).ToArray();
var icons = Enum.GetValues<CadIconName>().Select(icon => new
{
    Id = icon.ToString(),
    Filename = Kebab(icon.ToString()) + ".png",
    PaletteAppearances = appearances.Where(a => a.Icon == icon.ToString()).ToArray(),
    ChromeUses = chrome.Where(c => c.IsEnumIcon && c.Id == icon.ToString()).ToArray()
}).ToArray();
var data = new
{
    SourceRef = args[1],
    Scope = "All command/icon enum members and both live palette definitions, plus audited non-palette chrome. Presence is not implementation or user-acceptance certification.",
    Sources = new[] { "WorkbenchCommandId.cs", "CadIconName.cs", "DrawingWorkbench.razor.cs", "WorkbenchToolPalette.razor", "MainLayout.razor", "ToolHotkeyOptions.razor" },
    Counts = new { Commands = commands.Length, EnumIcons = icons.Length, PaletteCommands = commands.Count(c => c.PaletteAppearances.Length > 0), PaletteIcons = icons.Count(i => i.PaletteAppearances.Length > 0), ExtraChromeAssets = chrome.Count(c => !c.IsEnumIcon) },
    Commands = commands,
    Icons = icons,
    Chrome = chrome
};
Directory.CreateDirectory(args[0]);
File.WriteAllText(Path.Combine(args[0], "dxfer-icon-catalog.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }) + "\n");

var md = new StringBuilder();
md.AppendLine("# DXFer Command and Icon Catalog\n");
md.AppendLine($"Source: `{args[1]}`. Generated from the current application, not the old `dxfer-buttons.json`.\n");
md.AppendLine($"**{commands.Length} command IDs; {icons.Length} named icon assets; {data.Counts.PaletteCommands} commands and {data.Counts.PaletteIcons} unique icons in the palettes; {data.Counts.ExtraChromeAssets} additional chrome assets.**\n");
md.AppendLine("Full-editor and cleanup-only appearances are both included. Repeated labels/groups reuse one asset. 'Future' is the code's explicit flag; selection/document-dependent disabled buttons are NOT classified as unimplemented. Existing known editor defects are outside this inventory.\n");
md.AppendLine("## Image-generation handoff\n");
md.AppendLine("Generate one transparent PNG per unique asset ID, using the filenames below. Work in small families so the style stays consistent. Do not generate a single large atlas as the only deliverable. Keep labels and button backgrounds outside the bitmap. Use a consistent square canvas, centered silhouette, generous consistent padding, crisp high-contrast marks and minimal detail. No shadows, bevels, decorative backgrounds or tiny text. Create 256px masters, then inspect/export at 24px and 32px (plus 2x sizes); a large attractive rendering is not enough if it becomes muddy at toolbar size. Preserve genuine symbol differences, especially midpoint/endpoints, CW/CCW, inscribed/circumscribed, tangent/concentric and X/Y. Selected/disabled styling belongs to the application. Provide appropriate light/dark foreground variants if the assets cannot adapt to theme. This is an asset brief, not an instruction to replace the app UI yet.\n");

foreach (var group in appearances.Where(a => a.Mode == "Full editor").GroupBy(a => a.Group))
{
    md.AppendLine($"## {group.Key}\n");
    md.AppendLine("| Command ID | Button | Asset | State | Purpose / interaction |\n|---|---|---|---|---|");
    foreach (var a in group)
        md.AppendLine($"| `{a.Command}` | {Cell(a.Label)} | `{Kebab(a.Icon)}.png` | {(a.Future ? "Future / disabled" : "Exposed; may require context")} | {Cell(a.Prompt ?? a.Tooltip)} |");
    md.AppendLine();
}
md.AppendLine("## Complete Asset Checklist\n");
md.AppendLine("One row per CadIconName, including retained icons that are not currently exposed. A reserved icon is not proof that its command works.\n");
md.AppendLine("| Asset ID | Filename | Current use |\n|---|---|---|");
foreach (var i in icons)
{
    var uses = i.PaletteAppearances.Select(a => a.Label).Concat(i.ChromeUses.Select(c => c.Label)).Distinct().ToArray();
    md.AppendLine($"| `{i.Id}` | `{i.Filename}` | {Cell(uses.Length == 0 ? "Retained icon; not exposed by current palettes/chrome" : string.Join("; ", uses))} |");
}
md.AppendLine("\n## Controls Outside the Palette\n");
foreach (var c in chrome)
    md.AppendLine($"- `{Kebab(c.Id)}.png`: **{c.Label}**. {c.Purpose} Source: `{c.Source}`.");
md.AppendLine("\nCurrent text-only actions also exist: Open local DXF/DWG, Connect to Sync, Download DXF, Save to Sync, Connect, Cancel, Reset hotkeys and Close. They do not all have icon assignments; do not mistake proposed future artwork for existing bindings. Canvas geometry, grip points, measurement arrows and constraint annotations are dynamic drawing content, not static toolbar image assets.\n");
md.AppendLine("## Command IDs Not in Either Palette\n");
foreach (var c in commands.Where(c => c.PaletteAppearances.Length == 0))
    md.AppendLine($"- `{c.Id}` ({c.Name}): not in either palette; see the audited notes below. No icon binding is inferred by name.");
md.AppendLine("\nAudited notes: SaveDxf is Download DXF in the File menu. SendToSync is Save to Sync in the menu/Sync bar. NewBlankDocument and LoadSample retain handlers but are absent from the current menu. ExportDxfText is retired and points users to Download DXF. ReturnToSync is a retained ID, not a current visible control. TangentArc/Bezier/Pierce/Curvature are absent from the current palette; retained names/icons must not be taken as validated tool availability.\n");
File.WriteAllText(Path.Combine(args[0], "dxfer-icon-catalog.md"), md.ToString().TrimEnd() + "\n");
Console.WriteLine(JsonSerializer.Serialize(data.Counts));
return 0;

static string Kebab(string value) => Regex.Replace(value, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();
static string Cell(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
sealed record Appearance(string Mode, string Group, string Command, string Label, string Icon, string? Tool, string Tooltip, string? Prompt, bool Future);
sealed record Chrome(string Id, string Label, string Source, string Purpose, bool IsEnumIcon);
