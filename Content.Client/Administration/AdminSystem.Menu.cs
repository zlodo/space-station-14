using System.Collections.Generic;
using Content.Client.Administration.Managers;
using Content.Client.Administration.UI;
using Content.Client.Administration.UI.Tabs.PlayerTab;
using Content.Client.HUD;
using Content.Shared.Input;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Client.Administration
{
    public partial class AdminSystem
    {
        [Dependency] private readonly INetManager _netManager = default!;
        [Dependency] private readonly IInputManager _inputManager = default!;
        [Dependency] private readonly IGameHud _gameHud = default!;
        [Dependency] private readonly IClientAdminManager _clientAdminManager = default!;
        [Dependency] private readonly IClientConGroupController _clientConGroupController = default!;
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEntityLookup _entityLookup = default!;
        [Dependency] private readonly IClientConsoleHost _clientConsoleHost = default!;

        private AdminMenuWindow? _window;
        private readonly List<SS14Window> _commandWindows = new();

        private void InitializeMenu()
        {
            // Reset the AdminMenu Window on disconnect
            _netManager.Disconnect += (_, _) => ResetWindow();

            _inputManager.SetInputCommand(ContentKeyFunctions.OpenAdminMenu,
                InputCmdHandler.FromDelegate(_ => Toggle()));

            _clientAdminManager.AdminStatusUpdated += () =>
            {
                // when status changes, show the top button if we can open admin menu.
                // if we can't or we lost admin status, close it and hide the button.
                _gameHud.AdminButtonVisible = CanOpen();
                if (!_gameHud.AdminButtonVisible)
                {
                    Close();
                }
            };
            _gameHud.AdminButtonToggled += (open) =>
            {
                if (open)
                {
                    TryOpen();
                }
                else
                {
                    Close();
                }
            };
            _gameHud.AdminButtonVisible = CanOpen();
            _gameHud.AdminButtonDown = false;
        }


        public void ResetWindow()
        {
            _window?.Close();
            _window?.Dispose();
            _window = null;

            foreach (var window in _commandWindows)
            {
                window.Close();
                window.Dispose();
            }

            _commandWindows.Clear();
        }

        public void OpenCommand(SS14Window window)
        {
            _commandWindows.Add(window);
            window.OpenCentered();
        }

        public void Open()
        {
            _window ??= new AdminMenuWindow();
            _window.PlayerTabControl.OnEntryPressed += PlayerTabEntryPressed;
            _window.OpenCentered();
        }

        public void Close()
        {
            _window?.Close();

            foreach (var window in _commandWindows)
                window?.Dispose();
            _commandWindows.Clear();
        }

        /// <summary>
        /// Checks if the player can open the window
        /// </summary>
        /// <returns>True if the player is allowed</returns>
        public bool CanOpen()
        {
            return _clientConGroupController.CanAdminMenu();
        }

        /// <summary>
        /// Checks if the player can open the window and tries to open it
        /// </summary>
        public void TryOpen()
        {
            if (CanOpen())
                Open();
        }

        public void Toggle()
        {
            if (_window != null && _window.IsOpen)
            {
                Close();
            }
            else
            {
                TryOpen();
            }
        }

        private void PlayerTabEntryPressed(BaseButton.ButtonEventArgs args)
        {
            if (args.Button is not PlayerTabEntry button
                || button.PlayerUid == null)
                return;

            _clientConsoleHost.ExecuteCommand($"vv {button.PlayerUid}");
        }
    }
}
