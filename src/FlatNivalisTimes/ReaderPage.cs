using Nivalis;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NivalisNights.FlatNivalisTimes;

// All style changes are on this visual-only copy. Original canvas, camera, RT and fonts stay untouched.
internal sealed class ReaderPage : IDisposable
{
    internal readonly Newspaper Paper;
    private GameObject? page;
    private Camera? camera;
    private Canvas? canvas;
    private RawImage? background, underlay;
    private readonly List<(TMP_Text Text, Color Color)> texts = new();
    private readonly List<(Image Image, Color Color, bool ForecastRow)> panels = new();
    private readonly List<(RectTransform Rect, Vector2 Size)> fixedRects = new();
    private readonly List<(TMP_Text Text, float Size)> fixedText = new();
    private Material? material, originalMaterial, originalBackgroundMaterial;
    private MeshRenderer? rendererOwner;
    private RenderTexture? texture;
    private Texture? originalBackground;
    private Color backgroundColor;
    private PaperBackground lastBackground = (PaperBackground)(-1);
    private bool lastContrast;
    private int width, height;
    private float retryAt;
    internal bool Dirty = true;
    internal Action<Texture>? TextureChanged;
    internal Texture? Texture => texture;
    internal TMP_Text? TextTemplate => Paper.newsScrenUI.dailyNewsUi.contentText;

    internal ReaderPage(Newspaper paper) => Paper = paper;

    internal void Draw(Camera gameCamera)
    {
        if (PaperStates.Alive(material)) EnsureMaterial();
        var target = ReaderGeometry.Target(gameCamera.scaledPixelWidth, gameCamera.scaledPixelHeight, Math.Min(4096, SystemInfo.maxTextureSize));
        if (target.Width == 0) return; // Minimized/invalid size: retain the last valid texture.
        bool resized = target.Width != width || target.Height != height;
        if (resized && Time.unscaledTime < retryAt) return;
        if (Dirty || !PaperStates.Alive(page)) RebuildVisual();
        bool restyle = lastBackground != ReaderSettings.Background || lastContrast != ReaderSettings.Contrast;
        if (!Dirty && !resized && !restyle) return;
        if (!PaperStates.Alive(Paper.canvasCamera) || !PaperStates.Alive(Paper.canvasCamera.targetTexture))
            throw new InvalidOperationException("Original newspaper render target is unavailable.");
        RenderTexture? candidate = null;
        var old = texture;
        try
        {
            if (resized || !PaperStates.Alive(texture))
            {
                var descriptor = Paper.canvasCamera.targetTexture.descriptor;
                descriptor.width = target.Width; descriptor.height = target.Height;
                descriptor.useDynamicScale = false; descriptor.useMipMap = false; descriptor.autoGenerateMips = false;
                candidate = new RenderTexture(descriptor) { name = "FNT.ReaderTexture", filterMode = FilterMode.Bilinear };
                if (!candidate.Create()) throw new InvalidOperationException("Newspaper render texture allocation failed.");
            }
            Style();
            camera!.CopyFrom(Paper.canvasCamera);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(Paper.canvasCamera.transform.position, Paper.canvasCamera.transform.rotation);
            camera.targetTexture = candidate ?? texture;
            canvas!.worldCamera = camera;
            canvas.scaleFactor = target.Width / 1920f;
            page!.SetActive(true);
            Canvas.ForceUpdateCanvases();
            ValidateLayout();
            foreach (var (text, _) in texts) text.ForceMeshUpdate(true, false);
            camera.Render();
            ValidateLayout();
            if (candidate != null)
            {
                EnsureMaterial();
                material!.mainTexture = candidate;
                texture = candidate; candidate = null;
                TextureChanged?.Invoke(texture);
                width = target.Width; height = target.Height;
                if (PaperStates.Alive(old)) { old!.Release(); Object.Destroy(old); }
            }
            lastBackground = ReaderSettings.Background; lastContrast = ReaderSettings.Contrast; Dirty = false;
        }
        catch (Exception error) when (resized && PaperStates.Alive(old) && texture?.Pointer == old!.Pointer)
        {
            // A new target could not be rendered: retain the last complete page and retry with backoff.
            if (PaperStates.Alive(material)) material!.mainTexture = old;
            retryAt = Time.unscaledTime + 2;
            Plugin.ReportOnce("reader resize retained previous page", error);
        }
        finally
        {
            if (PaperStates.Alive(page)) page!.SetActive(false);
            if (PaperStates.Alive(candidate)) { camera!.targetTexture = texture; candidate!.Release(); Object.Destroy(candidate); }
        }
    }

