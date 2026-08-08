using Dalamud.Configuration;

namespace RudeUI;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; }
    public bool HideNativeFrames { get; set; } = true;
    public bool HideNativeCastBar { get; set; } = true;
    public bool HideOutOfCombat { get; set; }
    public bool ShowMp { get; set; } = true;
    public bool ShowPlayerCast { get; set; } = true;
    public bool ShowTargetCast { get; set; } = true;
    public bool AlwaysShowCastBars { get; set; }
    public bool ShowTargetOfTarget { get; set; } = true;
    public float FrameWidth { get; set; } = 330f;
    public float FrameHeight { get; set; } = 76f;
    public float PlayerX { get; set; } = 480f;
    public float PlayerY { get; set; } = 760f;
    public float TargetX { get; set; } = 1120f;
    public float TargetY { get; set; } = 760f;
    public bool PositionsInitialized { get; set; }
    public float PlayerCastX { get; set; }
    public float PlayerCastY { get; set; }
    public float TargetCastX { get; set; }
    public float TargetCastY { get; set; }
    public float PlayerCastWidth { get; set; } = 330f;
    public float PlayerCastHeight { get; set; } = 28f;
    public float TargetCastWidth { get; set; } = 330f;
    public float TargetCastHeight { get; set; } = 28f;
    public uint PlayerCastColor { get; set; } = 0xFF54B7DE;
    public uint TargetInterruptibleCastColor { get; set; } = 0xFFD7BE40;
    public uint TargetUninterruptibleCastColor { get; set; } = 0xFF54B7DE;
    public bool ShowSlideCast { get; set; } = true;
    public int SlideCastTimeMs { get; set; } = 500;
    public uint SlideCastColor { get; set; } = 0xFF391CBE;
    public bool ShowTotalCastTime { get; set; }
    public uint PlayerHealthColor { get; set; } = 0xFF66AE7D;
    public uint FriendlyHealthColor { get; set; } = 0xFFD3A94C;
    public uint HostileIdleHealthColor { get; set; } = 0xFF5EC7F2;
    public uint HostileEngagedHealthColor { get; set; } = 0xFF6C60FF;
    public bool CastPositionsInitialized { get; set; }
}
