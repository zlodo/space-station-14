using System.Collections.Generic;
using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Ghost.Components;
using Content.Server.Headset;
using Content.Server.Inventory.Components;
using Content.Server.Items;
using Content.Server.MoMMI;
using Content.Server.Preferences.Managers;
using Content.Server.Radio.EntitySystems;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using static Content.Server.Chat.Managers.IChatManager;

namespace Content.Server.Chat.Managers
{
    /// <summary>
    ///     Dispatches chat messages to clients.
    /// </summary>
    internal sealed class ChatManager : IChatManager
    {
        private static readonly Dictionary<string, string> PatronOocColors = new()
        {
            // I had plans for multiple colors and those went nowhere so...
            { "nuclear_operative", "#aa00ff" },
            { "syndicate_agent", "#aa00ff" },
            { "revolutionary", "#aa00ff" }
        };

        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly IServerNetManager _netManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IMoMMILink _mommiLink = default!;
        [Dependency] private readonly IAdminManager _adminManager = default!;
        [Dependency] private readonly IServerPreferencesManager _preferencesManager = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;

        /// <summary>
        /// The maximum length a player-sent message can be sent
        /// </summary>
        public int MaxMessageLength => _configurationManager.GetCVar(CCVars.ChatMaxMessageLength);

        private const int VoiceRange = 7; // how far voice goes in world units

        //TODO: make prio based?
        private readonly List<TransformChat> _chatTransformHandlers = new();
        private bool _oocEnabled = true;
        private bool _adminOocEnabled = true;

        public void Initialize()
        {
            _netManager.RegisterNetMessage<MsgChatMessage>();

            _configurationManager.OnValueChanged(CCVars.OocEnabled, OnOocEnabledChanged, true);
            _configurationManager.OnValueChanged(CCVars.AdminOocEnabled, OnAdminOocEnabledChanged, true);
        }

        private void OnOocEnabledChanged(bool val)
        {
            _oocEnabled = val;
            DispatchServerAnnouncement(Loc.GetString(val ? "chat-manager-ooc-chat-enabled-message" : "chat-manager-ooc-chat-disabled-message"));
        }

        private void OnAdminOocEnabledChanged(bool val)
        {
            _adminOocEnabled = val;
            DispatchServerAnnouncement(Loc.GetString(val ? "chat-manager-admin-ooc-chat-enabled-message" : "chat-manager-admin-ooc-chat-disabled-message"));
        }

        public void DispatchServerAnnouncement(string message)
        {
            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.Server;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-server-wrap-message");
            _netManager.ServerSendToAll(msg);
            Logger.InfoS("SERVER", message);
        }

