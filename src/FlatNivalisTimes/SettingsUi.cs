using HarmonyLib;
using Nivalis;
using Nivalis.Localization;
using Nivalis.UI.Concrete;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NivalisNights.FlatNivalisTimes;

internal static class SettingsUi
{
    private static readonly Dictionary<IntPtr, Row> Rows = new();
    private static readonly List<IntPtr> Dead = new();
    private static readonly Il2CppSystem.Action LanguageChanged = (Action)RefreshAll;
    private static ActionNonAlloc? languageEvents;
    private const string RowName = "NivalisNights.FlatNivalisTimes.Accessibility";

    private static void SubscribeLanguage()
    {
        if (languageEvents != null) return;
        // UI Awake has initialized the original settings/localization path. Do not
        // acquire a game-owned event while BepInEx is still loading plugins.
        var events = LocObjectsDB.OnLanguageChanged ?? throw new InvalidOperationException("Language notifications are not ready.");
        events.Add(LanguageChanged);
        languageEvents = events;
    }

    internal static void Ensure(GameplaySettingsUI panel)
    {
        if (!PaperStates.Alive(panel)) return;
        SubscribeLanguage();
        if (Rows.TryGetValue(panel.Pointer, out var existing))
        {
            if (existing.Alive) { existing.Refresh(); existing.WireNavigation(); return; }
            existing.Dispose();
            Rows.Remove(panel.Pointer);
        }
        Rows.Add(panel.Pointer, new Row(panel));
    }

    internal static void Remove(GameplaySettingsUI panel)
    {
        if (Rows.TryGetValue(panel.Pointer, out var row)) { row.Dispose(); Rows.Remove(panel.Pointer); }
    }

    internal static void Navigation(GameplaySettingsUI panel)
    {
        if (Rows.TryGetValue(panel.Pointer, out var row) && row.Alive) row.WireNavigation();
    }

    internal static void RefreshAll()
    {
        Dead.Clear();
        foreach (var (key, row) in Rows)
        {
            if (row.Alive) row.Refresh();
            else { row.Dispose(); Dead.Add(key); }
        }
        foreach (var key in Dead) Rows.Remove(key);
    }

    internal static void Shutdown()
    {
        var cleanup = new CleanupBatch();
        cleanup.Attempt(() => { languageEvents?.Remove(LanguageChanged); languageEvents = null; });
        foreach (var key in Rows.Keys.ToArray())
            cleanup.Attempt(() => { Rows[key].Dispose(); Rows.Remove(key); });
        cleanup.ThrowIfFailed();
    }

    private sealed class Row
    {
        private readonly GameplaySettingsUI panel;
        private readonly GameObject owner;
        private readonly Toggle toggle;
        private readonly TMP_Text label;
        private readonly LayoutElement layout;
        private readonly LocalizedTextSetFont font;
        private readonly Toggle anchor;
        private readonly UnityAction<bool> changed;
        private Selectable? next;
        private bool refreshing;
        internal bool Alive => PaperStates.Alive(panel) && PaperStates.Alive(owner) && PaperStates.Alive(toggle);

