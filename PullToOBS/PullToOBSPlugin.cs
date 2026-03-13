using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PullToOBS.Windows;

namespace PullToOBS;

public sealed class PullToOBSPlugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/pulltoobs";
    private const string CommandAlias = "/pto";

    public PullToOBSConfiguration Configuration { get; private set; }
    public IOBSController ObsController { get; private set; }
    public EncounterManager EncounterManager { get; private set; }

    public WindowSystem WindowSystem { get; } = new("PullToOBS");
    public PullToOBSConfigWindow ConfigWindow { get; private set; }
    private OBSStatusIndicator _indicator;

    // Scaled font handle for the indicator
    internal IFontHandle IndicatorFont { get; private set; } = null!;
    private bool _ownsIndicatorFont;
    private float _lastFontMultiplier = -1f;

    public PullToOBSPlugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as PullToOBSConfiguration ?? new PullToOBSConfiguration();
        Configuration.SetSaveAction(PluginInterface.SavePluginConfig);

        ObsController = new OBSController(Log);
        EncounterManager = new EncounterManager(ObsController, Condition, Log);

        ObsController.ErrorOccurred += OnOBSError;
        EncounterManager.ErrorOccurred += OnEncounterError;

        ConfigWindow = new PullToOBSConfigWindow(this, ChatGui);
        WindowSystem.AddWindow(ConfigWindow);

        _indicator = new OBSStatusIndicator(this, ClientState, Condition);

        EnsureIndicatorFont();

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open PullToOBS configuration window"
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open PullToOBS configuration window (alias)"
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        Framework.Update += OnFrameworkUpdate;

        if (Configuration.AutoConnectOnStart)
            _ = ObsController.ConnectAsync(Configuration.ObsWebSocketUrl, Configuration.ObsPassword);

        Log.Information("===PullToOBS plugin loaded successfully===");
    }

    private void OnCommand(string command, string args)
    {
        var trimmedArgs = args.Trim().ToLower();

        if (trimmedArgs == "show")
        {
            Configuration.HideIndicator = false;
            Configuration.Save();
        }
        else if (trimmedArgs == "hide")
        {
            Configuration.HideIndicator = true;
            Configuration.Save();
        }
        else
        {
            ToggleConfigUi();
        }
    }

    private void ToggleConfigUi()
    {
        ConfigWindow.Toggle();
    }

    /// <summary>
    /// Called every frame on the game/framework thread.
    /// Polls combat state via Dalamud condition flags.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        EncounterManager.Update();
    }

    private void DrawUi()
    {
        EnsureIndicatorFont();
        WindowSystem.Draw();
        _indicator.Draw();
    }

    internal void EnsureIndicatorFont()
    {
        var multiplier = 1.8f * Configuration.IndicatorScale;

        if (IndicatorFont != null && MathF.Abs(multiplier - _lastFontMultiplier) < 0.01f)
            return;

        if (_ownsIndicatorFont && IndicatorFont != null)
        {
            IndicatorFont.Dispose();
            _ownsIndicatorFont = false;
        }

        _lastFontMultiplier = multiplier;

        if (PluginInterface.UiBuilder.DefaultFontSpec is SingleFontSpec spec)
        {
            var scaledSpec = spec with { SizePx = spec.SizePx * multiplier };
            IndicatorFont = scaledSpec.CreateFontHandle(PluginInterface.UiBuilder.FontAtlas);
            _ownsIndicatorFont = true;
        }
        else
        {
            IndicatorFont = PluginInterface.UiBuilder.DefaultFontHandle;
            _ownsIndicatorFont = false;
        }
    }

    private void OnOBSError(string message)
    {
        Log.Error($"OBS Error: {message}");
        ChatGui.Print($"[PullToOBS] Error: {message}");
    }

    private void OnEncounterError(string message)
    {
        Log.Error($"Encounter Error: {message}");
        ChatGui.Print($"[PullToOBS] Error: {message}");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        _indicator.Dispose();

        ObsController.ErrorOccurred -= OnOBSError;
        EncounterManager.ErrorOccurred -= OnEncounterError;

        EncounterManager.Dispose();
        ObsController.Dispose();

        if (_ownsIndicatorFont)
            IndicatorFont.Dispose();
    }
}
