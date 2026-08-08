using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace RudeUI;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/rudeui";
    private static readonly string[] NativeAddons = ["_ParameterWidget", "TargetInfo", "TargetInfoCastBar", "TargetInfoMainTarget", "TargetInfoBuffDebuff"];

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IObjectTable objects;
    private readonly ITargetManager targets;
    private readonly ICondition conditions;
    private readonly IDataManager data;
    private readonly IGameGui gameGui;
    private Configuration config;
    private bool configOpen;
    private bool nativeFramesHidden;
    private bool nativeCastBarHidden;
    private bool positionsDirty;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IObjectTable objects,
        ITargetManager targets, ICondition conditions, IDataManager data, IGameGui gameGui)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.objects = objects;
        this.targets = targets;
        this.conditions = conditions;
        this.data = data;
        this.gameGui = gameGui;
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Version < 2)
        {
            config.HostileIdleHealthColor = Rgba(242, 199, 94);
            config.HostileEngagedHealthColor = Rgba(255, 96, 108);
            config.Version = 2;
            pluginInterface.SavePluginConfig(config);
        }

        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open RudeUI settings. Use /rudeui lock to toggle movement." });
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        pluginInterface.UiBuilder.OpenMainUi += OpenConfig;
    }

    public string Name => "RudeUI";

    public void Dispose()
    {
        SetNativeFramesVisible(true);
        SetNativeCastBarVisible(true);
        commands.RemoveHandler(Command);
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        pluginInterface.UiBuilder.OpenMainUi -= OpenConfig;
    }

    private void OnCommand(string _, string arguments)
    {
        if (arguments.Trim().Equals("lock", StringComparison.OrdinalIgnoreCase))
        {
            config.Locked = !config.Locked;
            Save();
        }
        else configOpen = true;
    }

    private void OpenConfig() => configOpen = true;
    private void Save() => pluginInterface.SavePluginConfig(config);

    private void Draw()
    {
        SetNativeFramesVisible(!config.Enabled || !config.HideNativeFrames);
        SetNativeCastBarVisible(!config.Enabled || !config.HideNativeCastBar);
        if (config.Enabled && objects.LocalPlayer is { } player && (!config.HideOutOfCombat || conditions[ConditionFlag.InCombat]))
        {
            var (playerPosition, playerMoved) = DrawUnitWindow("###RudeUIPlayer", player, true, config.PlayerX, config.PlayerY);
            config.PlayerX = playerPosition.X;
            config.PlayerY = playerPosition.Y;
            var moved = playerMoved;
            if (targets.Target is ICharacter target)
            {
                var (targetPosition, targetMoved) = DrawUnitWindow("###RudeUITarget", target, false, config.TargetX, config.TargetY);
                config.TargetX = targetPosition.X;
                config.TargetY = targetPosition.Y;
                moved |= targetMoved;
            }

            InitializeCastPositions();
            if (config.ShowPlayerCast)
            {
                var (position, castMoved) = DrawCastWindow("###RudeUIPlayerCast", player, "Player cast preview",
                    config.PlayerCastX, config.PlayerCastY, config.PlayerCastWidth, config.PlayerCastHeight, false);
                config.PlayerCastX = position.X;
                config.PlayerCastY = position.Y;
                moved |= castMoved;
            }
            if (config.ShowTargetCast)
            {
                var targetCaster = targets.Target as IBattleChara;
                var (position, castMoved) = DrawCastWindow("###RudeUITargetCast", targetCaster, "Target cast preview",
                    config.TargetCastX, config.TargetCastY, config.TargetCastWidth, config.TargetCastHeight, true);
                config.TargetCastX = position.X;
                config.TargetCastY = position.Y;
                moved |= castMoved;
            }
            positionsDirty |= moved;
            if (positionsDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                positionsDirty = false;
                Save();
            }
        }
        DrawConfig();
    }

    private (Vector2 Position, bool Moved) DrawUnitWindow(string id, ICharacter unit, bool player, float x, float y)
    {
        var width = Math.Clamp(config.FrameWidth, 200f, 650f);
        var height = Math.Clamp(config.FrameHeight, 50f, 150f);
        var scale = height / 76f;
        var hasLowerBar = player && config.ShowMp;
        var hasTargetOfTarget = !player && config.ShowTargetOfTarget && GetTargetOf(unit) != null;
        var contentHeight = hasLowerBar ? height : height * 65f / 76f;
        var size = new Vector2(width, contentHeight + (hasTargetOfTarget ? 10f * scale : 0f));
        if (!config.PositionsInitialized)
        {
            var display = ImGui.GetIO().DisplaySize;
            config.PlayerX = display.X * .5f - width - 60f;
            config.TargetX = display.X * .5f + 60f;
            config.PlayerY = config.TargetY = display.Y * .70f;
            config.PositionsInitialized = true;
            Save();
        }

        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav |
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin(id, flags);
        var newPos = ImGui.GetWindowPos();
        var moved = false;
        ImGui.InvisibleButton($"{id}InteractionSurface", size);
        var hovered = ImGui.IsItemHovered();
        if (!config.Locked && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            newPos += ImGui.GetIO().MouseDelta;
            ImGui.SetWindowPos(newPos);
            x = newPos.X;
            y = newPos.Y;
            moved = true;
        }
        if (hovered)
        {
            ImGui.SetMouseCursor(config.Locked ? ImGuiMouseCursor.Hand : ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var clickedUnit = unit;
                if (!player && config.ShowTargetOfTarget && GetTargetOf(unit) is { } targetOfTarget)
                {
                    var mouse = ImGui.GetMousePos();
                    var totWidth = Math.Min(size.X * .48f, 220f * scale);
                    var totMin = new Vector2(newPos.X + size.X - 10f * scale - totWidth, newPos.Y + size.Y - 18f * scale);
                    var totMax = totMin + new Vector2(totWidth, 14f * scale);
                    if (mouse.X >= totMin.X && mouse.X <= totMax.X && mouse.Y >= totMin.Y && mouse.Y <= totMax.Y)
                        clickedUnit = targetOfTarget;
                }
                targets.Target = clickedUnit;
            }
        }
        DrawFrame(ImGui.GetWindowDrawList(), newPos, size, unit, player, scale);
        if (hovered && config.Locked)
            ImGui.GetWindowDrawList().AddRect(newPos, newPos + size, Rgba(230, 218, 190, 75), 2f * scale);
        ImGui.End();
        ImGui.PopStyleVar();
        return (new Vector2(x, y), moved);
    }

    private void DrawFrame(ImDrawListPtr draw, Vector2 p, Vector2 size, ICharacter unit, bool player, float s)
    {
        var rightAligned = !player;
        var hp = unit.MaxHp == 0 ? 0f : Math.Clamp((float)unit.CurrentHp / unit.MaxHp, 0f, 1f);
        var brass = Rgba(164, 140, 92);
        var text = Rgba(244, 240, 229);
        var muted = Rgba(196, 190, 176);
        var barP = p + new Vector2(10, 31) * s;
        var barSize = new Vector2(size.X / s - 20, 19) * s;
        var hostile = !player && IsHostileDisposition(unit);
        var engaged = hostile && IsInCombat(unit);
        var (hpLow, hpHigh, nameColor) = GetHealthPalette(player, hostile, engaged);
        if (player) nameColor = text;

        var name = unit.Name.TextValue;
        DrawNativeText(draw, p + new Vector2(rightAligned ? size.X / s - 10 : 10, 7) * s,
            name, nameColor, 1.05f * s, rightAligned);
        var context = unit is IPlayerCharacter pc
            ? $"Lv {pc.Level}  {pc.ClassJob.Value.Abbreviation}"
            : $"Lv {unit.Level}";
        DrawShadowText(draw, p + new Vector2(rightAligned ? 10 : size.X / s - 10, 9) * s, context, muted, .86f * s, !rightAligned);

        DrawBar(draw, barP, barSize, hp, Rgba(18, 21, 17, 211), hpLow, hpHigh, s);
        var hpText = $"{FormatCompact(unit.CurrentHp)} / {FormatCompact(unit.MaxHp)}  ({hp * 100f:0}%)";
        DrawCenteredText(draw, barP, barSize, hpText, text, .84f * s);

        // XIV's HUD uses small metallic flourishes to define a widget rather than a full box.
        var accentStart = rightAligned ? barP.X + barSize.X : barP.X;
        draw.AddLine(new Vector2(accentStart, barP.Y - 3f * s),
            new Vector2(accentStart + (rightAligned ? -1 : 1) * 48f * s, barP.Y - 3f * s), brass, s);

        if (player && config.ShowMp)
        {
            var mp = unit.MaxMp == 0 ? 0f : Math.Clamp((float)unit.CurrentMp / unit.MaxMp, 0f, 1f);
            var mpP = p + new Vector2(10, 55) * s;
            var mpSize = new Vector2(size.X / s - 20, 7) * s;
            DrawBar(draw, mpP, mpSize, mp, Rgba(16, 20, 24, 211), Rgba(48, 83, 112), Rgba(91, 151, 184), s, false);
            DrawShadowText(draw, mpP + new Vector2(rightAligned ? mpSize.X / s : 0, 8) * s,
                $"MP  {unit.CurrentMp:N0}", muted, .72f * s, rightAligned);
        }
        if (!player && config.ShowTargetOfTarget && GetTargetOf(unit) is { } tot)
        {
            var totWidth = Math.Min(size.X * .48f, 220f * s);
            var totP = new Vector2(p.X + size.X - 10f * s - totWidth, p.Y + size.Y - 18f * s);
            var totSize = new Vector2(totWidth, 14f * s);
            var totHp = tot.MaxHp == 0 ? 0f : Math.Clamp((float)tot.CurrentHp / tot.MaxHp, 0f, 1f);
            var totHostile = IsHostileDisposition(tot);
            var totEngaged = totHostile && IsInCombat(tot);
            var (totLow, totHigh, _) = GetHealthPalette(false, totHostile, totEngaged);
            DrawBar(draw, totP, totSize, totHp, Rgba(17, 19, 16, 211), totLow, totHigh, s, false);

            var nameLength = Math.Clamp((int)(totWidth / s - 48f) / 7, 6, 22);
            var totName = Truncate(tot.Name.TextValue, nameLength);
            DrawShadowText(draw, totP + new Vector2(5, 0) * s, totName, text, .76f * s, false);
            DrawShadowText(draw, new Vector2(totP.X + totSize.X - 5f * s, totP.Y), $"{totHp * 100f:0}%", text, .76f * s, true);
        }
    }

    private (Vector2 Position, bool Moved) DrawCastWindow(string id, IBattleChara? caster, string previewName,
        float x, float y, float configuredWidth, float configuredHeight, bool previewInterruptible)
    {
        var casting = caster is { IsCasting: true, TotalCastTime: > 0 };
        if (!casting && !config.AlwaysShowCastBars) return (new Vector2(x, y), false);

        var width = Math.Clamp(configuredWidth, 120f, 650f);
        var height = Math.Clamp(configuredHeight, 18f, 70f);
        var scale = height / 28f;
        var size = new Vector2(width, height);
        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav |
                    ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove;
        if (config.Locked) flags |= ImGuiWindowFlags.NoInputs;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin(id, flags);
        var position = ImGui.GetWindowPos();
        var moved = false;
        if (!config.Locked)
        {
            ImGui.InvisibleButton($"{id}DragSurface", size);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                position += ImGui.GetIO().MouseDelta;
                ImGui.SetWindowPos(position);
                moved = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }

        var name = previewName;
        var progress = .55f;
        var remaining = 2.4f;
        var totalCastTime = 5.3f;
        var interruptible = previewInterruptible;
        if (casting && caster != null)
        {
            var action = data.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(caster.CastActionId);
            name = action?.Name.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) name = $"Action {caster.CastActionId}";
            progress = Math.Clamp(caster.CurrentCastTime / caster.TotalCastTime, 0f, 1f);
            remaining = Math.Max(0f, caster.TotalCastTime - caster.CurrentCastTime);
            totalCastTime = caster.TotalCastTime;
            interruptible = caster.IsCastInterruptible;
        }

        var draw = ImGui.GetWindowDrawList();
        var barPosition = position + new Vector2(2f * scale);
        var barSize = size - new Vector2(4f * scale);
        var castHigh = previewInterruptible
            ? (interruptible ? config.TargetInterruptibleCastColor : config.TargetUninterruptibleCastColor)
            : config.PlayerCastColor;
        var castLow = ScaleRgb(castHigh, .55f);
        var castBackground = ScaleRgb(castHigh, .16f, 220);
        DrawBar(draw, barPosition, barSize, progress, castBackground, castLow, castHigh, scale, false);
        if (previewInterruptible && interruptible)
            draw.AddRectFilled(barPosition, barPosition + new Vector2(3f * scale, barSize.Y), ScaleRgb(castHigh, 1.3f));

        if (!previewInterruptible && config.ShowSlideCast && config.SlideCastTimeMs > 0 && totalCastTime > 0)
        {
            var slideWidth = Math.Min(barSize.X, config.SlideCastTimeMs / 1000f * barSize.X / totalCastTime);
            var inset = Math.Max(1f, 2f * scale);
            var slideMin = new Vector2(barPosition.X + barSize.X - slideWidth + inset, barPosition.Y + inset);
            var slideMax = barPosition + barSize - new Vector2(inset);
            var insideSlideWindow = remaining <= config.SlideCastTimeMs / 1000f;
            var slideOutline = insideSlideWindow ? Rgba(105, 220, 112) : config.SlideCastColor;
            if (slideMax.X > slideMin.X && slideMax.Y > slideMin.Y)
                draw.AddRect(slideMin, slideMax, slideOutline, 1f * scale, ImDrawFlags.None,
                    Math.Max(1f, 1.5f * scale));
        }
        var textScale = .86f * scale;
        var textY = position.Y + (size.Y - ImGui.GetFontSize() * textScale) * .5f;
        DrawOutlinedText(draw, new Vector2(position.X + 7f * scale, textY), Truncate(name, 34),
            Rgba(244, 240, 229), textScale, false);
        var timeText = config.ShowTotalCastTime ? $"{totalCastTime - remaining:0.0} / {totalCastTime:0.0}s" : $"{remaining:0.0}s";
        DrawOutlinedText(draw, new Vector2(position.X + size.X - 7f * scale, textY), timeText,
            Rgba(244, 240, 229), textScale, true);
        ImGui.End();
        ImGui.PopStyleVar();
        return (position, moved);
    }

    private void InitializeCastPositions()
    {
        if (config.CastPositionsInitialized) return;
        var offset = Math.Clamp(config.FrameHeight, 50f, 150f) + 12f;
        config.PlayerCastX = config.PlayerX;
        config.PlayerCastY = config.PlayerY + offset;
        config.TargetCastX = config.TargetX;
        config.TargetCastY = config.TargetY + offset;
        config.CastPositionsInitialized = true;
        Save();
    }

    private ICharacter? GetTargetOf(ICharacter unit)
    {
        if (unit.TargetObject is ICharacter resolved) return resolved;
        if (unit.TargetObjectId != 0 && objects.SearchById(unit.TargetObjectId) is ICharacter byId) return byId;

        // The local player's target is maintained by the target system and is not
        // always mirrored into the player object's TargetObject/TargetObjectId.
        if (objects.LocalPlayer is { } localPlayer && unit.Address == localPlayer.Address)
            return targets.Target as ICharacter;

        return null;
    }

    private bool IsHostileDisposition(ICharacter unit)
    {
        if (objects.LocalPlayer is { } localPlayer && unit.Address == localPlayer.Address) return false;
        if (unit is IBattleNpc battleNpc && battleNpc.BattleNpcKind is
            BattleNpcSubKind.NpcPartyMember or BattleNpcSubKind.Pet or BattleNpcSubKind.Buddy)
            return false;
        unsafe
        {
            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)unit.Address;
            return character != null && character->IsHostile;
        }
    }

    private static bool IsInCombat(ICharacter unit)
    {
        unsafe
        {
            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)unit.Address;
            return character != null && character->InCombat;
        }
    }

    private (uint Low, uint High, uint Label) GetHealthPalette(bool player, bool hostile, bool engaged)
    {
        if (player)
        {
            var color = config.PlayerHealthColor;
            return color == Rgba(125, 174, 102)
                ? (Rgba(68, 111, 64), color, color)
                : (ScaleRgb(color, .55f), color, color);
        }

        if (!hostile)
        {
            var color = config.FriendlyHealthColor;
            return color == Rgba(76, 169, 211)
                ? (Rgba(34, 101, 139), color, color)
                : (ScaleRgb(color, .55f), color, color);
        }

        if (!engaged)
        {
            var color = config.HostileIdleHealthColor;
            return color == Rgba(242, 199, 94)
                ? (Rgba(139, 103, 43), Rgba(232, 194, 102), color)
                : (ScaleRgb(color, .55f), color, color);
        }

        var engagedColor = config.HostileEngagedHealthColor;
        return engagedColor == Rgba(255, 96, 108)
            ? (Rgba(188, 36, 44), Rgba(249, 176, 178), engagedColor)
            : (ScaleRgb(engagedColor, .55f), engagedColor, engagedColor);
    }

    private static void DrawBar(ImDrawListPtr draw, Vector2 p, Vector2 size, float value, uint bg, uint low, uint high,
        float s, bool segments = true)
    {
        draw.AddRectFilled(p + new Vector2(2, 3) * s, p + size + new Vector2(2, 3) * s, Rgba(0, 0, 0, 153), 2f * s);
        draw.AddRectFilled(p, p + size, bg, 1.5f * s);
        if (value > 0) draw.AddRectFilledMultiColor(p, p + new Vector2(size.X * value, size.Y), low, high, high, low);
        draw.AddRect(p, p + size, Rgba(8, 8, 7, 224), 1.5f * s, ImDrawFlags.None, 1.1f * s);
        draw.AddLine(p + new Vector2(1, 1) * s, new Vector2(p.X + size.X - s, p.Y + s), Rgba(255, 255, 255, 70), s);
        if (segments) for (var i = 1; i < 10; i++)
        {
            var x = p.X + size.X * i / 10f;
            draw.AddLine(new Vector2(x, p.Y + size.Y - 4f * s), new Vector2(x, p.Y + size.Y - s), Rgba(32, 29, 24, 102), s);
        }
    }

    private static uint Rgba(byte red, byte green, byte blue, byte alpha = 255) =>
        red | ((uint)green << 8) | ((uint)blue << 16) | ((uint)alpha << 24);

    private static uint ScaleRgb(uint color, float factor, byte? alpha = null)
    {
        var red = (byte)Math.Clamp((int)MathF.Round((color & 0xFF) * factor), 0, 255);
        var green = (byte)Math.Clamp((int)MathF.Round(((color >> 8) & 0xFF) * factor), 0, 255);
        var blue = (byte)Math.Clamp((int)MathF.Round(((color >> 16) & 0xFF) * factor), 0, 255);
        return Rgba(red, green, blue, alpha ?? (byte)(color >> 24));
    }

    private static Vector4 ToVector4(uint color) => new(
        (color & 0xFF) / 255f,
        ((color >> 8) & 0xFF) / 255f,
        ((color >> 16) & 0xFF) / 255f,
        ((color >> 24) & 0xFF) / 255f);

    private static uint ToRgba(Vector4 color) => Rgba(
        (byte)Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.W * 255f), 0, 255));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");

    private static string FormatCompact(uint value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000d:0.#}B",
        >= 1_000_000 => $"{value / 1_000_000d:0.#}M",
        >= 1_000 => $"{value / 1_000d:0.#}K",
        _ => value.ToString("N0")
    };

    private static void DrawCenteredText(ImDrawListPtr draw, Vector2 p, Vector2 size, string value, uint color, float scale)
    {
        var textSize = ImGui.CalcTextSize(value) * scale;
        DrawShadowText(draw, p + (size - textSize) / 2f, value, color, scale, false);
    }

    private static void DrawShadowText(ImDrawListPtr draw, Vector2 p, string value, uint color, float scale, bool right)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * scale;
        var width = ImGui.CalcTextSize(value).X * scale;
        if (right) p.X -= width;
        draw.AddText(font, fontSize, p + Vector2.One * scale, 0xE0000000, value);
        draw.AddText(font, fontSize, p, color, value);
    }

    private static void DrawOutlinedText(ImDrawListPtr draw, Vector2 p, string value, uint color, float scale, bool right)
    {
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * scale;
        var width = ImGui.CalcTextSize(value).X * scale;
        if (right) p.X -= width;
        var offset = Math.Max(1f, 1.35f * scale);
        var outline = Rgba(0, 0, 0, 235);
        draw.AddText(font, fontSize, p + new Vector2(-offset, 0), outline, value);
        draw.AddText(font, fontSize, p + new Vector2(offset, 0), outline, value);
        draw.AddText(font, fontSize, p + new Vector2(0, -offset), outline, value);
        draw.AddText(font, fontSize, p + new Vector2(0, offset), outline, value);
        draw.AddText(font, fontSize, p, color, value);
    }

    private static void DrawNativeText(ImDrawListPtr draw, Vector2 p, string value, uint color, float scale, bool right)
    {
        var font = ImGui.GetFont();
        var fontSize = MathF.Round(ImGui.GetFontSize() * scale);
        if (right) p.X -= ImGui.CalcTextSize(value).X * (fontSize / ImGui.GetFontSize());
        p = new Vector2(MathF.Round(p.X), MathF.Round(p.Y));
        var outline = ScaleRgb(color, .2f, 255);
        draw.AddText(font, fontSize, p + new Vector2(0, -1), outline, value);
        draw.AddText(font, fontSize, p + new Vector2(-1, 0), outline, value);
        draw.AddText(font, fontSize, p + new Vector2(1, 0), outline, value);
        draw.AddText(font, fontSize, p + new Vector2(0, 1), outline, value);
        draw.AddText(font, fontSize, p, color, value);
    }

    private void DrawConfig()
    {
        if (!configOpen) return;
        ImGui.SetNextWindowSize(new Vector2(390, 0), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("RudeUI Settings", ref configOpen, ImGuiWindowFlags.AlwaysAutoResize)) { ImGui.End(); return; }
        var changed = false;
        var enabled = config.Enabled;
        var locked = config.Locked;
        var hideNative = config.HideNativeFrames;
        var hideNativeCastBar = config.HideNativeCastBar;
        var combatOnly = config.HideOutOfCombat;
        var showMp = config.ShowMp;
        var showPlayerCast = config.ShowPlayerCast;
        var showCast = config.ShowTargetCast;
        var alwaysShowCastBars = config.AlwaysShowCastBars;
        var showTot = config.ShowTargetOfTarget;
        var frameWidth = config.FrameWidth;
        var frameHeight = config.FrameHeight;
        var playerCastWidth = config.PlayerCastWidth;
        var playerCastHeight = config.PlayerCastHeight;
        var targetCastWidth = config.TargetCastWidth;
        var targetCastHeight = config.TargetCastHeight;
        var playerCastColor = ToVector4(config.PlayerCastColor);
        var interruptibleColor = ToVector4(config.TargetInterruptibleCastColor);
        var uninterruptibleColor = ToVector4(config.TargetUninterruptibleCastColor);
        var showSlideCast = config.ShowSlideCast;
        var slideCastTime = config.SlideCastTimeMs;
        var slideCastColor = ToVector4(config.SlideCastColor);
        var showTotalCastTime = config.ShowTotalCastTime;
        var playerX = config.PlayerX;
        var playerY = config.PlayerY;
        var targetX = config.TargetX;
        var targetY = config.TargetY;
        var playerCastX = config.PlayerCastX;
        var playerCastY = config.PlayerCastY;
        var targetCastX = config.TargetCastX;
        var targetCastY = config.TargetCastY;
        var playerHealthColor = ToVector4(config.PlayerHealthColor);
        var friendlyHealthColor = ToVector4(config.FriendlyHealthColor);
        var hostileIdleHealthColor = ToVector4(config.HostileIdleHealthColor);
        var hostileEngagedHealthColor = ToVector4(config.HostileEngagedHealthColor);
        changed |= ImGui.Checkbox("Enable frames", ref enabled);
        changed |= ImGui.Checkbox("Lock positions", ref locked);
        changed |= ImGui.Checkbox("Hide native player and target frames", ref hideNative);
        changed |= ImGui.Checkbox("Hide native cast bar", ref hideNativeCastBar);
        changed |= ImGui.Checkbox("Only show in combat", ref combatOnly);
        ImGui.Separator();
        changed |= ImGui.Checkbox("Show player MP", ref showMp);
        changed |= ImGui.Checkbox("Show player cast bar", ref showPlayerCast);
        changed |= ImGui.Checkbox("Show target cast bar", ref showCast);
        changed |= ImGui.Checkbox("Always show cast bars for positioning", ref alwaysShowCastBars);
        ImGui.TextDisabled("Cyan casts are interruptible; gold casts are not.");
        changed |= ImGui.Checkbox("Show target of target", ref showTot);
        ImGui.Separator();
        ImGui.TextUnformatted("Unit frames");
        DrawSizeInputs("unitFrames", ref frameWidth, ref frameHeight, ref changed);
        changed |= ImGui.ColorEdit4("Player health", ref playerHealthColor);
        changed |= ImGui.ColorEdit4("Friendly health", ref friendlyHealthColor);
        changed |= ImGui.ColorEdit4("Hostile (idle)", ref hostileIdleHealthColor);
        changed |= ImGui.ColorEdit4("Hostile (engaged)", ref hostileEngagedHealthColor);
        ImGui.Separator();
        ImGui.TextUnformatted("Player cast bar");
        DrawSizeInputs("playerCast", ref playerCastWidth, ref playerCastHeight, ref changed);
        changed |= ImGui.ColorEdit4("Cast color###playerCastColor", ref playerCastColor);
        changed |= ImGui.Checkbox("Slide-cast indicator", ref showSlideCast);
        if (!showSlideCast) ImGui.BeginDisabled();
        changed |= ImGui.SliderInt("Slide-cast window", ref slideCastTime, 0, 1000, "%d ms");
        changed |= ImGui.ColorEdit4("Slide-cast color", ref slideCastColor);
        if (!showSlideCast) ImGui.EndDisabled();
        ImGui.TextUnformatted("Target cast bar");
        DrawSizeInputs("targetCast", ref targetCastWidth, ref targetCastHeight, ref changed);
        changed |= ImGui.ColorEdit4("Interruptible color", ref interruptibleColor);
        changed |= ImGui.ColorEdit4("Non-interruptible color", ref uninterruptibleColor);
        changed |= ImGui.Checkbox("Show elapsed and total cast time", ref showTotalCastTime);
        ImGui.Separator();
        ImGui.TextUnformatted("Positions");
        DrawPositionInputs("Player frame", ref playerX, ref playerY, frameWidth, ref changed);
        DrawPositionInputs("Target frame", ref targetX, ref targetY, frameWidth, ref changed);
        DrawPositionInputs("Player cast bar", ref playerCastX, ref playerCastY, playerCastWidth, ref changed);
        DrawPositionInputs("Target cast bar", ref targetCastX, ref targetCastY, targetCastWidth, ref changed);
        if (ImGui.Button("Reset positions"))
        {
            config.PositionsInitialized = false;
            config.CastPositionsInitialized = false;
            changed = true;
        }
        ImGui.SameLine(); ImGui.TextDisabled("/rudeui lock toggles movement");
        if (changed)
        {
            config.Enabled = enabled;
            config.Locked = locked;
            config.HideNativeFrames = hideNative;
            config.HideNativeCastBar = hideNativeCastBar;
            config.HideOutOfCombat = combatOnly;
            config.ShowMp = showMp;
            config.ShowPlayerCast = showPlayerCast;
            config.ShowTargetCast = showCast;
            config.AlwaysShowCastBars = alwaysShowCastBars;
            config.ShowTargetOfTarget = showTot;
            config.FrameWidth = Math.Clamp(frameWidth, 200f, 650f);
            config.FrameHeight = Math.Clamp(frameHeight, 50f, 150f);
            config.PlayerHealthColor = ToRgba(playerHealthColor);
            config.FriendlyHealthColor = ToRgba(friendlyHealthColor);
            config.HostileIdleHealthColor = ToRgba(hostileIdleHealthColor);
            config.HostileEngagedHealthColor = ToRgba(hostileEngagedHealthColor);
            config.PlayerCastWidth = Math.Clamp(playerCastWidth, 120f, 650f);
            config.PlayerCastHeight = Math.Clamp(playerCastHeight, 18f, 70f);
            config.TargetCastWidth = Math.Clamp(targetCastWidth, 120f, 650f);
            config.TargetCastHeight = Math.Clamp(targetCastHeight, 18f, 70f);
            config.PlayerCastColor = ToRgba(playerCastColor);
            config.TargetInterruptibleCastColor = ToRgba(interruptibleColor);
            config.TargetUninterruptibleCastColor = ToRgba(uninterruptibleColor);
            config.ShowSlideCast = showSlideCast;
            config.SlideCastTimeMs = slideCastTime;
            config.SlideCastColor = ToRgba(slideCastColor);
            config.ShowTotalCastTime = showTotalCastTime;
            config.PlayerX = playerX;
            config.PlayerY = playerY;
            config.TargetX = targetX;
            config.TargetY = targetY;
            config.PlayerCastX = playerCastX;
            config.PlayerCastY = playerCastY;
            config.TargetCastX = targetCastX;
            config.TargetCastY = targetCastY;
            Save();
        }
        ImGui.End();
    }

    private static void DrawPositionInputs(string label, ref float x, ref float y, float elementWidth, ref bool changed)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(115f);
        changed |= ImGui.InputFloat($"X###{label}X", ref x, 1f, 10f, "%.0f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(115f);
        changed |= ImGui.InputFloat($"Y###{label}Y", ref y, 1f, 10f, "%.0f");
        ImGui.SameLine();
        if (ImGui.Button($"Center X###{label}Center"))
        {
            x = MathF.Round((ImGui.GetIO().DisplaySize.X - elementWidth) * .5f);
            changed = true;
        }
    }

    private static void DrawSizeInputs(string id, ref float width, ref float height, ref bool changed)
    {
        ImGui.SetNextItemWidth(115f);
        changed |= ImGui.InputFloat($"Width###{id}Width", ref width, 1f, 10f, "%.0f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(115f);
        changed |= ImGui.InputFloat($"Height###{id}Height", ref height, 1f, 10f, "%.0f");
    }

    private void SetNativeFramesVisible(bool visible)
    {
        // Keep hidden widgets hidden because the game may reopen them as targets change,
        // but only restore once so we do not fight normal game visibility afterward.
        if (visible && !nativeFramesHidden) return;
        foreach (var name in NativeAddons)
        {
            var addon = gameGui.GetAddonByName(name);
            if (addon.Address == 0) continue;
            unsafe
            {
                var unit = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon.Address;
                if (visible) unit->Show(false, 0); else unit->Hide(false, false, 0);
            }
        }
        nativeFramesHidden = !visible;
    }

    private void SetNativeCastBarVisible(bool visible)
    {
        if (visible && !nativeCastBarHidden) return;
        var addon = gameGui.GetAddonByName("_CastBar");
        if (addon.Address != 0)
        {
            unsafe
            {
                var unit = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon.Address;
                if (visible) unit->Show(false, 0); else unit->Hide(false, false, 0);
            }
        }
        nativeCastBarHidden = !visible;
    }
}
