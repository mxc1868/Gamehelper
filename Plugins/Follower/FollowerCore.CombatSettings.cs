namespace Follower;

using BloodyBot.Combat;
using GameHelper;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.Components;
using ImGuiNET;

public sealed partial class FollowerCore
{
    private string[] combatBuffs = [];
    private string[] combatSkills = [];
    private string combatScanStatus = string.Empty;

    private string CombatLabel(string key, string fallback) => this.PluginText.Label("combat." + key, fallback, "Combat" + key);

    private void DrawCombatSettings()
    {
        if (!ImGui.CollapsingHeader(this.CombatLabel("title", "Combat assistance"))) return;
        var changed = ImGui.Checkbox(this.CombatLabel("enabled", "Enable combat assistance"), ref this.Settings.Combat.Enabled);
        if (this.Settings.LocalCoopFollow)
            changed |= ImGui.Checkbox(this.CombatLabel("p2", "Monitor P2 (otherwise P1)"), ref this.Settings.CombatUseP2);
        ImGui.TextWrapped(this.PluginText.T("combat.hint", "Rules run top to bottom while following, including when already near the leader. Conditions within each rule are ANDed. Each action briefly releases movement, presses one key, then resumes following. P2 correction has priority. Preview sends no keys."));
        ImGui.TextWrapped(this.PluginText.T("combat.aim", "Keys use your existing game/controller mapping and current aim. Selecting P1/P2 only changes whose position and status are read; it does not route keys to that player. No automatic aiming or chasing."));
        if (ImGui.Button(this.CombatLabel("scan", "Scan selected character's buffs and skills"))) this.ScanCombatNames();
        if (this.combatScanStatus.Length > 0) ImGui.TextWrapped(this.combatScanStatus);
        ImGui.TextWrapped(this.PluginText.T("combat.scan_hint", "Scan names are a snapshot, not a live status display. Buff names match a case-insensitive substring; skill readiness uses the exact internal name. Leave optional conditions empty to ignore them. Unknown required data blocks the rule."));

        var rules = this.Settings.Combat.Rules;
        var remove = -1;
        var move = -1;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            ImGui.PushID(rule.Id);
            var open = ImGui.TreeNode($"{i + 1}. {rule.Name} [{CombatKeyName(rule.Key)}]###rule");
            if (open)
            {
                changed |= ImGui.Checkbox(this.CombatLabel("rule_enabled", "Enabled"), ref rule.Enabled);
                changed |= ImGui.InputText(this.CombatLabel("name", "Rule name"), ref rule.Name, 100);
                if (ImGui.BeginCombo(this.CombatLabel("key", "Skill key"), CombatKeyName(rule.Key)))
                {
                    if (ImGui.Selectable("—", rule.Key == 0)) { rule.Key = 0; changed = true; }
                    for (var key = 0x20; key <= 0x5A; key++)
                        if (CombatRule.IsKeyAllowed(key) && key != this.Settings.ToggleKey &&
                            ImGui.Selectable(CombatKeyName(key), key == rule.Key)) { rule.Key = key; changed = true; }
                    ImGui.EndCombo();
                }
                if (rule.Key == this.Settings.ToggleKey)
                    ImGui.TextWrapped(this.PluginText.T("combat.key_conflict", "This key is also the start/stop hotkey. The rule is blocked until one binding changes."));
                changed |= ImGui.Checkbox(this.CombatLabel("enemy", "Require nearby enemies"), ref rule.RequireEnemy);
                if (rule.RequireEnemy)
                {
                    foreach (var rarity in new[] { EnemyRarity.Normal, EnemyRarity.Magic, EnemyRarity.Rare, EnemyRarity.Unique })
                    {
                        var selected = (rule.Rarities & rarity) != 0;
                        if (ImGui.Checkbox(this.CombatLabel("rarity." + rarity, rarity.ToString()), ref selected))
                        { rule.Rarities = selected ? rule.Rarities | rarity : rule.Rarities & ~rarity; changed = true; }
                        if (rarity != EnemyRarity.Unique) ImGui.SameLine();
                    }
                    changed |= ImGui.SliderFloat(this.CombatLabel("range", "Enemy range (grid cells)"), ref rule.Range, 1, 150);
                    changed |= ImGui.SliderInt(this.CombatLabel("count", "Minimum matching enemies"), ref rule.MinimumEnemies, 1, 100);
                }
                changed |= ImGui.Checkbox(this.CombatLabel("life", "Life below (%)"), ref rule.UseLifeBelow);
                if (rule.UseLifeBelow) { ImGui.SameLine(); changed |= ImGui.SliderInt("##life", ref rule.LifeBelow, 1, 100); }
                changed |= ImGui.Checkbox(this.CombatLabel("es", "Energy shield below (%)"), ref rule.UseEnergyShieldBelow);
                if (rule.UseEnergyShieldBelow) { ImGui.SameLine(); changed |= ImGui.SliderInt("##es", ref rule.EnergyShieldBelow, 1, 100); }
                changed |= ImGui.Checkbox(this.CombatLabel("mana", "Mana below (%)"), ref rule.UseManaBelow);
                if (rule.UseManaBelow) { ImGui.SameLine(); changed |= ImGui.SliderInt("##mana", ref rule.ManaBelow, 1, 100); }
                changed |= ImGui.SliderInt(this.CombatLabel("minimum_mana", "Minimum mana % (0: ignore)"), ref rule.MinimumMana, 0, 100);
                changed |= this.DrawCombatName("buff_present", "Required buff", ref rule.RequiredBuff, this.combatBuffs);
                changed |= this.DrawCombatName("buff_missing", "Buff must be missing", ref rule.MissingBuff, this.combatBuffs);
                changed |= this.DrawCombatName("skill_ready", "Skill must be ready", ref rule.ReadySkill, this.combatSkills);
                changed |= ImGui.SliderInt(this.CombatLabel("cooldown", "Minimum repeat interval (ms)"), ref rule.CooldownMilliseconds, 300, 600000);
                changed |= ImGui.SliderInt(this.CombatLabel("press", "Key press duration (ms)"), ref rule.PressMilliseconds, 30, 200);
                changed |= ImGui.SliderInt(this.CombatLabel("pause", "Pause movement after key press (ms)"), ref rule.PauseMilliseconds, 0, 2000);
                if (i > 0 && ImGui.Button(this.CombatLabel("up", "Move up"))) move = i;
                if (i > 0) ImGui.SameLine();
                if (ImGui.Button(this.CombatLabel("remove", "Remove rule"))) remove = i;
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        if (remove >= 0) { rules.RemoveAt(remove); changed = true; }
        else if (move > 0) { (rules[move - 1], rules[move]) = (rules[move], rules[move - 1]); changed = true; }
        if (rules.Count < 32 && ImGui.Button(this.CombatLabel("add", "Add rare / unique rule")))
        {
            rules.Add(new() { Name = this.PluginText.T("combat.default_name", "Rare / Unique skill") });
            changed = true;
        }
        ImGui.TextWrapped(this.PluginText.T("combat.intervals", "The repeat interval is a local input throttle, not the game's cooldown. At most one key is sent per 300 ms; rules sharing a key share its throttle. Percentages use unreserved life/mana; a missing resource (e.g. no ES) does not satisfy a below-threshold condition."));
        if (changed) { this.ClearNavigation(); this.SaveSettings(); }
    }

    private bool DrawCombatName(string key, string label, ref string value, string[] names)
    {
        var changed = ImGui.InputText(this.CombatLabel(key, label), ref value, 256);
        ImGui.SameLine();
        if (ImGui.BeginCombo("##choices_" + key, "…", ImGuiComboFlags.WidthFitPreview))
        {
            if (ImGui.Selectable("—", value.Length == 0)) { value = string.Empty; changed = true; }
            foreach (var name in names)
                if (ImGui.Selectable(name, value == name)) { value = name; changed = true; }
            ImGui.EndCombo();
        }
        return changed;
    }

    private void ScanCombatNames()
    {
        this.combatBuffs = this.combatSkills = [];
        this.combatScanStatus = this.PluginText.T("combat.scan_failed", "Selected character's data is unavailable. Check character selection and move into the same area.");
        try
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState) return;
            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            var players = ReadPlayers(area);
            var name = this.Settings.CombatUseP2 ? this.Settings.P2Name : this.Settings.P1Name;
            var matches = players.Where(p => this.Settings.LocalCoopFollow
                ? string.Equals(p.Key.Name, name, StringComparison.OrdinalIgnoreCase)
                : p.Value.Address == area.Player.Address).Select(p => p.Value).ToArray();
            if (matches.Length != 1) return;
            var selected = matches[0];
            if (CombatSnapshotReader.ReadComponent(selected, out Buffs buffs)) this.combatBuffs = buffs.StatusEffects.Keys.Order().ToArray();
            if (CombatSnapshotReader.ReadComponent(selected, out Actor actor)) this.combatSkills = actor.ActiveSkills.Keys.Order().ToArray();
            this.combatScanStatus = this.PluginText.F("combat.scanned", "Last scan: {0} buffs, {1} skills", this.combatBuffs.Length, this.combatSkills.Length);
        }
        catch (Exception ex) { this.error = ex.Message; }
    }
}