        internal Row(GameplaySettingsUI panel)
        {
            this.panel = panel;
            var source = panel.disableHeadBob;
            if (!PaperStates.Alive(source) || !PaperStates.Alive(source.toggle) || !PaperStates.Alive(panel.scroll))
                throw new InvalidOperationException("Original gameplay accessibility template is missing.");
            anchor = source.toggle;
            var content = panel.scroll.content;
            // Original puts ToggleSettingUI AND BetterToggle on the row itself, not a child.
            if (source.transform.parent.Pointer != content.Pointer || anchor.gameObject.Pointer != source.gameObject.Pointer)
                throw new InvalidOperationException("Gameplay template is not the investigated direct content row.");
            if (content.Find(RowName)) throw new InvalidOperationException("An unowned accessibility row already exists.");
            var staging = new GameObject(RowName + ".Staging");
            staging.SetActive(false);
            staging.transform.SetParent(panel.transform, false);
            GameObject? clone = null;
            try
            {
                // Inactive ancestry prevents Awake/OnEnable on copied settings/localization behaviours.
                clone = Object.Instantiate(source.gameObject, staging.transform, false);
                clone.SetActive(false);
                clone.name = RowName;
                foreach (var setting in clone.GetComponentsInChildren<SettingUIElementBase>(true)) Object.DestroyImmediate(setting);
                foreach (var localization in clone.GetComponentsInChildren<LocalizedStaticUILabel>(true)) Object.DestroyImmediate(localization);
                if (clone.GetComponentsInChildren<SettingUIElementBase>(true).Length != 0)
                    throw new InvalidOperationException("Native setting bindings remained in the accessibility clone.");
                toggle = clone.GetComponent<Toggle>();
                label = clone.GetComponentInChildren<TMP_Text>(true);
                layout = clone.GetComponent<LayoutElement>();
                if (!PaperStates.Alive(toggle) || !PaperStates.Alive(label) || !PaperStates.Alive(layout))
                    throw new InvalidOperationException("Accessibility row visual components are missing.");
                // The source label's localization component also selected its font; it was removed.
                // Install the original font-only helper while still inactive and populate its field
                // before Awake. This helper never registers a translation key or setting value.
                font = label.GetComponent<LocalizedTextSetFont>();
                if (!PaperStates.Alive(font)) font = label.gameObject.AddComponent<LocalizedTextSetFont>();
                font.textDisplayer = label.GetComponent<TextMeshProUGUI>();
                // Replace the event object, including persistent calls; no native setting callback survives.
                toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.group = null;
                toggle.interactable = true;
                changed = (UnityAction<bool>)(Action<bool>)OnChanged;
                toggle.onValueChanged.AddListener(changed);
                owner = clone;
                clone.transform.SetParent(content, false);
                clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
                if (clone.TryGetComponent<CanvasGroup>(out var canvasGroup))
                {
                    canvasGroup.alpha = 1f;
                    canvasGroup.interactable = true;
                    canvasGroup.blocksRaycasts = true;
                }
                Refresh();
                clone.SetActive(true);
                Refresh();
                WireNavigation();
                LayoutRebuilder.MarkLayoutForRebuild(content);
                Plugin.Logger.LogInfo($"Gameplay accessibility row added to panel {panel.GetInstanceID()}; native setting bindings=0; mod config only. RuntimeAccepted=false.");
            }
            catch
            {
                if (PaperStates.Alive(clone)) Object.Destroy(clone);
                throw;
            }
            finally { Object.Destroy(staging); }
        }

        private void OnChanged(bool value)
        {
            if (!refreshing) AccessibilitySettings.Request(value);
        }

        internal void Refresh()
        {
            refreshing = true;
            try
            {
                toggle.SetIsOnWithoutNotify(AccessibilitySettings.Enabled);
                font.Change();
                var text = Texts.Current;
                label.text = text.Title + "\n<size=65%>" + (AccessibilitySettings.HasError ? text.Error : text.Immediate) + "</size>";
                label.richText = true;
                label.enableWordWrapping = true;
                var width = label.rectTransform.rect.width;
                if (width < 1f) width = label.rectTransform.sizeDelta.x;
                if (width < 1f) width = 325f;
                layout.minHeight = 65f;
                layout.preferredHeight = Mathf.Max(65f, label.GetPreferredValues(label.text, width, float.PositiveInfinity).y + 10f);
                LayoutRebuilder.MarkLayoutForRebuild(panel.scroll.content);
            }
            finally { refreshing = false; }
        }

        internal void WireNavigation()
        {
            // Respect actual visible sibling order, including rows added by other mods.
            var content = panel.scroll.content;
            next = null;
            for (int index = owner.transform.GetSiblingIndex() + 1; index < content.childCount; index++)
            {
                var child = content.GetChild(index);
                if (!child.gameObject.activeInHierarchy) continue;
                var candidate = child.GetComponent<Selectable>();
                if (PaperStates.Alive(candidate) && candidate.IsInteractable()) { next = candidate; break; }
            }
            var anchorLinks = Links(anchor);
            if (!PaperStates.Alive(next)) next = anchorLinks.selectOnDown == toggle ? null : anchorLinks.selectOnDown;
            Connect(toggle, anchor, next);
            Connect(anchor, anchorLinks.selectOnUp, toggle);
            if (PaperStates.Alive(next)) Connect(next!, toggle, Links(next!).selectOnDown);
        }