        public void DispatchStationAnnouncement(string message, string sender = "CentComm", bool playDefaultSound = true)
        {
            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.Radio;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender));
            _netManager.ServerSendToAll(msg);
            if (playDefaultSound)
            {
                SoundSystem.Play(Filter.Broadcast(), "/Audio/Announcements/announce.ogg", AudioParams.Default.WithVolume(-2f));
            }
        }

        public void DispatchServerMessage(IPlayerSession player, string message)
        {
            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.Server;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-server-wrap-message");
            _netManager.ServerSendMessage(msg, player.ConnectedClient);
        }

        public void EntitySay(EntityUid source, string message, bool hideChat=false)
        {
            if (!EntitySystem.Get<ActionBlockerSystem>().CanSpeak(source))
            {
                return;
            }

            // Check if message exceeds the character limit if the sender is a player
            if (_entManager.TryGetComponent(source, out ActorComponent? actor) &&
                message.Length > MaxMessageLength)
            {
                var feedback = Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", MaxMessageLength));

                DispatchServerMessage(actor.PlayerSession, feedback);

                return;
            }

            foreach (var handler in _chatTransformHandlers)
            {
                //TODO: rather return a bool and use a out var?
                message = handler(source, message);
            }

            message = message.Trim();

            // We'll try to avoid using MapPosition as EntityCoordinates can early-out and potentially be faster for common use cases
            // Downside is it may potentially convert to MapPosition unnecessarily.
            var sourceMapId = _entManager.GetComponent<TransformComponent>(source).MapID;
            var sourceCoords = _entManager.GetComponent<TransformComponent>(source).Coordinates;

            var clients = new List<INetChannel>();

            foreach (var player in _playerManager.Sessions)
            {
                if (player.AttachedEntity is not {Valid: true} playerEntity)
                    continue;

                var transform = _entManager.GetComponent<TransformComponent>(playerEntity);

                if (transform.MapID != sourceMapId ||
                    !_entManager.HasComponent<GhostComponent>(playerEntity) &&
                    !sourceCoords.InRange(_entManager, transform.Coordinates, VoiceRange))
                    continue;

                clients.Add(player.ConnectedClient);
            }

            if (message.StartsWith(';'))
            {
                // Remove semicolon
                message = message.Substring(1).TrimStart();

                // Capitalize first letter
                message = message[0].ToString().ToUpper() +
                          message.Remove(0, 1);

                if (_entManager.TryGetComponent(source, out InventoryComponent? inventory) &&
                    inventory.TryGetSlotItem(EquipmentSlotDefines.Slots.EARS, out ItemComponent? item) &&
                    _entManager.TryGetComponent(item.Owner, out HeadsetComponent? headset))
                {
                    headset.RadioRequested = true;
                }
                else
                {
                    source.PopupMessage(Loc.GetString("chat-manager-no-headset-on-message"));
                }
            }
            else
            {
                // Capitalize first letter
                message = message[0].ToString().ToUpper() +
                          message.Remove(0, 1);
            }

            var listeners = EntitySystem.Get<ListeningSystem>();
            listeners.PingListeners(source, message);

            message = FormattedMessage.EscapeText(message);

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.Local;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-entity-say-wrap-message",("entityName", Name: _entManager.GetComponent<MetaDataComponent>(source).EntityName));
            msg.SenderEntity = source;
            msg.HideChat = hideChat;
            _netManager.ServerSendToMany(msg, clients);
        }

        public void EntityMe(EntityUid source, string action)
        {
            if (!EntitySystem.Get<ActionBlockerSystem>().CanEmote(source))
            {
                return;
            }

            // Check if entity is a player
            if (!_entManager.TryGetComponent(source, out ActorComponent? actor))
            {
                return;
            }

            // Check if message exceeds the character limit
            if (action.Length > MaxMessageLength)
            {
                DispatchServerMessage(actor.PlayerSession, Loc.GetString("chat-manager-max-message-length-exceeded-message",("limit", MaxMessageLength)));
                return;
            }

            action = FormattedMessage.EscapeText(action);

            var clients = Filter.Empty()
                .AddInRange(_entManager.GetComponent<TransformComponent>(source).MapPosition, VoiceRange)
                .Recipients
                .Select(p => p.ConnectedClient)
                .ToList();

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.Emotes;
            msg.Message = action;
            msg.MessageWrap = Loc.GetString("chat-manager-entity-me-wrap-message", ("entityName",Name: _entManager.GetComponent<MetaDataComponent>(source).EntityName));
            msg.SenderEntity = source;
            _netManager.ServerSendToMany(msg, clients);
        }

        public void SendOOC(IPlayerSession player, string message)
        {
            if (_adminManager.IsAdmin(player))
            {
                if (!_adminOocEnabled)
                {
                    return;
                }
            }
            else if (!_oocEnabled)
            {
                return;
            }

            // Check if message exceeds the character limit
            if (message.Length > MaxMessageLength)
            {
                DispatchServerMessage(player, Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", MaxMessageLength)));
                return;
            }

            message = FormattedMessage.EscapeText(message);

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.OOC;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-send-ooc-wrap-message", ("playerName",player.Name));
            if (_adminManager.HasAdminFlag(player, AdminFlags.Admin))
            {
                var prefs = _preferencesManager.GetPreferences(player.UserId);
                msg.MessageColorOverride = prefs.AdminOOCColor;
            }
            if (player.ConnectedClient.UserData.PatronTier is { } patron &&
                     PatronOocColors.TryGetValue(patron, out var patronColor))
            {
                msg.MessageWrap = Loc.GetString("chat-manager-send-ooc-patron-wrap-message", ("patronColor", patronColor),("playerName", player.Name));
            }

            //TODO: player.Name color, this will need to change the structure of the MsgChatMessage
            _netManager.ServerSendToAll(msg);

            _mommiLink.SendOOCMessage(player.Name, message);
        }

        public void SendDeadChat(IPlayerSession player, string message)
        {
            // Check if message exceeds the character limit
            if (message.Length > MaxMessageLength)
            {
                DispatchServerMessage(player, Loc.GetString("chat-manager-max-message-length-exceeded-message",("limit", MaxMessageLength)));
                return;
            }

            message = FormattedMessage.EscapeText(message);

            var clients = GetDeadChatClients();

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.Dead;
            msg.Message = message;

            var playerName = player.AttachedEntity is {Valid: true} playerEntity
                ? _entManager.GetComponent<MetaDataComponent>(playerEntity).EntityName
                : "???";
            msg.MessageWrap = Loc.GetString("chat-manager-send-dead-chat-wrap-message",
                                            ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                                            ("playerName", (playerName)));
            msg.SenderEntity = player.AttachedEntity.GetValueOrDefault();
            _netManager.ServerSendToMany(msg, clients.ToList());
        }

        public void SendAdminDeadChat(IPlayerSession player, string message)
        {
            // Check if message exceeds the character limit
            if (message.Length > MaxMessageLength)
            {
                DispatchServerMessage(player, Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", MaxMessageLength)));
                return;
            }

            message = FormattedMessage.EscapeText(message);

            var clients = GetDeadChatClients();

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.Dead;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-send-admin-dead-chat-wrap-message",
                                            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                                            ("userName", player.ConnectedClient.UserName));
            _netManager.ServerSendToMany(msg, clients.ToList());
        }

        private IEnumerable<INetChannel> GetDeadChatClients()
        {
            return Filter.Empty()
                .AddWhereAttachedEntity(uid => _entManager.HasComponent<GhostComponent>(uid))
                .Recipients
                .Union(_adminManager.ActiveAdmins)
                .Select(p => p.ConnectedClient);
        }

        public void SendAdminChat(IPlayerSession player, string message)
        {
            // Check if message exceeds the character limit
            if (message.Length > MaxMessageLength)
            {
                DispatchServerMessage(player, Loc.GetString("chat-manager-max-message-length-exceeded-message", ("limit", MaxMessageLength)));
                return;
            }

            message = FormattedMessage.EscapeText(message);

            var clients = _adminManager.ActiveAdmins.Select(p => p.ConnectedClient);

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();

            msg.Channel = ChatChannel.Admin;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-send-admin-chat-wrap-message",
                                            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                                            ("playerName", player.Name));
            _netManager.ServerSendToMany(msg, clients.ToList());
        }

        public void SendAdminAnnouncement(string message)
        {
            var clients = _adminManager.ActiveAdmins.Select(p => p.ConnectedClient);

            message = FormattedMessage.EscapeText(message);

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();

            msg.Channel = ChatChannel.Admin;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-send-admin-announcement-wrap-message",
                                            ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")));

            _netManager.ServerSendToMany(msg, clients.ToList());
        }

        public void SendHookOOC(string sender, string message)
        {
            message = FormattedMessage.EscapeText(message);

            var msg = _netManager.CreateNetMessage<MsgChatMessage>();
            msg.Channel = ChatChannel.OOC;
            msg.Message = message;
            msg.MessageWrap = Loc.GetString("chat-manager-send-hook-ooc-wrap-message", ("senderName", sender));
            _netManager.ServerSendToAll(msg);
        }

        public void RegisterChatTransform(TransformChat handler)
        {
            // TODO: Literally just make this an event...
            _chatTransformHandlers.Add(handler);
        }
    }
}