    private void EnsureMaterial()
    {
        var renderer = Paper.meshRenderer;
        if (rendererOwner != null && (!PaperStates.Alive(rendererOwner) || !PaperStates.Alive(renderer)
            || renderer.Pointer != rendererOwner.Pointer))
            throw new InvalidOperationException("Newspaper renderer owner changed; reader must retain its original lease.");
        if (!PaperStates.Alive(material))
        {
            rendererOwner = renderer;
            originalMaterial = renderer.sharedMaterial;
            if (!PaperStates.Alive(originalMaterial)) throw new InvalidOperationException("Paper material is missing.");
            material = Object.Instantiate(originalMaterial!); material.name = "FNT.ReaderMaterial";
            renderer.material = material;
            // Original UpdateAnimation/UpdateRain read Renderer.material. Complete any
            // Unity-side instancing now and track the actual per-renderer instance.
            var installed = renderer.material;
            if (!PaperStates.Alive(installed)) throw new InvalidOperationException("Paper material instance could not be acquired.");
            if (installed.Pointer != material.Pointer) { Object.Destroy(material); material = installed; }
        }
        else if (renderer.sharedMaterial.Pointer != material!.Pointer)
            throw new InvalidOperationException("Another owner replaced the newspaper material.");
    }

    private void RebuildVisual()
    {
        if (!PaperStates.Alive(Paper.canvas) || !PaperStates.Alive(Paper.UIContainer)) throw new InvalidOperationException("Original newspaper UI is missing.");
        var sourceRoot = Paper.canvas.transform;
        if (Paper.UIContainer.parent.Pointer != sourceRoot.Pointer || sourceRoot.Find("Background")?.parent.Pointer != sourceRoot.Pointer)
            throw new InvalidOperationException("Newspaper canvas hierarchy differs from the investigated original.");
        var staging = new GameObject("FNT.VisualStaging"); staging.SetActive(false);
        GameObject? copy = null;
        try
        {
            copy = Object.Instantiate(Paper.canvas.gameObject, staging.transform, false);
            copy.SetActive(false); copy.name = "FNT.ReaderPage";
            var sourceRects = Paper.canvas.GetComponentsInChildren<RectTransform>(true);
            var copyRects = copy.GetComponentsInChildren<RectTransform>(true);
            var sourceTexts = Paper.canvas.GetComponentsInChildren<TMP_Text>(true);
            var copyTexts = copy.GetComponentsInChildren<TMP_Text>(true);
            if (sourceRects.Length != copyRects.Length || sourceTexts.Length != copyTexts.Length)
                throw new InvalidOperationException("Paper visual snapshot hierarchy changed.");
            // Retain visual clipping, but never rerun fitters/layout on the snapshot.
            // Those drivers can overwrite the native Display() layout after cloning.
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour.TryCast<Graphic>() == null && behaviour.TryCast<Mask>() == null && behaviour.TryCast<RectMask2D>() == null)
                    Object.DestroyImmediate(behaviour);
            fixedRects.Clear(); fixedText.Clear();
            for (int i = 1; i < copyRects.Length; i++)
            {
                var from = sourceRects[i]; var to = copyRects[i];
                to.anchorMin = from.anchorMin; to.anchorMax = from.anchorMax; to.pivot = from.pivot;
                to.anchoredPosition3D = from.anchoredPosition3D; to.sizeDelta = from.sizeDelta;
                to.localRotation = from.localRotation; to.localScale = from.localScale;
                fixedRects.Add((to, from.rect.size));
            }
            for (int i = 0; i < copyTexts.Length; i++)
            {
                // Freeze the original's resolved size; do not introduce a reader font-size setting.
                copyTexts[i].enableAutoSizing = false;
                copyTexts[i].fontSize = sourceTexts[i].fontSize;
                fixedText.Add((copyTexts[i], sourceTexts[i].fontSize));
            }
            foreach (var group in copy.GetComponentsInChildren<CanvasGroup>(true)) { group.alpha = 1; group.interactable = false; group.blocksRaycasts = false; }
            var newCanvas = copy.GetComponent<Canvas>();
            var newBackground = copy.transform.Find("Background")?.GetComponent<RawImage>();
            if (!PaperStates.Alive(newCanvas) || !PaperStates.Alive(newBackground)) throw new InvalidOperationException("Original paper visual structure is missing.");
            newCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            newCanvas.enabled = true;
            var baseObject = new GameObject("FNT.PlainPaper"); baseObject.layer = newBackground!.gameObject.layer;
            var rect = baseObject.AddComponent<RectTransform>(); rect.SetParent(newBackground.transform.parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(newBackground.transform.GetSiblingIndex());
            var newUnderlay = baseObject.AddComponent<RawImage>(); newUnderlay.raycastTarget = false; newUnderlay.color = PaperColor;
            if (!PaperStates.Alive(camera))
            {
                var cameraObject = new GameObject("FNT.ReaderCamera");
                cameraObject.transform.SetParent(ReaderRunner.Current!.transform, false);
                camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            }
            copy.transform.SetParent(camera!.transform, false);
            copy.transform.localScale = Vector3.one;
            if (PaperStates.Alive(page)) Object.Destroy(page);
            page = copy; copy = null; canvas = newCanvas; background = newBackground; underlay = newUnderlay;
            originalBackground = background.texture; backgroundColor = background.color;
            originalBackgroundMaterial = background.material;
            texts.Clear(); panels.Clear();
            foreach (var text in page.GetComponentsInChildren<TMP_Text>(true)) { texts.Add((text, text.color)); text.raycastTarget = false; }
            var articleLayout = page.transform.Find("Container/Layout");
            if (!PaperStates.Alive(articleLayout)) throw new InvalidOperationException("Original article panel layout is missing.");
            foreach (var image in page.GetComponentsInChildren<Image>(true))
                image.raycastTarget = false;
            // Capture only the three native columns and seven direct forecast
            // rows. Photos, transparent masks and header rules retain their colors.
            CapturePanel(articleLayout.Find("LeftPanel"), false);
            CapturePanel(articleLayout.Find("MiddlePanel"), false);
            var weather = articleLayout.Find("WeatherWrapper");
            CapturePanel(weather, false);
            for (int day = 1; day <= 7; day++) CapturePanel(weather.Find("DayForecast-" + day), true);
        }
        finally { if (PaperStates.Alive(copy)) Object.Destroy(copy); Object.Destroy(staging); }
    }