        private static UnityEngine.UI.Navigation Links(Selectable selectable)
        {
            var navigation = selectable.navigation;
            var manual = selectable.GetComponent<ManualUINavigation>();
            if (!PaperStates.Alive(manual)) return navigation;
            // Original UpdateManualUINavigation writes these locks, not just Selectable.navigation.
            // Preserve its footer/other-mod destinations when splicing this one row.
            if (manual.lockSelectOnUp?.HasValue == true) navigation.selectOnUp = manual.lockSelectOnUp.Value;
            if (manual.lockSelectOnDown?.HasValue == true) navigation.selectOnDown = manual.lockSelectOnDown.Value;
            if (manual.lockSelectOnLeft?.HasValue == true) navigation.selectOnLeft = manual.lockSelectOnLeft.Value;
            if (manual.lockSelectOnRight?.HasValue == true) navigation.selectOnRight = manual.lockSelectOnRight.Value;
            return navigation;
        }

        private static void Connect(Selectable selectable, Selectable? up, Selectable? down)
        {
            var navigation = Links(selectable);
            navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            selectable.navigation = navigation;
            var manual = selectable.GetComponent<ManualUINavigation>();
            if (PaperStates.Alive(manual)) manual.SetCustomNavigation(up, down, navigation.selectOnLeft, navigation.selectOnRight);
        }

        internal void Dispose()
        {
            if (!PaperStates.Alive(owner)) return;
            var cleanup = new CleanupBatch();
            cleanup.Attempt(() => { if (PaperStates.Alive(toggle)) toggle.onValueChanged.RemoveListener(changed); });
            cleanup.Attempt(() => {
                if (PaperStates.Alive(anchor) && Links(anchor).selectOnDown == toggle)
                    Connect(anchor, Links(anchor).selectOnUp, PaperStates.Alive(next) ? next : null);
            });
            cleanup.Attempt(() => {
                if (PaperStates.Alive(next) && Links(next!).selectOnUp == toggle)
                    Connect(next!, PaperStates.Alive(anchor) ? anchor : null, Links(next!).selectOnDown);
            });
            cleanup.ThrowIfFailed();
            Object.Destroy(owner);
        }
    }
}

[HarmonyPatch(typeof(GameplaySettingsUI), "Awake")]
internal static class GameplayAccessibilityCreatePatch
{
    private static void Postfix(GameplaySettingsUI __instance)
    {
        try { SettingsUi.Ensure(__instance); }
        catch (Exception error) { Plugin.ReportOnce("settings creation rejected", error); }
    }
}

[HarmonyPatch(typeof(GameplaySettingsUI), "BeforeDisplay")]
internal static class GameplayAccessibilityDisplayPatch
{
    private static void Postfix(GameplaySettingsUI __instance)
    {
        try { SettingsUi.Ensure(__instance); }
        catch (Exception error) { Plugin.ReportOnce("settings display rejected", error); }
    }
}

[HarmonyPatch(typeof(GameplaySettingsUI), "UpdateManualUINavigation")]
internal static class GameplayAccessibilityNavigationPatch
{
    private static void Postfix(GameplaySettingsUI __instance)
    {
        try { SettingsUi.Navigation(__instance); }
        catch (Exception error) { Plugin.ReportOnce("settings navigation rejected", error); }
    }
}

[HarmonyPatch(typeof(GameplaySettingsUI), "RestoreCurrentCategoryToDefaults")]
internal static class GameplayAccessibilityDefaultsPatch
{
    private static void Postfix() => AccessibilitySettings.Request(true);
}

[HarmonyPatch(typeof(GameplaySettingsUI), "OnDestroy")]
internal static class GameplayAccessibilityDestroyPatch
{
    private static void Prefix(GameplaySettingsUI __instance)
    {
        try { SettingsUi.Remove(__instance); }
        catch (Exception error) { Plugin.ReportOnce("settings cleanup skipped", error); }
    }
}
