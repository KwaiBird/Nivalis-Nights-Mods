using Nivalis;
using Nivalis.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static NivalisNights.BoatControlBindings.BoatContext;
using PlayerInputManager = Nivalis.PlayerInputManager;

namespace NivalisNights.BoatControlBindings;

internal sealed class BoatHints : IDisposable
{
    private readonly BoatContext owner;
    private readonly RectTransform bottom;
    private readonly HintRow[] rows;
    private readonly Il2CppSystem.Action languageChanged;
    private bool dirty = true;
    private int language = -1;
    private string deviceStamp = "";
    private SecurityAwarenessPanel? securityPanel;
    private float nextSecuritySearch;
    private readonly Dictionary<string, (string Text, Sprite? Sprite)> icons = new();
    internal BoatHints(BoatContext owner)
    {
        this.owner = owner;
        var template = owner.Crosshair.interactionTMP;
        if (!Alive(template)) throw new InvalidOperationException("Missing native crosshair text/font.");
        var hud = owner.Crosshair.GetComponentInParent<HeadsUpDisplayUI>();
        if (!Alive(hud)) throw new InvalidOperationException("Missing native first-person HUD.");
        var go = new GameObject("BoatControlBindings_Bottom"); go.layer = hud!.gameObject.layer;
        // Inherit native HUD visibility and stay below the screen-fade overlay.
        bottom = go.AddComponent<RectTransform>(); bottom.SetParent(hud.transform, false);
        bottom.anchorMin = bottom.anchorMax = new(.5f, 0); bottom.pivot = new(.5f, 0);
        rows = Enumerable.Range(0, 3).Select(i => new HintRow(bottom, "BoatAction_" + i, template)).ToArray();
        languageChanged = (Il2CppSystem.Action)(Action)(() => { dirty = true; icons.Clear(); Hide(); });
        LocObjectsDB.OnLanguageChanged.Add(languageChanged);
        Hide();
    }

    internal void Update(BoatContext context, BoatInput input, bool pending)
    {
        bool busy = pending || context.Busy;
        if (!Alive(bottom) || !Same(context.Canvas, owner.Canvas)) { Hide(); return; }
        var hud = context.Crosshair.GetComponentInParent<HeadsUpDisplayUI>();
        var hudRect = Alive(hud) ? hud!.transform.TryCast<RectTransform>() : null;
        if (!Alive(hudRect) || !hud!.gameObject.activeInHierarchy) { Hide(); return; }
        // Rejoin the native HUD if it was recreated under the same root canvas.
        if (!Same(bottom.parent, hudRect)) bottom.SetParent(hudRect, false);
        int currentLanguage = (int)LocObjectsDB.CurrentLanguage;
        if (language != currentLanguage || dirty)
        {
            language = currentLanguage; dirty = false; icons.Clear();
        }
        var manager = context.Manager;
        bool pad = manager.IsController;
        var devices = manager.user.pairedDevices;
        string stamp = pad + ":" + string.Join(",", Enumerable.Range(0, devices.Count).Select(i => devices[i].deviceId + ":" + devices[i].layout));
        if (deviceStamp != stamp) { deviceStamp = stamp; icons.Clear(); }
        // TMP must be active before measuring text that was previously hidden.
        bottom.gameObject.SetActive(true);
        float total = 0;
        foreach (Operation op in Enum.GetValues<Operation>())
        {
            var row = rows[(int)op];
            var binding = input.Find(op, pad);
            bool visible = binding != null && BoatPolicy.Visible(op, context.Mode, context.CanDock, busy);
            if (visible && context.Mode == BoatMode.Driving)
            {
                var button = op == Operation.Engine ? context.Cockpit.exitButton : op == Operation.Dock ? context.Cockpit.dockButton : context.Cockpit.lightButton;
                visible = ButtonUsable(button);
            }
            string label = visible ? Name(context, op) : "";
            if (string.IsNullOrWhiteSpace(label)) { row.Hide(); continue; }
            var key = Icon(context.Manager, binding!, pad);
            row.Show(key.Text, key.Sprite, label, currentLanguage, pad);
            row.Rect.anchoredPosition = new(total, 0); total += row.Width + 26;
        }
        total = Math.Max(0, total - 26);
        Rect safe = Screen.safeArea;
        float width = hudRect!.rect.width * safe.width / Math.Max(1, Screen.width) - 40;
        float scale = Math.Min(1, Math.Max(1, width) / Math.Max(1, total));
        bottom.sizeDelta = new(total, 34); bottom.localScale = new(scale, scale, 1);
        // Only the native security panel may lift this guide above the safe-area bottom.
        float center = (safe.x + safe.width * .5f) / Math.Max(1, Screen.width);
        bottom.anchorMin = bottom.anchorMax = new(center, safe.y / Math.Max(1, Screen.height));
        float safeBottom = hudRect.rect.height * safe.y / Math.Max(1, Screen.height);
        bottom.anchoredPosition = new(0, SecurityOffset(hudRect, safeBottom));
        bottom.gameObject.SetActive(total > 0);
    }

