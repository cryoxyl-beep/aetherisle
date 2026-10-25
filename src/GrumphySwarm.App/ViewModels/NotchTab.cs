namespace GrumphySwarm.App.ViewModels;

/// <summary>Expanded-island content modes. Media is the player view (no tab chip).</summary>
public enum NotchTab
{
    Nook,
    Agents,

    /// <summary>Live system stats view (CPU/GPU/RAM/disk/network/uptime/top apps).</summary>
    Stats,

    /// <summary>The media player expanded view — opened explicitly (artwork/card click, Shift+scroll).</summary>
    Media,
}
