using Nivalis;
using Nivalis.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using PlayerInputManager = Nivalis.PlayerInputManager;

namespace NivalisNights.FlatNivalisTimes;

internal sealed class ReaderHud : IDisposable
{
    private GameObject owner = null!;
    private Canvas canvas = null!;
    private RawImage backdrop = null!, paper = null!;
    private RectTransform bar = null!;
    private Sprite keyFrame = null!;
    private Texture2D keyTexture = null!;
    private readonly List<Row> rows = new();
    private readonly HashSet<string> reportedIcons = new();
    private readonly record struct Row(ReaderCommand Command, RectTransform Rect, Image Icon, TextMeshProUGUI Key, TextMeshProUGUI Label);
    private string stamp = "";

    internal void Initialize()
    {
        if (!ReferenceEquals(owner, null))
        {
            if (PaperStates.Alive(owner)) return;
            throw new InvalidOperationException("Reading HUD owner was destroyed; release its remaining assets before recreating.");
        }
        owner = new GameObject("FNT.ReadingControls"); owner.SetActive(false);
        owner.transform.SetParent(ReaderRunner.Current!.transform, false);
        canvas = owner.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        backdrop = Image(owner.transform, "Backdrop"); backdrop.color = new(.025f, .025f, .025f, 1);
        backdrop.rectTransform.anchorMin = Vector2.zero; backdrop.rectTransform.anchorMax = Vector2.one;
        backdrop.rectTransform.offsetMin = backdrop.rectTransform.offsetMax = Vector2.zero;
        paper = Image(owner.transform, "Paper");
        var strip = Image(owner.transform, "Controls"); strip.color = new(0, 0, 0, .88f); bar = strip.rectTransform;
        keyTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "FNT.NotchedKeyFrame", filterMode = FilterMode.Bilinear };
        var pixels = new Color32[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            bool inside = x >= 3 && y >= 3 && x <= 60 && y <= 60 && x + y <= 108;
            bool edge = x < 6 || y < 6 || x > 57 || y > 57 || x + y > 104;
            pixels[y * 64 + x] = !inside ? new Color32(0, 0, 0, 0) : edge ? new Color32(239, 204, 142, 255) : new Color32(24, 24, 24, 255);
        }
        keyTexture.SetPixels32(pixels); keyTexture.Apply();
        keyFrame = Sprite.Create(keyTexture, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f));
        foreach (var command in new[] { ReaderCommand.View, ReaderCommand.Background, ReaderCommand.Contrast })
        {
            var rect = Rect(bar, command.ToString());
            var iconObject = new GameObject("Icon"); var iconRect = iconObject.AddComponent<RectTransform>(); iconRect.SetParent(rect, false);
            Place(iconRect, 10, 14, 44, 44);
            var icon = iconObject.AddComponent<Image>(); icon.preserveAspect = true; icon.raycastTarget = false;
            var key = Text(rect, "Key"); Place(key.rectTransform, 10, 14, 44, 44); key.alignment = TextAlignmentOptions.Center;
            var label = Text(rect, "Label"); label.alignment = TextAlignmentOptions.MidlineLeft;
            rows.Add(new(command, rect, icon, key, label));
        }
    }

    private static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name); var rt = go.AddComponent<RectTransform>(); rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero; return rt;
    }
    private static RawImage Image(Transform parent, string name)
    {
        var image = Rect(parent, name).gameObject.AddComponent<RawImage>(); image.raycastTarget = false; return image;
    }
    private static TextMeshProUGUI Text(Transform parent, string name)
    {
        var t = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>(); t.color = new(1, .9f, .69f, 1);
        t.fontSize = 25; t.enableAutoSizing = true; t.fontSizeMin = 17; t.fontSizeMax = 25;
        t.richText = false; t.enableWordWrapping = true; t.raycastTarget = false; return t;
    }
    private static void Place(RectTransform rt, float x, float y, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = new(x, y); rt.sizeDelta = new(width, height);
    }

    internal void SetTexture(Texture value) => paper.texture = value;
    internal void Show(ReaderPage source, ReaderInput input, PlayerInputManager manager)
    {
        float scale = Mathf.Max(.1f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f)); canvas.scaleFactor = scale;
        var safe = Screen.safeArea;
        float left = safe.x / scale, bottom = safe.y / scale, width = safe.width / scale, height = safe.height / scale;
        Place(bar, left, bottom, width, 72);
        backdrop.gameObject.SetActive(ReaderSettings.Expanded); paper.gameObject.SetActive(ReaderSettings.Expanded);
        paper.texture = source.Texture;
        float pageHeight = Mathf.Max(1, Mathf.Min(height - 100, (width - 32) * 9 / 16));
        float pageWidth = pageHeight * 16 / 9;
        Place(paper.rectTransform, left + (width - pageWidth) / 2, bottom + 84 + (height - 84 - pageHeight) / 2, pageWidth, pageHeight);
        string now = (int)LocObjectsDB.CurrentLanguage + ":" + manager.IsController + ":" + ReaderSettings.Expanded + ":" + ReaderSettings.Background + ":" + ReaderSettings.Contrast
            + ":" + string.Join("|", input.Bindings.Select(b => b.Control.Pointer.ToString()));
        var ordered = manager.IsController
            ? rows.OrderBy(row => ReaderHintOrder.Pad(input.Bindings.FirstOrDefault(b => b.Command == row.Command && b.Pad)?.Control.name)).ToArray()
            : rows.ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            var row = ordered[i]; var binding = input.Bindings.FirstOrDefault(b => b.Command == row.Command && b.Pad == manager.IsController);
            row.Rect.gameObject.SetActive(binding != null);
            Place(row.Rect, i * width / 3, 0, width / 3, 72);
            Place(row.Label.rectTransform, 66, 6, width / 3 - 80, 60);
            if (binding == null || now == stamp) continue;
            Sprite? sprite = null;
            string key = InputControlPath.ToHumanReadableString((binding.Pad ? "<Gamepad>/" : "<Keyboard>/") + binding.Control.name,
                InputControlPath.HumanReadableStringOptions.OmitDevice, null);
            var display = manager.inputConfigurator.GetDeviceAssetSprite(binding.Control.device.layout, key, false);
            // Keyboard displayInputText selects the TMP representation; it does not
            // mean the complete native key image is absent. Preserve the pad policy.
            if (display != null && (!binding.Pad || !display.displayInputText)
                && !string.IsNullOrWhiteSpace(display.sprite)
                && !string.Equals(display.sprite, "Default", StringComparison.OrdinalIgnoreCase)
                && PaperStates.Alive(display.displayInputIcon)) sprite = display.displayInputIcon;
            row.Icon.sprite = PaperStates.Alive(sprite) ? sprite : keyFrame;
            row.Icon.preserveAspect = binding.Pad || !PaperStates.Alive(sprite);
            row.Key.enabled = !PaperStates.Alive(sprite);
            string result = binding.Control.device.layout + ":" + key + ":" + (PaperStates.Alive(sprite) ? sprite!.name : "text fallback");
            if (reportedIcons.Add(result)) Plugin.Logger.LogInfo($"Reader hint image: {result}.");
            var template = source.TextTemplate;
            foreach (var t in new[] { row.Key, row.Label })
                if (PaperStates.Alive(template)) { t.font = template!.font; t.fontSharedMaterial = template.fontSharedMaterial; }
            ComponentExtensions.SetTextLocalized(row.Key, key);
            ComponentExtensions.SetTextLocalized(row.Label, ReaderTexts.Label(row.Command));
        }
        stamp = now; owner.SetActive(true);
    }

    internal void Hide() { if (PaperStates.Alive(owner)) owner.SetActive(false); if (PaperStates.Alive(paper)) paper.texture = null; stamp = ""; }
    public void Dispose()
    {
        var cleanup = new CleanupBatch();
        cleanup.Attempt(() => { if (PaperStates.Alive(owner)) Object.Destroy(owner); owner = null!; });
        cleanup.Attempt(() => { if (PaperStates.Alive(keyFrame)) Object.Destroy(keyFrame); keyFrame = null!; });
        cleanup.Attempt(() => { if (PaperStates.Alive(keyTexture)) Object.Destroy(keyTexture); keyTexture = null!; });
        cleanup.ThrowIfFailed();
    }
}
