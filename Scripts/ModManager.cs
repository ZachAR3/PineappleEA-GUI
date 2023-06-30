	using Godot;
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Text.Json;
	using System.Threading.Tasks;
	using NativeFileDialogSharp;
	using YuzuEAUpdateManager.Scripts.Sources;
	using Button = Godot.Button;
	using ProgressBar = Godot.ProgressBar;

	public partial class ModManager : Control
	{
		[ExportGroup("ModManager")] 
		[Export()] private string _installedModsPath;
		[Export()] private int _selectionPaddingLeft = 4;
		[Export()] private ItemList _modList;
		[Export()] private ProgressBar _downloadBar;
		[Export()] private Timer _downloadUpdateTimer;
		[Export()] private HttpRequest _downloadRequester;
		[Export()] private HttpRequest _titleRequester;
		[Export()] private Texture2D _installedIcon;
		[Export()] private Panel _loadingPanel;
		[Export()] private OptionButton _gamePickerButton;
		[Export()] private OptionButton _sourcePickerButton;
		[Export()] private Button _modLocationButton;
		[Export()] private Button _refreshButton;
		[Export()] private Button _updateAllButton;
		[Export()] private Button _updateSelectedButton;
		[Export()] private Button _loadMoreButton;


		private string _currentGameId;
		private string _osUsed = OS.GetName();
		private const string TitleSplitter = "||";


		// Code for handling sources and their associated names with each
		enum Sources
		{
			Official,
			Banana,
			TotkHolo,
			All
		}

		private List<string> _sourceNames;
		private Dictionary<string, List<Mod>> _selectedSourceMods = new();
		private int _selectedSource = (int)Sources.Official;
		private Dictionary<int, string> _gameSpecificSources = new();

		// Game id, game name
		private Dictionary<string, string> _titles = new();

		// Game id, mod names List
		private Dictionary<string, Game> _installedGames = new();
		private Dictionary<string, List<Mod>> _installedMods = new();

		private StandardModManagement _standardModManager;
		private BananaManager _bananaManager = new();
		private OfficialManager _officialManager = new();
		private TotkHoloManager _totkHoloManager = new();

		private int _modsPage = 1;


		// Godot Functions
		private async void Initiate()
		{
			// Converts the given local path to an absolute one upon run time
			_installedModsPath = ProjectSettings.GlobalizePath(_installedModsPath);

			_loadMoreButton.Disabled = true;

			_titleRequester.Connect("request_completed", new Callable(this, nameof(GetTitles)));

			_sourceNames = Enum.GetNames(typeof(Sources)).ToList();
			
			// Sets zelda totk specific source with game id and source value
			_gameSpecificSources[(int)Sources.TotkHolo] = "0100F2C0115B6000";

			_modLocationButton.Text =
				Globals.Instance.Settings.ModsLocation.PadLeft(
					Globals.Instance.Settings.ModsLocation.Length + _selectionPaddingLeft, ' ');

			AddSources();
			await GetGamesAndMods();
		}


		public override void _Notification(int notification)
		{
			if (notification == NotificationWMCloseRequest)
				SaveInstalledMods();
		}


		public void ResetInstalled()
		{
			_installedMods = new Dictionary<string, List<Mod>>();
			SaveInstalledMods();
		}


		// Custom functions
		private async Task GetGamesAndMods(int source = (int)Sources.Official, int selectedGame = 0)
		{
			await Task.Run(async () =>
			{
				_loadingPanel.Visible = true;
				if (!Directory.Exists(Globals.Instance.Settings.ModsLocation))
				{
					Tools.Instance.AddError($@"mods location does not exist. Please set a valid location and refresh.");
					_loadingPanel.Visible = false;
					return;
				}

				_titleRequester.Request(
					"https://switchbrew.org/w/index.php?title=Title_list/Games&mobileaction=toggle_view_desktop");
				await ToSignal(_titleRequester,
					"request_completed"); // Waits for titles to be retrieved before checking installed titles against them.

				// Checks if no titles were found, if they weren't gives error and cancels.
				if (_titles.Count <= 0)
				{
					Tools.Instance.AddError("failed to retrieve titles list, check connection and try again later.");
					_loadingPanel.Visible = false;
					return;
				}

				foreach (var gameModFolder in Directory.GetDirectories(Globals.Instance.Settings.ModsLocation))
				{
					string gameId = gameModFolder.GetFile(); // Gets game id by grabbing the folders name
					if (_titles.TryGetValue(gameId, out var gameName))
					{
						_installedGames[gameId] = new() { GameName = gameName };
						GetInstalledMods(gameId);
						_gamePickerButton.AddItem($@"    {gameName}");
						if (_gameSpecificSources.ContainsKey(_selectedSource) && !_gameSpecificSources.ContainsValue(gameId))
						{
							continue;
						}
						await Task.Run(async () => { await GetAvailableMods(gameId, source); });
					}
					else
					{ 
						Tools.Instance.AddError($@"could not find associated game title for: {gameId}");
					}

				}
			});
			
			// Sets the first game as selected by default
			if (_installedGames.Count > 0)
			{
				selectedGame = Math.Clamp(selectedGame, 0, _installedGames.Count - 1);
				_gamePickerButton.Selected = selectedGame;
				SelectGame(selectedGame);
			}
			else
			{
				Tools.Instance.AddError("no installed games found, please ensure your mods directory is set to the correct location.");
				_loadingPanel.Visible = false;
			}
		}


		private async Task GetAvailableMods(string gameId, int source)
		{
			if (gameId == null && !_installedGames.ContainsKey(gameId))
			{
				Tools.Instance.AddError("game ID invalid. Cancelling...");
				_loadingPanel.Visible = false;
				return;
			}

			// Grabs the mods from the specified source.
			try
			{
				switch (source)
				{
					case (int)Sources.Official:
						_selectedSourceMods = await _officialManager.GetAvailableMods(_selectedSourceMods, _installedGames,
							gameId, (int)Sources.Official);
						break;

						case (int)Sources.Banana:
							_selectedSourceMods = await _bananaManager.GetAvailableMods(_selectedSourceMods, _installedGames,
								gameId, (int)Sources.Banana, _modsPage);
							break;

						case (int)Sources.TotkHolo:
							_selectedSourceMods =
								await _totkHoloManager.GetAvailableMods(_selectedSourceMods, gameId, (int)Sources.TotkHolo,
									Globals.Instance.Settings.GetCompatibleVersions);
							break;

						case (int)Sources.All:
							// Adds both official and banana mods the our source list
							_selectedSourceMods = await _officialManager.GetAvailableMods(_selectedSourceMods, _installedGames,
								gameId, (int)Sources.Official);
							_selectedSourceMods = await _bananaManager.GetAvailableMods(_selectedSourceMods, _installedGames,
								gameId, (int)Sources.Banana, _modsPage);
							if (_currentGameId == _gameSpecificSources[(int)Sources.TotkHolo])
							{
								_selectedSourceMods = await _totkHoloManager.GetAvailableMods(_selectedSourceMods,
									gameId, (int)Sources.TotkHolo, Globals.Instance.Settings.GetCompatibleVersions);
							}
							break;
					}
			}
			catch (ArgumentException argumentException)
			{
				Tools.Instance.AddError(
					$@"Failed to retrieve mod list for ID:{gameId} | Title:{_titles[gameId]}. Exception:{argumentException.Message}");
				_loadingPanel.Visible = false;
				return;
			}

			if (_selectedSourceMods.ContainsKey(gameId))
			{
				var installedModNames = new HashSet<string>(_installedMods[gameId].Select(mod => mod.ModName.ToLower().Trim()));
				_selectedSourceMods[gameId].RemoveAll(mod => installedModNames.Contains(mod.ModName.ToLower().Trim()));
			}
		}


		private void GetInstalledMods(string gameId)
		{
			try
			{
				// Initializes the list of mods for the given game,
				_installedMods[gameId] = new List<Mod>();

				if (File.Exists(_installedModsPath))
				{
					var installedModsJson =
						JsonSerializer.Deserialize<Dictionary<string, List<Mod>>>(File.ReadAllText(_installedModsPath));
					if (installedModsJson.TryGetValue(gameId, out var gameMods))
					{
						_installedMods[gameId] = gameMods;
					}
				}

				// Adds local mods that aren't in the data base
				foreach (var modDirectory in
				         Directory.GetDirectories($@"{Globals.Instance.Settings.ModsLocation}/{gameId}"))
				{
					if (!modDirectory.GetFile().StartsWith("Managed"))
					{
						Mod modToAdd = new()
						{
							ModName = modDirectory.GetFile(),
							ModUrl = null,
							CompatibleVersions = new List<string> { "NA" },
							Source = -1,
							InstalledPath = modDirectory
						};

						if (_installedMods[gameId].Any(mod => mod.InstalledPath == modToAdd.InstalledPath))
						{
							return;
						}

						_installedMods[gameId].Add(modToAdd);
					}
				}
			}
			catch (Exception installedError)
			{
				Tools.Instance.AddError($@"cannot find installed mods error: {installedError}");
				_loadingPanel.Visible = false;
				throw;
			}

			SaveInstalledMods();
		}


		private async void LoadNextPage()
		{
			_modsPage++;
			_loadingPanel.Visible = true;
			DisableInteraction();

			var tempModsList = new Dictionary<string, List<Mod>>();
			await Task.Run(async () =>
			{
				switch (_selectedSource)
				{
					case (int)Sources.Banana:
						tempModsList = await _bananaManager.GetAvailableMods(_selectedSourceMods, _installedGames,
							_currentGameId,
							_selectedSource, _modsPage);
						break;
					case (int)Sources.All:
						tempModsList = await _bananaManager.GetAvailableMods(_selectedSourceMods, _installedGames,
							_currentGameId,
							_selectedSource, _modsPage);
						break;
				}
			});

			// If our old list is the same as the new one, disabled the load more as no more mods are available.
			_loadMoreButton.Disabled = tempModsList == _selectedSourceMods || _loadMoreButton.Disabled;
			_selectedSourceMods = tempModsList;

			DisableInteraction(false);
			_loadingPanel.Visible = false;

			SelectGame(_gamePickerButton.Selected);
		}


		// Adds available and local mods to mod list
		private void AddMods(string gameId)
		{
			if (!_selectedSourceMods.ContainsKey(gameId) && !_installedMods.ContainsKey(gameId))
			{
				_loadingPanel.Visible = false;
				return;
			}

			// Adds the available and installed mods
			if (_installedMods.TryGetValue(gameId, out var installedMods))
			{
				foreach (var mod in installedMods)
				{
					var modIndex = _modList.AddItem($@"  {mod.ModName} || Supports:{string.Join(", ", mod.CompatibleVersions)}  ",
						icon: _installedIcon);
					_modList.SetItemMetadata(modIndex, mod.ModName);
				}
			}

			if (_selectedSourceMods.TryGetValue(gameId, out var selectedSourceMods))
			{
				foreach (var mod in selectedSourceMods)
				{
					var modIndex = _modList.AddItem($@"  {mod.ModName} || Supports:{string.Join(", ", mod.CompatibleVersions)}  ");
					_modList.SetItemMetadata(modIndex, mod.ModName);
				}
			}

			_loadingPanel.Visible = false;
		}


		private void GetTitles(long result, long responseCode, string[] headers, byte[] body)
		{
			string[]
				gamesList = Encoding.UTF8.GetString(body).Split("<tr>"); // Splits the list into the beginnings of each game
			var gameList =
				gamesList.ToList(); // Converted to list so first and second item (headers and example text at top) can be removed

			if (gameList.Count < 2)
			{
				Tools.Instance.AddError("cannot retrieve titles");
				return;
			}

			gameList.RemoveRange(0, 2);

			foreach (string game in gameList)
			{
				// Removes the <td> and </td> html from our script for cleaning along with the special TM character otherwise the mod sites won't recognize the title.
				var gameCleaned = game.Replace("<td>", "").Replace("</td>", "").Replace("™", "");
				// Splits at every new line
				var gameSplit = gameCleaned.Split("\n");

				if (gameSplit.Length < 2)
				{
					Tools.Instance.AddError("unable to parse titles list, check connection and try again later.");
					_loadingPanel.Visible = false;
					return;
				}

				// Adds the game to our title list with type (string ID, string Title)
				_titles[gameSplit[1]] = gameSplit[2];
			}
			
			// TODO remove this once games are added to list (#59)
			_titles["01001F5010DFA000"] = "Pokémon Legends: Arceus";
		}


		private async void UpdateAll()
		{
			var confirm = await Tools.Instance.ConfirmationPopup("Update all mods?");
			if (confirm == false)
			{
				return;
			}

			foreach (var installedGame in _installedGames)
			{
				// Mods list is temporarily duplicated to avoid issues with indexing when removing and re-adding the mods during update.
				foreach (var mod in new List<Mod>(_installedMods[installedGame.Key]))
				{
					if (mod.ModUrl != null)
					{
						var modUpdated = await UpdateMod(installedGame.Key, mod, true);
						if (modUpdated != true)
						{
							Tools.Instance.AddError($@"failed to update:{mod.ModName}");
							_loadingPanel.Visible = false;
							return;
						}
					}
				}
			}

			SelectGame(_gamePickerButton.Selected);
		}


		private async Task<bool> UpdateMod(string gameId, Mod mod, bool noConfirmation = false)
		{
			if (!noConfirmation)
			{
				var confirm = await Tools.Instance.ConfirmationPopup($@"Update {mod.ModName}?");
				if (confirm == false)
				{
					return false;
				}
			}

			try
			{
				var removedMod = await DeleteMod(gameId, mod, _selectedSource, (int)Sources.All);
				if (removedMod != true)
				{
					return false;
				}

				await InstallMod(gameId, mod);
			}
			catch (Exception updateError)
			{
				Tools.Instance.AddError($@"failed to update mod:{updateError}");
				throw;
			}

			SelectGame(_gamePickerButton.Selected);
			return true;
		}


		private async void SelectGame(int gameIndex)
		{
			// Gets the keys we can equate as an List
			_currentGameId = GetGameIdFromValue(_gamePickerButton.GetItemText(gameIndex).Trim(), _installedGames);
			// Clears old mods from our list
			_modList.Clear();

			_sourcePickerButton.Clear();
			AddSources();

			if (_gameSpecificSources.ContainsKey(_selectedSource) && _gameSpecificSources[_selectedSource] != _currentGameId)
			{
				await Refresh();
			}
			else
			{
				AddMods(_currentGameId);
			}
		}


		static string GetGameIdFromValue(string value, Dictionary<string, Game> installedGames)
		{
			foreach (string gameId in installedGames.Keys)
			{
				if (installedGames[gameId].GameName == value)
				{
					return gameId;
				}
			}

			return null;
		}


		private async Task Refresh(int source = (int)Sources.Official)
		{
			// Clean up
			_modList.Clear();
			_selectedSourceMods.Clear();
			_installedGames.Clear();
			_titles.Clear();
			
			// Re-grabs and adds the mods
			int selectedGame = _gamePickerButton.Selected;
			_gamePickerButton.Clear();
			_selectedSource = source;
			
			await GetGamesAndMods(source, selectedGame);
		}


		private void AddSources()
		{	
			foreach (var source in Enum.GetValues(typeof(Sources)))
			{
				switch (source)
				{
					// Breaks if source is totkholo but selected game isn't
					case Sources.TotkHolo when _currentGameId != _gameSpecificSources[(int)source]:
						break;
					
					default:
						_sourcePickerButton.AddItem(_sourceNames[(int)source].PadLeft(_sourceNames[(int)source].Length + _selectionPaddingLeft));
						break;
				}
			}

			_sourcePickerButton.Select(_selectedSource);
		}


		private void DisableInteraction(bool interactionDisabled = true)
		{
			for (int itemIndex = 0; itemIndex < _modList.ItemCount; itemIndex++)
			{
				_modList.SetItemDisabled(itemIndex, interactionDisabled);
			}

			_gamePickerButton.Disabled = interactionDisabled;
			_sourcePickerButton.Disabled = interactionDisabled;
			_modLocationButton.Disabled = interactionDisabled;
			_refreshButton.Disabled = interactionDisabled;
			_updateAllButton.Disabled = interactionDisabled;
			_updateSelectedButton.Disabled = interactionDisabled;
			if (_selectedSource is (int)Sources.Banana or (int)Sources.All)
			{
				_loadMoreButton.Disabled = interactionDisabled;
			}
		}
		
		
		private async Task SelectSource(int sourceIndex)
		{
			_selectedSource = _sourceNames.IndexOf(_sourcePickerButton.GetItemText(sourceIndex).Trim());
			if (_selectedSource == -1)
			{
				Tools.Instance.AddError("source not found, please file a bug report. Defaulting back to official");
				_sourcePickerButton.Select(0);
				return;
			}

			switch (_selectedSource)
			{
				case (int)Sources.Banana:
					_loadMoreButton.Disabled = false;
					break;
				case (int)Sources.All:
					_loadMoreButton.Disabled = false;
					break;
				default:
					_loadMoreButton.Disabled = true;
					break;
			}

			await Refresh(_selectedSource);
		}
		
		
		// Helper functions
		private void SaveInstalledMods()
		{
			var serializedMods = JsonSerializer.Serialize(_installedMods);
			File.WriteAllText(_installedModsPath, serializedMods);
		}
		
		
		private async Task InstallMod(string gameId, Mod mod)
		{
			UpdateManagers();
			switch (_selectedSource)
			{
				case (int)Sources.TotkHolo:
					await _totkHoloManager.InstallMod(gameId, mod);
					break;
				default:
					await _standardModManager.InstallMod(gameId, mod);
					_downloadBar.Value = 100;
					break;
			}
		}


		private async Task<bool> DeleteMod(string gameId, Mod mod, int source, int sourcesAll, bool noConfirmation = false)
		{
			UpdateManagers();
			switch (_selectedSource)
			{
				case (int)Sources.TotkHolo:
					break;
				default:
					return await _standardModManager.DeleteMod(gameId, mod, source, sourcesAll, noConfirmation);
			}

			return false;
		}
		
		
		private void UpdateManagers()
		{
			_standardModManager = new()
			{
				InstalledMods = _installedMods,
				SelectedSourceMods = _selectedSourceMods,
				InstalledModsPath = _installedModsPath,
				DownloadRequester = _downloadRequester,
				DownloadUpdateTimer = _downloadUpdateTimer,
				LoadingPanel = _loadingPanel

			};

			_totkHoloManager = new()
			{
				InstalledMods = _installedMods,
				SelectedSourceMods = _selectedSourceMods
			};
		}


		// Signal functions
		private void SearchUpdated(string newSearch)
		{
			if (_selectedSourceMods.TryGetValue(_currentGameId, out var sourceMods))
			{
				var searchQuery = newSearch.ToLower().Trim();
				_modList.Clear();
				AddMods(_currentGameId);

				for (int modIndex = _modList.ItemCount - 1; modIndex >= 0; modIndex--)
				{
					if (!_modList.GetItemText(modIndex).ToLower().Trim().Contains(searchQuery))
					{
						_modList.RemoveItem(modIndex);
					}
				}
			}
		}


		private async void ModClicked(int modIndex)
		{
			// If the mod is found in the installed mods list removes it
			if (_installedMods.TryGetValue(_currentGameId, out var installedMods))
			{
				foreach (var mod in installedMods)
				{
					if ((string)_modList.GetItemMetadata(modIndex) == mod.ModName)
					{
						DisableInteraction();
						await DeleteMod(_currentGameId, mod, _selectedSource, (int)Sources.All);
						DisableInteraction(false);
						
						//Used to update UI with installed icon	
						SelectGame(_gamePickerButton.Selected);
						return;
					}
				}
			}

			// Installs the mod from the online source
			if (_selectedSourceMods.TryGetValue(_currentGameId, out var selectedSourceMods))
			{
				foreach (var mod in selectedSourceMods)
				{
					if ((string)_modList.GetItemMetadata(modIndex) == mod.ModName)
					{
						DisableInteraction();
						await InstallMod(_currentGameId, mod);
						DisableInteraction(false);

						//Used to update UI with installed icon	
						SelectGame(_gamePickerButton.Selected);
						return;
					}
				}
			}
		}


		private async void UpdateSelectedPressed()
		{
			foreach (Mod mod in _installedMods[_currentGameId])
			{
				var selectedMods = _modList.GetSelectedItems();
				if (selectedMods.Length <= 0)
				{
					return;
				}

				if (_modList.GetItemText(selectedMods[0]).Split(TitleSplitter)[0].Trim() == (mod.ModName))
				{
					_loadingPanel.Visible = true;
					if (mod.ModUrl != null)
					{
						await UpdateMod(_currentGameId, mod);
					}

					// Used to update UI with installed icon
					SelectGame(_gamePickerButton.Selected);
					_loadingPanel.Visible = false;
					return;
				}
			}
		}


		private void ModLocationPressed()
		{
			var modLocationInput = Dialog.FolderPicker(Globals.Instance.Settings.ModsLocation).Path;
			if (modLocationInput != null)
			{
				Globals.Instance.Settings.ModsLocation = modLocationInput;
			}

			if (Globals.Instance.Settings.ModsLocation != null)
			{
				_modLocationButton.Text =
					Globals.Instance.Settings.ModsLocation.PadLeft(
						Globals.Instance.Settings.ModsLocation.Length + _selectionPaddingLeft, ' ');
			}

			Globals.Instance.SaveManager.WriteSave(Globals.Instance.Settings);
		}


		private void RefreshPressed()
		{
			Refresh(_selectedSource);
		}

		private void UpdateDownloadProgress()
		{
			_downloadBar.Value = (float)_downloadRequester.GetDownloadedBytes() / _downloadRequester.GetBodySize() * 100;
		}


		private void SourceSelected(int selectedSource)
		{
			SelectSource(selectedSource);
		}
	}
