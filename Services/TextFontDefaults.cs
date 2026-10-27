using System.Windows.Markup;
using System.Windows.Media;

namespace KillerPDF.Services
{
    // Fork (jensen0915/KillerPDF): the typeface new text starts in, and how the font list names it.
    //
    // Taiwan forms are set in 新細明體 (PMingLiU) - also Word's Traditional Chinese default for
    // years - so Traditional Chinese starts there. Segoe UI drew the Chinese part in the sans
    // 微軟正黑體, which looks out of place on a printed form.
    //
    // The choice is remembered under its own key the moment it is made in the font list. The
    // original "TextFontName" setting was written at exit from whatever box was edited last, so
    // double-clicking a PDF's own text silently changed the font of every later box.
    internal static class TextFontDefaults
    {
        internal const string SettingKey = "TextDefaultFont";

        // Ming / Song faces first: they carry their own serif Latin, so a mixed line still reads as
        // one typeface. JhengHei covers machines without the Traditional Chinese font feature.
        private static readonly string[] TraditionalChoices = ["PMingLiU", "MingLiU", "Microsoft JhengHei"];

        // Pinned to the top of the font list for Traditional Chinese, in this order, when installed.
        private static readonly string[] TraditionalPinned =
            ["PMingLiU", "DFKai-SB", "Microsoft JhengHei", "MingLiU", "Times New Roman"];

        private static HashSet<string>? _installed;

        internal static string For(bool traditionalChinese, Func<string, bool> installed)
        {
            if (traditionalChinese)
                foreach (string family in TraditionalChoices)
                    if (installed(family)) return family;
            return "Segoe UI";
        }

        /// <summary>The saved choice when it is still installed, otherwise the default for the
        /// interface language.</summary>
        internal static string Initial(string? saved, bool traditionalChinese, Func<string, bool> installed)
            => !string.IsNullOrWhiteSpace(saved) && installed(saved!) ? saved! : For(traditionalChinese, installed);

        internal static bool IsInstalled(string family)
        {
            _installed ??= new HashSet<string>(
                Fonts.SystemFontFamilies.Select(f => f.Source).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);
            return _installed.Contains(family);
        }

        /// <summary>One entry of the text bar's font list: the family name the annotation stores and
        /// the name shown, which is the family's own name in the interface language when it has one
        /// (新細明體 rather than PMingLiU).</summary>
        internal sealed record Choice(string Family, string Label)
        {
            public override string ToString() => Label;
        }

        internal static List<Choice> Choices(IEnumerable<string> families, string? languageTag, bool pinTraditional)
        {
            XmlLanguage? language = null;
            if (!string.IsNullOrEmpty(languageTag))
                try { language = XmlLanguage.GetLanguage(languageTag); } catch { language = null; }
            var all = families.Select(f => new Choice(f, Label(f, language))).ToList();
            var pinned = new List<Choice>();
            if (pinTraditional)
                foreach (string family in TraditionalPinned)
                    if (all.FirstOrDefault(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase)) is { } hit)
                    {
                        pinned.Add(hit);
                        all.Remove(hit);
                    }
            all.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            return [.. pinned, .. all];
        }

        private static string Label(string family, XmlLanguage? language)
        {
            if (language is null) return family;
            try
            {
                if (new FontFamily(family).FamilyNames.TryGetValue(language, out string? name)
                    && !string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { /* an unreadable family keeps its own name */ }
            return family;
        }
    }
}
