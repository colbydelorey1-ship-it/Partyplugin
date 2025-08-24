using Rocket.API;               // <-- make sure this using exists
// other usings stay the same

namespace PartyPlugin
{
    public class PartyConfig : IRocketPluginConfiguration
    {
        public int InviteTimeoutSeconds = 60;
        public string ChatColorHex = "00FFAA";
        public bool AllowInvitesAcrossGroups = false;

        public void LoadDefaults()
        {
            InviteTimeoutSeconds = 60;
            ChatColorHex = "00FFAA";
            AllowInvitesAcrossGroups = false;
        }
    }

    public class PartyPlugin : Rocket.Core.Plugins.RocketPlugin<PartyConfig>
    {
        // ... rest of your file unchanged ...
    }
}
        private readonly Dictionary<ulong, List<PartyInvite>> _pendingInvites = new Dictionary<ulong, List<PartyInvite>>();

        protected override void Load()
        {
            Logger.Log("[PartyPlugin] Loaded. Commands: /party <player> | /party accept [player] | /party deny [player] | /party leave");
        }

        protected override void Unload()
        {
            _pendingInvites.Clear();
            Logger.Log("[PartyPlugin] Unloaded.");
        }

        internal void AddInvite(UnturnedPlayer inviter, UnturnedPlayer invitee)
        {
            var now = DateTime.UtcNow;
            var expireAt = now.AddSeconds(Configuration.Instance.InviteTimeoutSeconds);

            var inv = new PartyInvite
            {
                Inviter = inviter.CSteamID,
                Invitee = invitee.CSteamID,
                CreatedAt = now,
                ExpiresAt = expireAt
            };

            if (!_pendingInvites.TryGetValue(invitee.CSteamID.m_SteamID, out var list))
            {
                list = new List<PartyInvite>();
                _pendingInvites[invitee.CSteamID.m_SteamID] = list;
            }

            list.RemoveAll(i => i.IsExpired);
            list.Add(inv);

            UnturnedChat.Say(inviter, $"Invite sent to {invitee.CharacterName}. They have {Configuration.Instance.InviteTimeoutSeconds}s to respond.", HexColor);
            UnturnedChat.Say(invitee, $"{inviter.CharacterName} invited you to their party. Use /party accept or /party deny.", HexColor);
        }

        internal bool AcceptInvite(UnturnedPlayer invitee, string optionalInviterNameOrId = null)
        {
            if (!_pendingInvites.TryGetValue(invitee.CSteamID.m_SteamID, out var list))
            {
                UnturnedChat.Say(invitee, "You have no pending party invites.", HexColor);
                return false;
            }

            list.RemoveAll(i => i.IsExpired);
            if (list.Count == 0)
            {
                UnturnedChat.Say(invitee, "You have no pending party invites.", HexColor);
                return false;
            }

            PartyInvite chosen;

            if (!string.IsNullOrWhiteSpace(optionalInviterNameOrId))
            {
                var inviterTarget = Util.FindPlayerByNameOrId(optionalInviterNameOrId);
                if (inviterTarget == null)
                {
                    UnturnedChat.Say(invitee, "Could not find the inviter you specified.", HexColor);
                    return false;
                }

                chosen = list.LastOrDefault(i => i.Inviter == inviterTarget.CSteamID);
                if (chosen.Inviter.m_SteamID == 0)
                {
                    UnturnedChat.Say(invitee, "No pending invite from that player.", HexColor);
                    return false;
                }
            }
            else
            {
                chosen = list.Last();
            }

            var inviter = UnturnedPlayer.FromCSteamID(chosen.Inviter);
            if (inviter == null)
            {
                UnturnedChat.Say(invitee, "Inviter is no longer online.", HexColor);
                list.Remove(chosen);
                return false;
            }

            if (!Configuration.Instance.AllowInvitesAcrossGroups &&
                IsInGroup(invitee) && !SameGroup(invitee, inviter))
            {
                UnturnedChat.Say(invitee, "You are already in a different group. Leave it first.", HexColor);
                return false;
            }

            var inviterGroup = inviter.Player.quests.groupID;
            if (inviterGroup == CSteamID.Nil || inviterGroup.m_SteamID == 0)
            {
                if (!TryCreateGroupFor(inviter, out inviterGroup))
                {
                    UnturnedChat.Say(invitee, "Could not create a party for the inviter. Tell them to try again.", HexColor);
                    return false;
                }
            }

            try
            {
                invitee.Player.quests.ServerAssignToGroup(inviterGroup, EPlayerGroupRank.MEMBER);
                UnturnedChat.Say(invitee, $"You joined {inviter.CharacterName}'s party.", HexColor);
                UnturnedChat.Say(inviter, $"{invitee.CharacterName} joined your party.", HexColor);
                list.Remove(chosen);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                UnturnedChat.Say(invitee, "Failed to join party due to a server error.", HexColor);
                return false;
            }
        }

        internal bool DenyInvite(UnturnedPlayer invitee, string optionalInviterNameOrId = null)
        {
            if (!_pendingInvites.TryGetValue(invitee.CSteamID.m_SteamID, out var list))
            {
                UnturnedChat.Say(invitee, "You have no pending party invites.", HexColor);
                return false;
            }

            list.RemoveAll(i => i.IsExpired);
            if (list.Count == 0)
            {
                UnturnedChat.Say(invitee, "You have no pending party invites.", HexColor);
                return false;
            }

            PartyInvite chosen;

            if (!string.IsNullOrWhiteSpace(optionalInviterNameOrId))
            {
                var inviterTarget = Util.FindPlayerByNameOrId(optionalInviterNameOrId);
                if (inviterTarget == null)
                {
                    UnturnedChat.Say(invitee, "Could not find the inviter you specified.", HexColor);
                    return false;
                }
                chosen = list.LastOrDefault(i => i.Inviter == inviterTarget.CSteamID);
                if (chosen.Inviter.m_SteamID == 0)
                {
                    UnturnedChat.Say(invitee, "No pending invite from that player.", HexColor);
                    return false;
                }
            }
            else
            {
                chosen = list.Last();
            }

            var inviter = UnturnedPlayer.FromCSteamID(chosen.Inviter);
            if (inviter != null)
                UnturnedChat.Say(inviter, $"{invitee.CharacterName} denied your party invite.", HexColor);

            UnturnedChat.Say(invitee, "Invite denied.", HexColor);
            list.Remove(chosen);
            return true;
        }

        internal bool LeaveParty(UnturnedPlayer player)
        {
            var gid = player.Player.quests.groupID;
            if (gid == CSteamID.Nil || gid.m_SteamID == 0)
            {
                UnturnedChat.Say(player, "You’re not in a party.", HexColor);
                return false;
            }

            try
            {
                player.Player.quests.ServerAssignToGroup(CSteamID.Nil, EPlayerGroupRank.NONE);
                UnturnedChat.Say(player, "You left the party.", HexColor);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                UnturnedChat.Say(player, "Couldn’t leave the party due to a server error.", HexColor);
                return false;
            }
        }

        private static bool IsInGroup(UnturnedPlayer player)
        {
            var gid = player.Player.quests.groupID;
            return gid != CSteamID.Nil && gid.m_SteamID != 0;
        }

        private static bool SameGroup(UnturnedPlayer a, UnturnedPlayer b)
        {
            return a.Player.quests.groupID == b.Player.quests.groupID;
        }

        private bool TryCreateGroupFor(UnturnedPlayer owner, out CSteamID groupId)
        {
            groupId = CSteamID.Nil;

            try
            {
                bool created = false;
                CSteamID newGroup;

                var gm = typeof(GroupManager);
                var method = gm.GetMethod("serverCreateGroup") ?? gm.GetMethod("ServerCreateGroup");
                if (method != null)
                {
                    object[] args = new object[] { default(CSteamID) };
                    created = (bool)method.Invoke(null, args);
                    newGroup = (CSteamID)args[0];
                }
                else
                {
                    method = gm.GetMethod("CreateGroup");
                    if (method != null && method.ReturnType == typeof(CSteamID))
                    {
                        newGroup = (CSteamID)method.Invoke(null, null);
                        created = newGroup != CSteamID.Nil && newGroup.m_SteamID != 0;
                    }
                    else
                    {
                        UnturnedChat.Say(owner, "Server does not expose a creatable group API.", HexColor);
                        return false;
                    }
                }

                if (!created || newGroup == CSteamID.Nil || newGroup.m_SteamID == 0)
                {
                    UnturnedChat.Say(owner, "Failed to create a new party.", HexColor);
                    return false;
                }

                owner.Player.quests.ServerAssignToGroup(newGroup, EPlayerGroupRank.OWNER);
                groupId = newGroup;
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                return false;
            }
        }

        internal string HexColor => $"#{Configuration.Instance.ChatColorHex}";
    }

    internal struct PartyInvite
    {
        public CSteamID Inviter;
        public CSteamID Invitee;
        public DateTime CreatedAt;
        public DateTime ExpiresAt;

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }

    public static class Util
    {
        public static UnturnedPlayer FindPlayerByNameOrId(string query)
        {
            if (string.IsNullOrEmpty(query)) return null;

            if (ulong.TryParse(query, out var id))
            {
                var byId = UnturnedPlayer.FromCSteamID(new CSteamID(id));
                if (byId != null) return byId;
            }

            var all = Provider.clients;
            UnturnedPlayer best = null;
            int bestScore = int.MinValue;

            foreach (var sp in all)
            {
                var up = UnturnedPlayer.FromSteamPlayer(sp);
                if (up == null) continue;
                var name = up.CharacterName ?? "";
                var score = ScoreName(name, query);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = up;
                }
            }

            return bestScore > 0 ? best : null;
        }

        private static int ScoreName(string source, string needle)
        {
            source = source.ToLowerInvariant();
            needle = needle.ToLowerInvariant();
            if (source == needle) return 1000;
            if (source.Contains(needle)) return 100 + needle.Length;
            return source.StartsWith(needle) ? 80 : 0;
        }
    }

    public class CommandParty : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "party";
        public string Help => "Invite players to your party, accept/deny invites, or leave your party.";
        public string Syntax => "/party <player> | /party accept [player] | /party deny [player] | /party leave";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string> { "party.use" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            var player = caller as UnturnedPlayer;
            if (player == null) return;

            var plugin = (PartyPlugin)Rocket.Core.R.Plugins.GetPlugins().FirstOrDefault(p => p is PartyPlugin);
            if (plugin == null)
            {
                UnturnedChat.Say(player, "Party plugin not loaded.", "#FF5555");
                return;
            }

            if (command.Length == 0)
            {
                UnturnedChat.Say(player, Syntax, plugin.HexColor);
                return;
            }

            var sub = command[0].ToLowerInvariant();

            if (sub == "accept")
            {
                string who = command.Length >= 2 ? string.Join(" ", command.Skip(1)) : null;
                plugin.AcceptInvite(player, who);
                return;
            }

            if (sub == "deny")
            {
                string who = command.Length >= 2 ? string.Join(" ", command.Skip(1)) : null;
                plugin.DenyInvite(player, who);
                return;
            }

            if (sub == "leave")
            {
                plugin.LeaveParty(player);
                return;
            }

            var targetName = string.Join(" ", command);
            var target = Util.FindPlayerByNameOrId(targetName);
            if (target == null)
            {
                UnturnedChat.Say(player, "Player not found.", plugin.HexColor);
                return;
            }

            if (target.CSteamID == player.CSteamID)
            {
                UnturnedChat.Say(player, "You cannot invite yourself.", plugin.HexColor);
                return;
            }

            if (!plugin.Configuration.Instance.AllowInvitesAcrossGroups)
            {
                bool inviterInGroup = player.Player.quests.groupID != CSteamID.Nil && player.Player.quests.groupID.m_SteamID != 0;
                bool targetInGroup = target.Player.quests.groupID != CSteamID.Nil && target.Player.quests.groupID.m_SteamID != 0;

                if (inviterInGroup && targetInGroup && player.Player.quests.groupID != target.Player.quests.groupID)
                {
                    UnturnedChat.Say(player, $"{target.CharacterName} is in a different group. They must leave first.", plugin.HexColor);
                    return;
                }
            }

            plugin.AddInvite(player, target);
        }
    }
}