    private void CapturePanel(Transform? owner, bool forecastRow)
    {
        if (!PaperStates.Alive(owner)) throw new InvalidOperationException("Original newspaper panel is missing.");
        var image = owner!.GetComponent<Image>();
        if (!PaperStates.Alive(image) || PaperStates.Alive(image!.sprite) || image.color.a < .05f)
            throw new InvalidOperationException("Newspaper panel differs from the investigated untextured background.");
        panels.Add((image, image.color, forecastRow));
    }

    private void Style()
    {
        bool original = ReaderSettings.Background == PaperBackground.Original;
        underlay!.enabled = !original;
        background!.texture = originalBackground;
        background.enabled = ReaderSettings.Background != PaperBackground.Plain;
        // Default UI blending on the owned background makes the faded alpha explicit.
        background.material = original ? originalBackgroundMaterial : null;
        background.color = new(backgroundColor.r, backgroundColor.g, backgroundColor.b, original ? backgroundColor.a : ReaderPalette.PatternOpacity * backgroundColor.a);
        foreach (var (text, color) in texts) text.color = ReaderSettings.Contrast ? new(.035f, .030f, .025f, 1) : color;
        foreach (var (image, color, forecastRow) in panels)
        {
            var fill = ReaderPalette.Panel(forecastRow);
            image.color = ReaderSettings.Contrast ? new(fill.R, fill.G, fill.B, fill.A) : color;
        }
    }

    private static Color PaperColor => new(ReaderPalette.Paper.R, ReaderPalette.Paper.G, ReaderPalette.Paper.B, ReaderPalette.Paper.A);
    private void ValidateLayout()
    {
        foreach (var (rect, size) in fixedRects)
            if ((rect.rect.size - size).sqrMagnitude > .25f)
                throw new InvalidOperationException($"Reader snapshot rectangle {rect.name}: expected {size}, got {rect.rect.size}.");
        foreach (var (text, size) in fixedText)
            if (Mathf.Abs(text.fontSize - size) > .01f)
                throw new InvalidOperationException($"Reader snapshot font {text.name}: expected {size}, got {text.fontSize}.");
    }

    public void Dispose()
    {
        if (PaperStates.Alive(rendererOwner) && PaperStates.Alive(material))
        {
            var current = rendererOwner!.sharedMaterial;
            if (PaperStates.Alive(current) && current.Pointer == material!.Pointer) rendererOwner.sharedMaterial = originalMaterial;
            else if (PaperStates.Alive(current) && current.mainTexture?.Pointer == texture?.Pointer)
                throw new InvalidOperationException("An external material still uses the owned newspaper texture; resources retained.");
        }
        if (PaperStates.Alive(camera)) { camera!.targetTexture = null; Object.Destroy(camera.gameObject); }
        if (PaperStates.Alive(page)) Object.Destroy(page);
        if (PaperStates.Alive(material)) Object.Destroy(material);
        if (PaperStates.Alive(texture)) { texture!.Release(); Object.Destroy(texture); }
        camera = null; page = null; material = null; texture = null;
        rendererOwner = null;
    }
}
