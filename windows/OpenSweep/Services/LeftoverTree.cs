using System.ComponentModel;
using Microsoft.Win32;
using OpenSweep.Core;

namespace OpenSweep.Services;

/// <summary>
/// Nodo del árbol de restos (como el de Revo): grupos con casilla de tres estados y hojas que son restos.
/// Marcar un grupo marca todo lo que contiene; el grupo queda a medias si solo hay parte marcada.
/// </summary>
public sealed class LeftoverNode : INotifyPropertyChanged
{
    public string Text { get; init; } = "";
    public string Detail { get; set; } = "";
    public string Icon { get; init; } = "";
    public Leftover? Item { get; init; }
    public List<LeftoverNode> Children { get; } = [];
    public LeftoverNode? Parent { get; private set; }
    public bool IsExpanded { get; set; } = true;
    public bool IsWarning => Item != null && (Item.Shared || Item.ByNameOnly);

    private bool? _checked;
    public bool? IsChecked
    {
        get => _checked;
        set => Set(value ?? false, down: true, up: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LeftoverNode Add(LeftoverNode child)
    {
        child.Parent = this;
        Children.Add(child);
        return child;
    }

    private void Set(bool? value, bool down, bool up)
    {
        if (_checked == value) return;
        _checked = value;
        if (Item != null && value != null) Item.Selected = value.Value;
        if (down && value != null) foreach (var c in Children) c.Set(value, down: true, up: false);
        if (up) Parent?.Refresh();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
    }

    /// <summary>Recalcula el estado de un grupo a partir de sus hijos.</summary>
    internal void Refresh()
    {
        var states = Children.Select(c => c._checked).Distinct().ToList();
        Set(states.Count == 1 ? states[0] : null, down: false, up: true);
    }

    /// <summary>Estado inicial de las hojas según Selected y de los grupos según sus hijos.</summary>
    internal void Init()
    {
        foreach (var c in Children) c.Init();
        if (Item != null) _checked = Item.Selected;
        else if (Children.Count > 0)
        {
            var states = Children.Select(c => c._checked).Distinct().ToList();
            _checked = states.Count == 1 ? states[0] : null;
        }
    }

    public IEnumerable<Leftover> Items() => Item != null ? [Item] : Children.SelectMany(c => c.Items());
}

public static class LeftoverTree
{
    private static string HiveName(RegistryHive h) => h == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE";

    /// <summary>Registro: clave → valores, agrupado por raíz. Además, servicios, tareas y reglas del firewall.</summary>
    public static List<LeftoverNode> BuildRegistry(IEnumerable<Leftover> items)
    {
        var roots = new List<LeftoverNode>();
        var reg = items.Where(i => i.IsRegistry && i.Special == null).ToList();
        foreach (var hive in reg.GroupBy(i => i.Hive).OrderBy(g => g.Key == RegistryHive.CurrentUser ? 0 : 1))
        {
            var root = new LeftoverNode { Text = HiveName(hive.Key), Icon = "" };
            foreach (var key in hive.GroupBy(i => (i.Path, i.View)).OrderBy(g => g.Key.Path, StringComparer.OrdinalIgnoreCase))
            {
                var suffix = key.Key.View == RegistryView.Registry32 ? Loc.T("  (32 bits)") : "";
                var whole = key.FirstOrDefault(i => i.ValueName == null);
                if (whole != null)
                {
                    root.Add(Leaf(whole, key.Key.Path + suffix, ""));
                    continue;
                }
                var keyNode = root.Add(new LeftoverNode { Text = key.Key.Path + suffix, Icon = "" });
                foreach (var v in key.OrderBy(i => i.ValueName, StringComparer.OrdinalIgnoreCase))
                    keyNode.Add(Leaf(v, v.ValueName!.Length == 0 ? Loc.T("(Predeterminado)") : v.ValueName, ""));
            }
            roots.Add(root);
        }

        var special = items.Where(i => i.Special != null).ToList();
        if (special.Count > 0)
        {
            var root = new LeftoverNode { Text = Loc.T("Servicios, tareas programadas y firewall"), Icon = "" };
            foreach (var g in special.GroupBy(i => i.Kind))
            {
                var kind = root.Add(new LeftoverNode { Text = g.Key, Icon = "" });
                foreach (var s in g) kind.Add(Leaf(s, s.Display, ""));
            }
            roots.Add(root);
        }
        foreach (var r in roots) { r.Init(); Count(r); }
        return roots;
    }

    /// <summary>Archivos y carpetas agrupados por la carpeta que los contiene.</summary>
    public static List<LeftoverNode> BuildFiles(IEnumerable<Leftover> items)
    {
        var roots = new List<LeftoverNode>();
        foreach (var g in items.Where(i => !i.IsRegistry && i.Special == null)
                     .GroupBy(i => Path.GetDirectoryName(i.Path.TrimEnd('\\')) ?? "", StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var root = new LeftoverNode { Text = g.Key, Icon = "" };
            foreach (var i in g.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase))
                root.Add(Leaf(i, Path.GetFileName(i.Path.TrimEnd('\\')), i.IsDirectory ? "" : ""));
            root.Detail = Util.FormatBytes(g.Sum(i => i.Size));
            roots.Add(root);
        }
        foreach (var r in roots) r.Init();
        return roots;
    }

    private static LeftoverNode Leaf(Leftover i, string text, string icon) => new()
    {
        Item = i, Text = text, Icon = icon,
        Detail = string.Join(" · ", new[] { i.SizeText, i.ReasonText }.Where(s => !string.IsNullOrEmpty(s))),
    };

    private static void Count(LeftoverNode n)
    {
        foreach (var c in n.Children) Count(c);
        if (n.Item == null && n.Children.Count > 0) n.Detail = $"{n.Items().Count()} {Loc.T("elementos")}";
    }
}