    private float SecurityOffset(RectTransform hud, float safeBottom)
    {
        if (!Alive(securityPanel) || !securityPanel!.transform.IsChildOf(hud))
        {
            securityPanel = null;
            if (Time.unscaledTime >= nextSecuritySearch)
            {
                nextSecuritySearch = Time.unscaledTime + .5f;
                securityPanel = hud.GetComponentInChildren<SecurityAwarenessPanel>(true);
            }
        }
        if (!Alive(securityPanel) || !securityPanel!.gameObject.activeInHierarchy)
            return BoatHintPlacement.Bottom(false, 0, safeBottom);
        // Test only the panel's own branch. Common HUD fading is inherited by both guides.
        for (var node = securityPanel.transform; Alive(node) && !Same(node, hud); node = node.parent)
        {
            bool ignoreParents = false;
            foreach (var group in node.GetComponents<CanvasGroup>())
            {
                if (!group.enabled) continue;
                if (!(group.alpha > 0)) return BoatHintPlacement.Bottom(false, 0, safeBottom);
                ignoreParents |= group.ignoreParentGroups;
            }
            if (ignoreParents) break;
        }
        var panel = securityPanel.transform.TryCast<RectTransform>();
        if (!Alive(panel)) return BoatHintPlacement.Bottom(false, 0, safeBottom);
        // Use the panel rectangle, not inactive popup text or unrelated HUD rectangles.
        var rect = panel!.rect;
        float top = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            var corner = new Vector3(i % 2 == 0 ? rect.xMin : rect.xMax, i < 2 ? rect.yMin : rect.yMax, 0);
            top = Math.Max(top, hud.InverseTransformPoint(panel.TransformPoint(corner)).y - hud.rect.yMin);
        }
        return BoatHintPlacement.Bottom(true, top, safeBottom);
    }

    private static string Name(BoatContext c, Operation op) => op switch
    {
        Operation.Engine when c.Mode == BoatMode.Ready => c.Cockpit.locObjRef?.Obj?.Text ?? "",
        Operation.Engine => c.Cockpit.exitButton.InteractionName ?? "",
        Operation.Dock => c.Cockpit.dockButton.InteractionName ?? "",
        _ => c.Cockpit.lightButton.InteractionName ?? ""
    };
    private (string Text, Sprite? Sprite) Icon(PlayerInputManager manager, BoatInput.Binding binding, bool pad)
    {
        if (icons.TryGetValue(binding.Path, out var cached)) return cached;
        string name = InputControlPath.ToHumanReadableString(binding.Path, InputControlPath.HumanReadableStringOptions.OmitDevice, null);
        string layout = pad ? "Gamepad" : "Keyboard";
        var devices = manager.user.pairedDevices;
        for (int i = 0; i < devices.Count; i++)
            if (pad ? devices[i].TryCast<Gamepad>() != null : devices[i].TryCast<Keyboard>() != null) { layout = devices[i].layout; break; }
        var data = manager.inputConfigurator.GetDeviceAssetSprite(layout, name, false);
        // Native DisplayActionKeyUI uses the complete key sprite for keyboard too.
        // Keyboard displayInputText=true controls TMP rich-text displays, not Image availability.
        (string Text, Sprite? Sprite) result = data != null && BoatHintGeometry.NativeIcon(pad, data.displayInputText, Alive(data.displayInputIcon), data.sprite) ? ("", data.displayInputIcon)
            : (!string.IsNullOrWhiteSpace(data?.name) && !string.Equals(data.name, "Default", StringComparison.OrdinalIgnoreCase) ? data.name : name, null);
        icons[binding.Path] = result; return result;
    }
    internal void Hide()
    {
        if (Alive(bottom)) bottom.gameObject.SetActive(false);
        foreach (var row in rows) row.Hide();
    }
    public void Dispose()
    {
        LocObjectsDB.OnLanguageChanged.Remove(languageChanged);
        Hide();
        if (Alive(bottom)) Object.Destroy(bottom.gameObject);
    }

    private sealed class HintRow : IDisposable
    {
        internal readonly RectTransform Rect;
        private readonly TextMeshProUGUI key, label;
        private readonly Image icon;
        private string lastText = "";
        internal float Width { get; private set; }
        internal HintRow(Transform parent, string name, TMP_Text template)
        {
            var go = new GameObject(name); go.layer = parent.gameObject.layer;
            Rect = go.AddComponent<RectTransform>(); Rect.SetParent(parent, false);
            Rect.anchorMin = Rect.anchorMax = new(0, 0); Rect.pivot = new(0, 0);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            key = Text(Rect, name + "_Key"); label = Text(Rect, name + "_Name");
            var image = new GameObject(name + "_Icon"); image.layer = go.layer;
            var ir = image.AddComponent<RectTransform>(); ir.SetParent(Rect, false);
            ir.anchorMin = ir.anchorMax = new(0, .5f); ir.pivot = new(0, .5f); ir.sizeDelta = new(30, 30);
            icon = image.AddComponent<Image>(); icon.preserveAspect = true; icon.raycastTarget = false;
            CopyFont(template); Hide();
        }
        private static TextMeshProUGUI Text(Transform parent, string name)
        {
            var go = new GameObject(name); go.layer = parent.gameObject.layer;
            var rt = go.AddComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new(0, .5f); rt.pivot = new(0, .5f);
            var text = go.AddComponent<TextMeshProUGUI>(); text.fontSize = 25; text.alignment = TextAlignmentOptions.Midline;
            text.enableWordWrapping = false; text.richText = false; text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = new(1, .86f, .55f, 1); return text;
        }
        internal void CopyFont(TMP_Text template)
        {
            foreach (var text in new[] { key, label })
            {
                if (!Same(text.font, template.font)) { text.font = template.font; lastText = ""; }
                if (!Same(text.fontSharedMaterial, template.fontSharedMaterial)) { text.fontSharedMaterial = template.fontSharedMaterial; lastText = ""; }
            }
        }
        internal void Show(string keyName, Sprite? sprite, string operation, int currentLanguage, bool pad)
        {
            if (!Alive(Rect)) return;
            Rect.gameObject.SetActive(true); icon.enabled = Alive(sprite); icon.sprite = sprite;
            icon.type = Image.Type.Simple;
            // Match the native HUD KeyBind's 40x40 Image; retain the existing 30x30 pad guide.
            icon.preserveAspect = pad;
            key.enabled = !Alive(sprite);
            string signature = currentLanguage + ":" + keyName + ":" + operation + ":" + (Alive(sprite) ? sprite!.Pointer.ToInt64() : 0);
            if (signature != lastText)
            {
                // Native text assignment also selects the current language font/material.
                // Do this on our own text: the crosshair may never have displayed a localized name.
                ComponentExtensions.SetTextLocalized(key, keyName);
                ComponentExtensions.SetTextLocalized(label, operation);
                // Give initial mesh generation a real rectangle rather than a zero-width inactive label.
                key.rectTransform.sizeDelta = label.rectTransform.sizeDelta = new(2048, 34);
                if (key.enabled) key.ForceMeshUpdate(true, true);
                label.ForceMeshUpdate(true, true);
                lastText = signature;
            }
            float iconSize = pad ? 30 : 40;
            float kw = Alive(sprite) ? iconSize : Math.Max(20, BoatHintGeometry.TextWidth(key.GetPreferredValues(keyName).x, key.fontSize, keyName.Length));
            float lw = operation.Length == 0 ? 0 : BoatHintGeometry.TextWidth(label.GetPreferredValues(operation).x, label.fontSize, operation.Length);
            icon.rectTransform.sizeDelta = new(iconSize, iconSize);
            key.rectTransform.anchoredPosition = Vector2.zero; key.rectTransform.sizeDelta = new(kw, 34);
            label.rectTransform.anchoredPosition = new(kw + 8, 0); label.rectTransform.sizeDelta = new(lw, 34);
            Width = kw + (lw > 0 ? lw + 8 : 0); Rect.sizeDelta = new(Width, 34);
        }
        internal void Hide() { if (Alive(Rect)) Rect.gameObject.SetActive(false); }
        public void Dispose() { if (Alive(Rect)) Object.Destroy(Rect.gameObject); }
    }
}
