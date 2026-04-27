using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace FullStatGUI;

/// <summary>Scrollable character-screen panel (same pattern as DeathWounds / TraitCore) showing agreed stat + attribute list with ~200ms refresh.</summary>
public sealed class FullStatGuiClientSystem : ModSystem
{
    public const string ComposerKey = "fullstatgui-panel";
    private const int ApproxLinePixel = 24;
    private int visibleLineCount = 18;
    private int clipHeight = 18 * 24;
    private const int ApproxCharsPerRow = 50;
    private const int YUnderTitle = 28;
    private const int ClipW = 450;
    private const int PanelGap = 12;

    private const string DynamicTextKey = "fullstatguitext";
    private const string ScrollbarKey = "fullstatgui-sb";

    private ICoreClientAPI? capi;
    private GuiDialogCharacterBase? charDlg;
    private long tickListener;
    private int lineOffset;
    private List<string> allLines = [];
    private string lastRenderSig = "";
    private float lastScrollbarValue = -1f;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        tickListener = api.Event.RegisterGameTickListener(OnClientTick, 200, 0);
    }

    public override void Dispose()
    {
        if (capi is not null)
        {
            capi.Event.UnregisterGameTickListener(tickListener);
        }

        DetachFromDialog();
    }

    private void OnClientTick(float _dt)
    {
        if (capi is null)
        {
            return;
        }

        if (charDlg is not null)
        {
            if (!IsDialogStillLoaded(charDlg))
            {
                DetachFromDialog();
            }
            else
            {
                if (HasOurComposer() && charDlg?.Composers is { } c && c[ComposerKey] is not null)
                {
                    var next = BuildAllLines();
                    var sig = ToSignature(next);
                    if (sig != lastRenderSig)
                    {
                        allLines = next;
                        lastRenderSig = sig;
                        int maxOff = Math.Max(0, allLines.Count - visibleLineCount);
                        lineOffset = Math.Clamp(lineOffset, 0, maxOff);
                        if (c[ComposerKey] is GuiComposer compoR)
                        {
                            SyncHeightsAndScrollbarToLineOffset(compoR);
                            UpdateBodyText(compoR);
                        }
                    }
                    else
                    {
                        SyncScrollbar();
                    }
                }
                else
                {
                    SyncScrollbar();
                }

                return;
            }
        }

        for (int i = 0; i < capi.Gui.LoadedGuis.Count; i++)
        {
            if (capi.Gui.LoadedGuis[i] is not GuiDialogCharacterBase found)
            {
                continue;
            }

            charDlg = found;
            charDlg.ComposeExtraGuis += OnComposeExtraGuis;
            charDlg.OnClosed += OnCharDialogClosed;
            return;
        }
    }

    private static string ToSignature(List<string> lines)
    {
        var sb = new StringBuilder(lines.Count * 48);
        foreach (string t in lines)
        {
            sb.Append(t);
            sb.Append('\u241F');
        }
        return sb.ToString();
    }

    private bool HasOurComposer()
    {
        if (charDlg?.Composers is not { } c)
        {
            return false;
        }

        try
        {
            return c[ComposerKey] is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool IsDialogStillLoaded(GuiDialogCharacterBase dlg)
    {
        if (capi is null)
        {
            return false;
        }

        for (int i = 0; i < capi.Gui.LoadedGuis.Count; i++)
        {
            if (ReferenceEquals(capi.Gui.LoadedGuis[i], dlg))
            {
                return true;
            }
        }

        return false;
    }

    private void OnCharDialogClosed()
    {
        lastRenderSig = "";
        allLines = [];
        lineOffset = 0;
        DetachFromDialog();
    }

    private void DetachFromDialog()
    {
        if (charDlg is not null)
        {
            charDlg.ComposeExtraGuis -= OnComposeExtraGuis;
            charDlg.OnClosed -= OnCharDialogClosed;
        }

        charDlg = null;
    }

    private void OnComposeExtraGuis()
    {
        if (capi is null || charDlg is null)
        {
            return;
        }

        var composers = charDlg.Composers;
        if (composers is null || composers["playercharacter"] is null)
        {
            return;
        }

        allLines = BuildAllLines();
        lastRenderSig = ToSignature(allLines);
        lineOffset = 0;

        ElementBounds left = composers["playercharacter"]!.Bounds;
        ElementBounds? env = composers["environment"]?.Bounds;

        CairoFont bodyFont = CairoFont.WhiteSmallText().WithLineHeightMultiplier(1.2);
        double gscale = RuntimeEnv.GUIScale;
        double leftH = left.OuterHeight / gscale;
        clipHeight = (int)Math.Clamp(leftH - 32, 400, 720);
        visibleLineCount = Math.Max(12, (clipHeight - 6) / ApproxLinePixel);
        clipHeight = visibleLineCount * ApproxLinePixel + 6;

        ElementBounds textBounds = ElementBounds.Fixed(0, YUnderTitle, ClipW, clipHeight);
        ElementBounds scrollbarBounds = ElementStdBounds.VerticalScrollbar(textBounds);
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        textBounds = textBounds.WithParent(bgBounds);
        scrollbarBounds = scrollbarBounds.WithParent(bgBounds);
        _ = bgBounds.WithChildren(textBounds, scrollbarBounds);

        double offsetY = env is not null
            ? (env.renderY - left.renderY + env.OuterHeight) / RuntimeEnv.GUIScale + PanelGap
            : (left.OuterHeight / RuntimeEnv.GUIScale) + 8;

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.None)
            .WithFixedPosition(left.renderX / RuntimeEnv.GUIScale, left.renderY / RuntimeEnv.GUIScale + offsetY);

        GuiComposer compo = capi.Gui
            .CreateCompo(ComposerKey, dialogBounds)
            .AddShadedDialogBG(bgBounds, true, 0, 0.5f)
            .AddDialogTitleBar("Full stat readout (vanilla+)", () => charDlg?.TryClose())
            .BeginChildElements(bgBounds)
            .AddDynamicText(VisibleChunk(), bodyFont, textBounds, DynamicTextKey)
            .AddVerticalScrollbar(OnScroll, scrollbarBounds, ScrollbarKey)
            .EndChildElements()
            .Compose();

        if (compo.GetScrollbar(ScrollbarKey) is { } sc)
        {
            sc.SetHeights(visibleLineCount, Math.Max(visibleLineCount, allLines.Count));
            sc.SetScrollbarPosition(lineOffset);
            lastScrollbarValue = sc.CurrentYPosition * sc.ScrollConversionFactor;
        }

        composers[ComposerKey] = compo;
    }

    private void SyncHeightsAndScrollbarToLineOffset(GuiComposer composer)
    {
        if (composer.GetScrollbar(ScrollbarKey) is not { } sc)
        {
            return;
        }

        sc.SetHeights(visibleLineCount, Math.Max(visibleLineCount, allLines.Count));
        sc.SetScrollbarPosition(lineOffset);
        lastScrollbarValue = sc.CurrentYPosition * sc.ScrollConversionFactor;
    }

    private void OnScroll(float newValue)
    {
        if (charDlg?.Composers is not { } c || c[ComposerKey] is not GuiComposer composer)
        {
            return;
        }

        int maxOffset = Math.Max(0, allLines.Count - visibleLineCount);
        float raw = newValue;
        if (raw <= 1.001f && maxOffset > 1)
        {
            raw *= maxOffset;
        }

        lineOffset = (int)Math.Clamp(MathF.Round(raw), 0, maxOffset);
        if (composer.GetScrollbar(ScrollbarKey) is { } sbar)
        {
            lastScrollbarValue = sbar.CurrentYPosition * sbar.ScrollConversionFactor;
        }

        UpdateBodyText(composer);
    }

    private void SyncScrollbar()
    {
        if (charDlg?.Composers is not { } c || c[ComposerKey] is not GuiComposer composer)
        {
            return;
        }

        if (composer.GetScrollbar(ScrollbarKey) is not { } sc)
        {
            return;
        }

        float raw = sc.CurrentYPosition * sc.ScrollConversionFactor;
        if (MathF.Abs(raw - lastScrollbarValue) < 0.001f)
        {
            return;
        }

        lastScrollbarValue = raw;
        int maxOffset = Math.Max(0, allLines.Count - visibleLineCount);
        if (raw <= 1.001f && maxOffset > 1)
        {
            raw *= maxOffset;
        }

        int ne = (int)Math.Clamp(MathF.Round(raw), 0, maxOffset);
        if (ne == lineOffset)
        {
            return;
        }

        lineOffset = ne;
        UpdateBodyText(composer);
    }

    private void UpdateBodyText(GuiComposer composer)
    {
        if (composer.GetDynamicText(DynamicTextKey) is { } dtxt)
        {
            dtxt.SetNewText(VisibleChunk(), false, true, false);
        }
    }

    private string VisibleChunk()
    {
        if (allLines.Count == 0)
        {
            return "(no player entity / no data yet)";
        }

        int start = Math.Clamp(lineOffset, 0, Math.Max(0, allLines.Count - 1));
        int count = Math.Min(visibleLineCount, allLines.Count - start);
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(allLines[start + i]);
        }

        return sb.ToString();
    }

    private List<string> BuildAllLines()
    {
        if (capi?.World?.Player?.Entity is not { } ent)
        {
            return [ "(no local player entity)" ];
        }

        var lines = new List<string>(128);
        lines.Add("FullStatGUI — blended stats & watched attributes. Refresh: ~0.2s");
        lines.Add("");

        try
        {
            AppendLine(lines, "animalHarvestingTime", P(ent, "animalHarvestingTime"));
            AppendLine(lines, "animalLootDropRate", P(ent, "animalLootDropRate"));
            AppendLine(lines, "animalSeekingRange", P(ent, "animalSeekingRange"));
            AppendLine(lines, "armorDurabilityLoss", P(ent, "armorDurabilityLoss"));
            AppendLine(lines, "armorWalkSpeedAffectedness", P(ent, "armorWalkSpeedAffectedness"));
            AddBodyTempLines(lines, ent);
            AppendLine(lines, "bowDrawingStrength", P(ent, "bowDrawingStrength"));
            AppendLine(lines, "entityDead", ent.WatchedAttributes.GetInt("entityDead", 0).ToString(CultureInfo.InvariantCulture));
            AppendLine(lines, "forageDropRate", P(ent, "forageDropRate"));
            AppendLine(lines, "forageLootDropMul", P(ent, "forageLootDropMul"));
            AppendLine(lines, "healingeffectivness", P(ent, "healingeffectivness"));
            lines.Add(HealthLine(ent));
            AppendLine(lines, "hungerrate", P(ent, "hungerrate"));
            AddHungerSection(lines, ent);
            AppendLine(lines, "maxhealthExtraPoints", FPlain(ent, "maxhealthExtraPoints"));
            AppendLine(lines, "mechanicalsDamage", P(ent, "mechanicalsDamage"));
            AppendLine(lines, "meleeWeaponsDamage", P(ent, "meleeWeaponsDamage"));
            AppendLine(lines, "meleeWeaponsDamageMul", P(ent, "meleeWeaponsDamageMul"));
            AppendLine(lines, "miningSpeedMul", P(ent, "miningSpeedMul"));
            AddOnFireSection(lines, ent);
            AppendLine(lines, "oreDropRate", P(ent, "oreDropRate"));
            AppendLine(lines, "oreLootDropMul", P(ent, "oreLootDropMul"));
            AppendLine(lines, "rangedWeaponsAcc", P(ent, "rangedWeaponsAcc"));
            AppendLine(lines, "rangedWeaponsDamage", P(ent, "rangedWeaponsDamage"));
            AppendLine(lines, "rangedWeaponsSpeed", P(ent, "rangedWeaponsSpeed"));
            AppendLine(lines, "rangedWeaponsSpeedMul", P(ent, "rangedWeaponsSpeedMul"));
            AppendLine(lines, "rustyGearDropRate", P(ent, "rustyGearDropRate"));
            AppendLine(lines, "sprintSpeed", P(ent, "sprintSpeed"));
            AppendLine(lines, "temporalGearTLRepairCost", P(ent, "temporalGearTLRepairCost"));
            AppendLine(lines, "vesselContentsDropRate", P(ent, "vesselContentsDropRate"));
            AppendLine(lines, "walkspeed", P(ent, "walkspeed"));
            AppendLine(lines, "wetness", WetnessPercent(ent));
            AppendLine(lines, "wholeVesselLootChance", P(ent, "wholeVesselLootChance"));
            AppendLine(lines, "wildCropDropRate", P(ent, "wildCropDropRate"));
        }
        catch (Exception ex)
        {
            lines.Add("Build error: " + ex.GetType().Name + " " + ex.Message);
        }

        return lines;
    }

    private static void AppendLine(List<string> lines, string label, string value)
    {
        AddWrappedRows(lines, label + ": " + value);
    }

    private static string P(Entity e, string code)
    {
        try
        {
            float v = e.Stats.GetBlended(code);
            return (v * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }
        catch
        {
            return "—";
        }
    }

    private static string FPlain(Entity e, string code)
    {
        try
        {
            float v = e.Stats.GetBlended(code);
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "—";
        }
    }

    private static string HealthLine(Entity e)
    {
        ITreeAttribute? t = e.WatchedAttributes.GetTreeAttribute("health");
        if (t is null)
        {
            return "health (current / max): — (no health tree)";
        }

        float cur = t.GetFloat("currenthealth", 0f);
        float max = t.GetFloat("maxhealth", 0f);
        return string.Format(
            CultureInfo.InvariantCulture,
            "health (current / max): {0:0.##} / {1:0.##}",
            cur,
            max);
    }

    private static void AddBodyTempLines(List<string> lines, Entity e)
    {
        ITreeAttribute? t = e.WatchedAttributes.GetTreeAttribute("bodyTemp");
        if (t is null)
        {
            AppendLine(lines, "body temperature", "— (no bodyTemp tree)");
            return;
        }

        float raw = t.GetFloat("bodytemp", 0f);
        float forVanillaText = raw;
        if (forVanillaText > 37f)
        {
            forVanillaText = 37f + (forVanillaText - 37f) / 10f;
        }

        AppendLine(
            lines,
            "body temperature (like vanilla Stats text)",
            string.Format(CultureInfo.InvariantCulture, "{0:0.#}°C", forVanillaText));
    }

    private static string WetnessPercent(Entity e)
    {
        float w = e.WatchedAttributes.GetFloat("wetness", 0f);
        return (w * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static void AddOnFireSection(List<string> lines, Entity e)
    {
        if (!e.WatchedAttributes.HasAttribute("onFire"))
        {
            lines.Add("onFire: (absent)");
            return;
        }

        lines.Add("onFire (subtree):");
        IAttribute? a = e.WatchedAttributes["onFire"];
        if (a is ITreeAttribute fire)
        {
            AppendTreeForDisplay(fire, lines, "  ", 0, 2);
        }
        else
        {
            AddWrappedRows(lines, "  " + FormatIAttributeValue(a));
        }
    }

    private static void AddHungerSection(List<string> lines, Entity e)
    {
        lines.Add("--- hunger (entire sub-tree) ---");
        ITreeAttribute? t = e.WatchedAttributes.GetTreeAttribute("hunger");
        if (t is null)
        {
            lines.Add("  (no hunger tree on entity)");
            return;
        }

        AppendTreeForDisplay(t, lines, "  ", 0, 6);
    }

    private static void AppendTreeForDisplay(ITreeAttribute t, List<string> lines, string indent, int depth, int maxDepth)
    {
        if (t is not TreeAttribute tr)
        {
            AddWrappedRows(lines, indent + "(non-Tree ITree, Count=" + t.Count + "): " + t);
            return;
        }

        if (depth >= maxDepth)
        {
            AddWrappedRows(lines, indent + "…(max display depth " + maxDepth + ")");
            return;
        }

        if (tr.Count < 1)
        {
            AddWrappedRows(lines, indent + "(empty tree)");
            return;
        }

        IOrderedEnumerable<string> names = tr.Keys.OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        foreach (string k in names)
        {
            IAttribute? a = tr.GetAttribute(k);
            if (a is ITreeAttribute sub)
            {
                AddWrappedRows(lines, indent + k + ":");
                AppendTreeForDisplay(sub, lines, indent + "  ", depth + 1, maxDepth);
            }
            else
            {
                AddWrappedRows(lines, indent + k + ": " + FormatIAttributeValue(a));
            }
        }
    }

    private static string FormatIAttributeValue(IAttribute? a)
    {
        if (a is null)
        {
            return "null";
        }

        if (a is ITreeAttribute)
        {
            return "[tree: use child keys]";
        }

        object? o;
        try
        {
            o = a.GetValue();
        }
        catch
        {
            return a.ToString() ?? "";
        }

        return o switch
        {
            null => "null",
            float f => f.ToString("0.####", CultureInfo.InvariantCulture),
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            string s => s,
            byte[] bytes => "bytes(" + bytes.Length + ")",
            _ => o.ToString() ?? ""
        };
    }

    private static void AddWrappedRows(List<string> lines, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (current.Length == 0)
            {
                current.Append(w);
                continue;
            }

            if (current.Length + 1 + w.Length > ApproxCharsPerRow)
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(w);
            }
            else
            {
                current.Append(' ');
                current.Append(w);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }
    }
}
