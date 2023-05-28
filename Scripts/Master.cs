using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Godot.Collections;
using Mono.Unix;
using ProgressBar = Godot.ProgressBar;
using WindowsShortcutFactory;

public partial class Master : Control
{
	[ExportGroup("App")]
	[Export()] private float _appVersion = 2.2f;
	[Export()] private float _saveManagerVersion = 1.9f;
	[Export()] private TextureRect _darkBg;
	[Export()] private TextureRect _lightBg;
	[Export()] private ColorRect _downloadWindowApp;
	[Export()] private AudioStreamPlayer _backgroundAudio;
	[Export()] private Godot.CheckButton _muteButton;
	[Export()] private CheckBox _enableLightTheme;
	[Export()] private Array<Theme> _themes;
	[Export()] private Array<StyleBoxLine> _themesSeparator;
	[Export()] private ColorRect _header;
	[Export()] private Godot.Label _headerLabel;
	[Export()] private Godot.Label _latestVersionLabel;
	[Export()] private ToolsPage _toolsPage;
	[Export()] private ModManager _modManager;
	[Export()] private Installer _installer;


	// Internal variables
	public ResourceSaveManager _saveManager;
	public SettingsResource _settings;
	private Theme _currentTheme;


	// Godot functions
	public void Initiate()
	{
		// Sets minimum window size to prevent text clipping and UI breaking at smaller scales.
		DisplayServer.WindowSetMinSize(new Vector2I(1024, 576));

		_saveManager = Globals.Instance.SaveManager;
		_settings = Globals.Instance.Settings;
		// Setup save manager and load settings
		// _saveManager = new ResourceSaveManager();
		// _saveManager.Version = _saveManagerVersion;
		// _settings = _saveManager.GetSettings();
		// _saveManager.WriteSave(_settings);
		
		// _toolsPage.Initiate();
		// _modManager.Initiate();
		// _installer.Initiate();

		// Mute by default the music
		ToggledMusicButton(false);
		SetTheme(_settings.LightModeEnabled);
		
		// Signals
		Resized += WindowResized;
		_enableLightTheme.Toggled += SetTheme;
	}


	// Custom functions
	private void SetTheme(bool enableLight)
	{
		_lightBg.Visible = enableLight;
		_darkBg.Visible = !enableLight;
		_currentTheme = enableLight ? _themes[1] : _themes[0];
		_header.Color = enableLight ? new Godot.Color(0.74117648601532f, 0.76470589637756f, 0.78039216995239f) : new Godot.Color(0.16862745583057f, 0.1803921610117f, 0.18823529779911f);
		_downloadWindowApp.Color = enableLight ? new Godot.Color(0.74117648601532f, 0.76470589637756f, 0.78039216995239f) : new Godot.Color(0.16862745583057f, 0.1803921610117f, 0.18823529779911f);
		_enableLightTheme.ButtonPressed = enableLight;
		_settings.LightModeEnabled = enableLight;
		_saveManager._settings = _settings;
		_saveManager.WriteSave();
		Theme = _currentTheme;
	}


	private void WindowResized()
	{
		float scaleRatio = (((float)GetWindow().Size.X / 1920) + ((float)GetWindow().Size.Y / 1080)) / 2;
		_headerLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * 76));
		_latestVersionLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * 32));
		_currentTheme.DefaultFontSize = Mathf.Clamp((int)(scaleRatio * 35), 20, 50);
	}


	// Signal functions
	private void ToggledMusicButton(bool musicEnabled)
	{
		AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), !musicEnabled);
	}
	
	
}