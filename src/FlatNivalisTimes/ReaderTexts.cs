using Nivalis.Localization;

namespace NivalisNights.FlatNivalisTimes;

internal static class ReaderTexts
{
    internal sealed record Entry(string Expand, string Hands, string Background, string Original, string Faded, string Plain, string Contrast, string On, string Off);
    internal static readonly Dictionary<Language, Entry> All = new()
    {
        [Language.English] = new("Read fullscreen", "Return to hands", "Background", "Original", "Faded", "Plain", "Contrast", "On", "Off"),
        [Language.German] = new("Im Vollbild lesen", "Zurück zur Handansicht", "Hintergrund", "Original", "Blass", "Einfarbig", "Kontrast", "Ein", "Aus"),
        [Language.French] = new("Lire en plein écran", "Revenir en main", "Fond", "Original", "Atténué", "Uni", "Contraste", "Oui", "Non"),
        [Language.Korean] = new("전체 화면으로 읽기", "손에 든 화면으로", "배경", "원본", "연하게", "단색", "대비", "켜기", "끄기"),
        [Language.Japanese] = new("画面を広げて読む", "手持ちに戻る", "背景", "原作", "薄い", "単色", "コントラスト", "オン", "オフ"),
        [Language.SChinese] = new("全屏阅读", "返回手持", "背景", "原版", "淡化", "纯色", "对比度", "开", "关"),
        [Language.TChinese] = new("全螢幕閱讀", "返回手持", "背景", "原版", "淡化", "純色", "對比度", "開", "關"),
        [Language.Spanish] = new("Leer a pantalla completa", "Volver a las manos", "Fondo", "Original", "Tenue", "Liso", "Contraste", "Sí", "No"),
        [Language.Portuguese] = new("Ler em tela cheia", "Voltar às mãos", "Fundo", "Original", "Suave", "Liso", "Contraste", "Sim", "Não"),
        [Language.Russian] = new("Читать на весь экран", "Вернуть в руки", "Фон", "Исходный", "Бледный", "Однотонный", "Контраст", "Вкл.", "Выкл."),
        [Language.Turkish] = new("Tam ekranda oku", "Elde tutmaya dön", "Arka plan", "Orijinal", "Soluk", "Düz", "Kontrast", "Açık", "Kapalı"),
    };
    internal static Entry Current => All.TryGetValue(LocObjectsDB.CurrentLanguage, out var value) ? value : All[Language.English];
    internal static string Label(ReaderCommand command)
    {
        var t = Current;
        return command switch
        {
            ReaderCommand.View => ReaderSettings.Expanded ? t.Hands : t.Expand,
            ReaderCommand.Background => t.Background + ": " + (ReaderSettings.Background switch { PaperBackground.Faded => t.Faded, PaperBackground.Plain => t.Plain, _ => t.Original }),
            _ => t.Contrast + ": " + (ReaderSettings.Contrast ? t.On : t.Off)
        };
    }
}
