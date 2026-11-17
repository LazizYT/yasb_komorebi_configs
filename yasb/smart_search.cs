using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YasbSearch
{
    public struct SearchMatch
    {
        public int Score;
        public string InterpretedAs;
        public string MatchKind;
    }

    public sealed class IndexedText
    {
        public string RawLower;
        public string AlnumSpaced;
        public string Joined;
        public string[] Tokens;
        public string[] PhoneticTokens;
        public string PhoneticJoined;
        public string Acronym;
    }

    public static class SmartSearchMatcher
    {
        private static readonly Dictionary<char, char> RuToEnMap = new Dictionary<char, char>
        {
            {'й','q'}, {'ц','w'}, {'у','e'}, {'к','r'}, {'е','t'}, {'н','y'}, {'г','u'}, {'ш','i'}, {'щ','o'}, {'з','p'}, {'х','['}, {'ъ',']'},
            {'ф','a'}, {'ы','s'}, {'в','d'}, {'а','f'}, {'п','g'}, {'р','h'}, {'о','j'}, {'л','k'}, {'д','l'}, {'ж',';'}, {'э','\''},
            {'я','z'}, {'ч','x'}, {'с','c'}, {'м','v'}, {'и','b'}, {'т','n'}, {'ь','m'}, {'б',','}, {'ю','.'}, {'ё','`'},
            {'Й','Q'}, {'Ц','W'}, {'У','E'}, {'К','R'}, {'Е','T'}, {'Н','Y'}, {'Г','U'}, {'Ш','I'}, {'Щ','O'}, {'З','P'}, {'Х','{'}, {'Ъ','}'},
            {'Ф','A'}, {'Ы','S'}, {'В','D'}, {'А','F'}, {'П','G'}, {'Р','H'}, {'О','J'}, {'Л','K'}, {'Д','L'}, {'Ж',':'}, {'Э','"'},
            {'Я','Z'}, {'Ч','X'}, {'С','C'}, {'М','V'}, {'И','B'}, {'Т','N'}, {'Ь','M'}, {'Б','<'}, {'Ю','>'}, {'Ё','~'}
        };

        private static readonly Dictionary<char, char> EnToRuMap = BuildEnToRuMap();

        private static readonly Dictionary<string, string[]> SynonymGroups = BuildSynonymGroups();

        private static Dictionary<char, char> BuildEnToRuMap()
        {
            var map = new Dictionary<char, char>();
            foreach (var kv in RuToEnMap)
            {
                if (!map.ContainsKey(kv.Value))
                    map[kv.Value] = kv.Key;
            }
            return map;
        }

        private static Dictionary<string, string[]> BuildSynonymGroups()
        {
            string[][] groups = new[]
            {
                new[] { "chrome", "google chrome", "хром", "гугл", "браузер" },
                new[] { "edge", "msedge", "microsoft edge", "эдж", "едж", "браузер" },
                new[] { "firefox", "фаерфокс", "лиса" },
                new[] { "vscode", "vs code", "visual studio code", "code", "вскод", "вс код", "код", "редактор" },
                new[] { "notepad", "блокнот", "нотепад", "заметки" },
                new[] { "calculator", "calc", "калькулятор", "кальк" },
                new[] { "explorer", "file explorer", "проводник", "файлы", "папки", "finder" },
                new[] { "settings", "ms-settings", "настройки", "параметры", "конфиг" },
                new[] { "control panel", "панель управления" },
                new[] { "task manager", "taskmgr", "диспетчер задач", "процессы" },
                new[] { "terminal", "windows terminal", "wt", "терминал", "консоль" },
                new[] { "powershell", "pwsh", "пауэршелл", "пш", "шелл" },
                new[] { "cmd", "command prompt", "командная строка", "кмд", "цмд" },
                new[] { "snipping tool", "ножницы", "скриншот", "снимок" },
                new[] { "camera", "камера" },
                new[] { "clock", "alarms", "часы", "будильник", "таймер" },
                new[] { "weather", "погода" },
                new[] { "paint", "mspaint", "паинт", "рисование" },
                new[] { "telegram", "телеграм", "телеграмм", "телега", "тг" },
                new[] { "discord", "дискорд", "дс" },
                new[] { "steam", "стим" },
                new[] { "counter-strike", "cs2", "cs", "кс", "кс2", "контра" },
                new[] { "spotify", "спотифай", "спотик", "музыка" },
                new[] { "aimp", "аимп", "музыка", "плеер" },
                new[] { "vlc", "влс", "видеоплеер", "плеер" },
                new[] { "winrar", "винрар", "архиватор", "рар" },
                new[] { "7-zip", "7zip", "7z", "архиватор", "зип" },
                new[] { "utorrent", "µtorrent", "torrent", "торрент", "мюторрент" },
                new[] { "blender", "блендер" },
                new[] { "photoshop", "фотошоп", "фш" },
                new[] { "figma", "фигма" },
                new[] { "obs", "obs studio", "обс" },
                new[] { "davinci", "davinci resolve", "давинчи", "резолв" },
                new[] { "python", "питон", "пайтон" },
                new[] { "git", "гит" },
                new[] { "github", "гитхаб" },
                new[] { "zoom", "зум" },
                new[] { "word", "ворд" },
                new[] { "excel", "эксель" },
                new[] { "antigravity", "антигравити" },
                new[] { "wallpaper", "wallpapers", "обои", "фон", "карусель" },
                new[] { "downloads", "загрузки", "скачанное" },
                new[] { "desktop", "рабочий стол" },
                new[] { "documents", "документы", "доки" },
                new[] { "pictures", "photos", "изображения", "картинки", "фото", "фотки" },
                new[] { "videos", "видео" },
                new[] { "music", "музыка" },
                new[] { "recycle bin", "корзина" },
                new[] { "sleep", "сон", "спящий режим" },
                new[] { "shutdown", "выключение", "выключить", "выкл" },
                new[] { "restart", "перезагрузка", "перезагрузить", "рестарт" },
                new[] { "lock", "блокировка", "заблокировать" },
                new[] { "sound", "volume", "звук", "громкость", "аудио" },
                new[] { "display", "экран", "дисплей", "монитор", "яркость" },
                new[] { "network", "wifi", "wi-fi", "сеть", "вайфай", "интернет" },
                new[] { "bluetooth", "блютуз", "блютус" },
                new[] { "windows update", "update", "обновления", "обновление" },
                new[] { "windhawk", "виндхавк" },
                new[] { "yasb", "ясб" },
                new[] { "hotkeys", "cheatsheet", "клавиши", "шпаргалка", "хоткеи" },
                new[] { "scratchpad", "блокнот", "скретчпад", "калькулятор" },
                new[] { "visual", "вижуал" },
                new[] { "studio", "студио" }
            };

            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string[] g in groups)
            {
                for (int i = 0; i < g.Length; i++)
                {
                    string key = NormalizeBasic(g[i]);
                    HashSet<string> set;
                    if (!map.TryGetValue(key, out set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        map[key] = set;
                    }
                    for (int j = 0; j < g.Length; j++)
                    {
                        if (i == j) continue;
                        set.Add(NormalizeBasic(g[j]));
                    }
                }
            }

            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in map)
                result[kv.Key] = kv.Value.ToArray();
            return result;
        }

        public static bool ContainsCyrillic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u0400' && c <= '\u04FF') || (c >= '\u0500' && c <= '\u052F'))
                    return true;
            }
            return false;
        }

        public static bool ContainsLatin(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    return true;
            }
            return false;
        }

        public static string LayoutSwapRuToEn(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char mapped;
                sb.Append(RuToEnMap.TryGetValue(c, out mapped) ? mapped : c);
            }
            return sb.ToString();
        }

        public static string LayoutSwapEnToRu(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char mapped;
                sb.Append(EnToRuMap.TryGetValue(c, out mapped) ? mapped : c);
            }
            return sb.ToString();
        }

        public static string NormalizeBasic(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if (c == '\u00B5' || c == '\u03BC') c = 'u'; // µ -> u (µTorrent)
                else if (c == 'ё') c = 'е';

                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    sb.Append(' ');
                }
            }
            return sb.ToString().Trim();
        }

        public static string TransliterateRuToEn(string text, bool softW)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length * 2);
            for (int i = 0; i < text.Length; i++)
            {
                char raw = char.ToLowerInvariant(text[i]);
                if (i + 1 < text.Length)
                {
                    string two = new string(new[] { raw, char.ToLowerInvariant(text[i + 1]) });
                    if (two == "дж" || two == "дз")
                    {
                        sb.Append('j');
                        i++;
                        continue;
                    }
                    if (two == "кс")
                    {
                        sb.Append('x');
                        i++;
                        continue;
                    }
                }

                switch (raw)
                {
                    case 'а': sb.Append('a'); break;
                    case 'б': sb.Append('b'); break;
                    case 'в': sb.Append(softW ? 'w' : 'v'); break;
                    case 'г': sb.Append('g'); break;
                    case 'д': sb.Append('d'); break;
                    case 'е': sb.Append('e'); break;
                    case 'ё': sb.Append("yo"); break;
                    case 'ж': sb.Append('j'); break;
                    case 'з': sb.Append('z'); break;
                    case 'и': sb.Append('i'); break;
                    case 'й': sb.Append('i'); break;
                    case 'к': sb.Append('k'); break;
                    case 'л': sb.Append('l'); break;
                    case 'м': sb.Append('m'); break;
                    case 'н': sb.Append('n'); break;
                    case 'о': sb.Append('o'); break;
                    case 'п': sb.Append('p'); break;
                    case 'р': sb.Append('r'); break;
                    case 'с': sb.Append('s'); break;
                    case 'т': sb.Append('t'); break;
                    case 'у': sb.Append('u'); break;
                    case 'ф': sb.Append('f'); break;
                    case 'х': sb.Append('h'); break;
                    case 'ц': sb.Append("ts"); break;
                    case 'ч': sb.Append("ch"); break;
                    case 'ш': sb.Append("sh"); break;
                    case 'щ': sb.Append("sh"); break;
                    case 'ъ': break;
                    case 'ы': sb.Append('i'); break;
                    case 'ь': break;
                    case 'э': sb.Append('e'); break;
                    case 'ю': sb.Append("yu"); break;
                    case 'я': sb.Append("ya"); break;
                    default: sb.Append(raw); break;
                }
            }
            return sb.ToString();
        }

        public static string PhoneticToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            if (ContainsCyrillic(token))
                token = TransliterateRuToEn(token, false);

            token = NormalizeBasic(token).Replace(" ", "");
            if (token.Length == 0) return "";

            var sb = new StringBuilder(token.Length);
            int n = token.Length;
            for (int i = 0; i < n; i++)
            {
                char c = token[i];
                char next = (i + 1 < n) ? token[i + 1] : '\0';
                char next2 = (i + 2 < n) ? token[i + 2] : '\0';

                // Multi-char English/translit sound folds
                if (c == 's' && next == 'h' && next2 == 'c' && i + 3 < n && token[i + 3] == 'h')
                {
                    sb.Append("sh"); i += 3; continue;
                }
                if (c == 'd' && next == 'g' && (next2 == 'e' || next2 == 'i' || next2 == 'y'))
                {
                    sb.Append('j'); i += 2; continue;
                }
                if (c == 'd' && next == 'g')
                {
                    sb.Append('j'); i++; continue;
                }
                if (c == 'c' && next == 'h' && next2 == 'r')
                {
                    // "chrome" -> hrom (aligns closely with "хром")
                    sb.Append("hr"); i += 2; continue;
                }
                if (c == 't' && next == 'c' && next2 == 'h')
                {
                    sb.Append("ch"); i += 2; continue;
                }
                if (c == 'p' && next == 'h')
                {
                    sb.Append('f'); i++; continue;
                }
                if (c == 't' && next == 'h')
                {
                    sb.Append('t'); i++; continue;
                }
                if (c == 'k' && next == 'h')
                {
                    sb.Append('h'); i++; continue;
                }
                if (c == 'z' && next == 'h')
                {
                    sb.Append('j'); i++; continue;
                }
                if (c == 'c' && next == 'k')
                {
                    sb.Append('k'); i++; continue;
                }
                if (c == 'q' && next == 'u')
                {
                    sb.Append("kv"); i++; continue;
                }
                if ((c == 'e' && (next == 'e' || next == 'a')) || (c == 'i' && next == 'e'))
                {
                    sb.Append('i'); i++; continue;
                }
                if (c == 'o' && (next == 'o' || next == 'u' || next == 'w'))
                {
                    sb.Append('u'); i++; continue;
                }
                if (c == 'a' && (next == 'i' || next == 'y'))
                {
                    sb.Append("ai"); i++; continue;
                }
                if (c == 'y' && (next == 'u' || next == 'o' || next == 'a' || next == 'e'))
                {
                    if (next == 'u') sb.Append('u');
                    else if (next == 'o') sb.Append('o');
                    else if (next == 'a') sb.Append("ya");
                    else sb.Append('e');
                    i++;
                    continue;
                }

                // Single-char normalizations
                if (c == 'c')
                {
                    sb.Append((next == 'e' || next == 'i' || next == 'y') ? 's' : 'k');
                }
                else if (c == 'q')
                {
                    sb.Append('k');
                }
                else if (c == 'w')
                {
                    sb.Append('v');
                }
                else if (c == 'y')
                {
                    sb.Append('i');
                }
                else if (c == 'x')
                {
                    sb.Append("ks");
                }
                else
                {
                    sb.Append(c);
                }
            }

            // Collapse doubled chars (google -> gugl, app -> ap, torrent -> torent)
            var collapsed = new StringBuilder(sb.Length);
            for (int i = 0; i < sb.Length; i++)
            {
                if (collapsed.Length == 0 || collapsed[collapsed.Length - 1] != sb[i])
                    collapsed.Append(sb[i]);
            }

            string res = collapsed.ToString();
            // Drop trailing silent 'e' on words of length >= 4 (code -> kod, chrome -> hrom, edge -> ej)
            if (res.Length >= 4 && res[res.Length - 1] == 'e')
                res = res.Substring(0, res.Length - 1);
            // Drop final trailing 'i' / 'j' nuance after consonant clusters? Keep for specificity.
            return res;
        }

        public static IndexedText BuildIndex(string rawText)
        {
            string lower = (rawText ?? "").ToLowerInvariant();
            string norm = NormalizeBasic(rawText);
            string[] tokens = norm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var phonTokens = new List<string>(tokens.Length);
            var acr = new StringBuilder(tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                string pt = PhoneticToken(tokens[i]);
                if (!string.IsNullOrEmpty(pt))
                    phonTokens.Add(pt);
                if (tokens[i].Length > 0)
                    acr.Append(tokens[i][0]);
            }

            return new IndexedText
            {
                RawLower = lower,
                AlnumSpaced = norm,
                Joined = norm.Replace(" ", ""),
                Tokens = tokens,
                PhoneticTokens = phonTokens.ToArray(),
                PhoneticJoined = string.Join("", phonTokens.ToArray()),
                Acronym = acr.ToString()
            };
        }

        private struct QueryVariant
        {
            public string Text;
            public string Joined;
            public string[] Tokens;
            public string[] PhoneticTokens;
            public string PhoneticJoined;
            public int Penalty;
            public string Kind;
            public string Label;
        }

        private static List<QueryVariant> BuildQueryVariants(string rawQuery)
        {
            var list = new List<QueryVariant>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string, int, string, string> addVariant = (candidate, penalty, kind, label) =>
            {
                string norm = NormalizeBasic(candidate);
                if (string.IsNullOrEmpty(norm)) return;
                string key = norm + "|" + kind;
                if (!seen.Add(key)) return;

                string[] toks = norm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var ptoks = new string[toks.Length];
                for (int i = 0; i < toks.Length; i++)
                    ptoks[i] = PhoneticToken(toks[i]);

                list.Add(new QueryVariant
                {
                    Text = norm,
                    Joined = norm.Replace(" ", ""),
                    Tokens = toks,
                    PhoneticTokens = ptoks,
                    PhoneticJoined = string.Join("", ptoks),
                    Penalty = penalty,
                    Kind = kind,
                    Label = label
                });
            };

            string trimmed = (rawQuery ?? "").Trim();
            if (trimmed.Length == 0) return list;

            // 1. Direct query as typed
            addVariant(trimmed, 0, "direct", null);

            bool hasCyr = ContainsCyrillic(trimmed);
            bool hasLat = ContainsLatin(trimmed);

            // 2. Wrong keyboard layout conversion (only meaningful if input has Cyrillic -> EN, or Latin -> RU)
            if (hasCyr)
            {
                string swapped = LayoutSwapRuToEn(trimmed);
                // Preserve clean spacing if layout punctuation interfered, and only add if it actually has Latin letters
                if (ContainsLatin(swapped))
                {
                    string cleanSwap = CleanLayoutPunctuation(swapped);
                    addVariant(cleanSwap, 25, "layout", cleanSwap.ToLowerInvariant());
                }

                // 3. Phonetic transliteration (хром -> hrom, блендер -> blender, стим -> stim)
                string tr = TransliterateRuToEn(trimmed, false);
                addVariant(tr, 45, "translit", tr.ToLowerInvariant());

                string trW = TransliterateRuToEn(trimmed, true);
                if (trW != tr)
                    addVariant(trW, 55, "translit", trW.ToLowerInvariant());
            }

            if (hasLat)
            {
                string swappedRu = LayoutSwapEnToRu(trimmed);
                if (ContainsCyrillic(swappedRu))
                    addVariant(swappedRu, 25, "layout", swappedRu.ToLowerInvariant());
            }

            // 4. Expand whole-phrase and token synonyms across generated forms
            int baseCount = list.Count;
            for (int i = 0; i < baseCount; i++)
            {
                string baseNorm = list[i].Text;
                int basePenalty = list[i].Penalty;

                string[] phraseSyns;
                if (SynonymGroups.TryGetValue(baseNorm, out phraseSyns))
                {
                    for (int s = 0; s < phraseSyns.Length; s++)
                        addVariant(phraseSyns[s], basePenalty + 60, "alias", phraseSyns[s]);
                }

                // Also try single-word or 2-word replacements for queries like "открыть хром" or "вс код"
                string[] words = list[i].Tokens;
                if (words.Length == 1)
                {
                    // Already checked above via phraseSyns
                }
                else
                {
                    var expandedParts = new List<string>();
                    bool anyExpanded = false;
                    for (int w = 0; w < words.Length; w++)
                    {
                        string[] wSyns;
                        if (SynonymGroups.TryGetValue(words[w], out wSyns) && wSyns.Length > 0)
                        {
                            expandedParts.Add(wSyns[0]);
                            anyExpanded = true;
                        }
                        else
                        {
                            expandedParts.Add(words[w]);
                        }
                    }
                    if (anyExpanded)
                    {
                        string joined = string.Join(" ", expandedParts.ToArray());
                        addVariant(joined, basePenalty + 75, "alias", joined);
                    }
                }
            }

            return list;
        }

        private static string CleanLayoutPunctuation(string swapped)
        {
            // Keep letters, digits, spaces, '.', '-', '_'; replace layout punctuation like [ ] ; ' , with space or keep readable
            var sb = new StringBuilder(swapped.Length);
            for (int i = 0; i < swapped.Length; i++)
            {
                char c = swapped[i];
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
                    sb.Append(c);
                else
                    sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        public static SearchMatch Match(IndexedText target, string rawQuery)
        {
            var none = new SearchMatch { Score = 0, InterpretedAs = null, MatchKind = null };
            if (target == null || string.IsNullOrWhiteSpace(rawQuery)) return none;

            List<QueryVariant> variants = BuildQueryVariants(rawQuery);
            if (variants.Count == 0) return none;

            int bestScore = 0;
            string bestInterp = null;
            string bestKind = null;

            for (int v = 0; v < variants.Count; v++)
            {
                int score = ScoreVariant(target, variants[v]) - variants[v].Penalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestInterp = variants[v].Label;
                    bestKind = variants[v].Kind;
                }
            }

            if (bestScore <= 0) return none;
            return new SearchMatch
            {
                Score = bestScore,
                InterpretedAs = bestInterp,
                MatchKind = bestKind
            };
        }

        public static SearchMatch Match(string rawTarget, string rawQuery)
        {
            return Match(BuildIndex(rawTarget), rawQuery);
        }

        private static int ScoreVariant(IndexedText t, QueryVariant q)
        {
            if (string.IsNullOrEmpty(q.Text)) return 0;

            // 1. Exact phrase / prefix / substring on normalized full text
            if (t.AlnumSpaced == q.Text) return 1000;
            if (t.Joined == q.Joined && q.Joined.Length >= 2) return 960;
            if (t.AlnumSpaced.StartsWith(q.Text, StringComparison.Ordinal)) return 900;
            if (t.Joined.StartsWith(q.Joined, StringComparison.Ordinal) && q.Joined.Length >= 3) return 870;

            // Check token boundaries
            int idx = t.AlnumSpaced.IndexOf(" " + q.Text, StringComparison.Ordinal);
            if (idx >= 0) return 840 - Math.Min(40, idx);

            if (q.Text.Length >= 2)
            {
                int sub = t.AlnumSpaced.IndexOf(q.Text, StringComparison.Ordinal);
                if (sub >= 0) return 760 - Math.Min(60, sub);

                int jSub = t.Joined.IndexOf(q.Joined, StringComparison.Ordinal);
                if (jSub >= 0 && q.Joined.Length >= 3) return 730 - Math.Min(60, jSub);
            }

            // 2. Phonetic joined / token matches (catches гугл -> Google, стим -> Steam, вскод -> VS Code, питон -> Python)
            if (!string.IsNullOrEmpty(q.PhoneticJoined) && q.PhoneticJoined.Length >= 2)
            {
                if (t.PhoneticJoined == q.PhoneticJoined) return 890;
                if (t.PhoneticJoined.StartsWith(q.PhoneticJoined, StringComparison.Ordinal) && q.PhoneticJoined.Length >= 3) return 820;

                for (int i = 0; i < t.PhoneticTokens.Length; i++)
                {
                    if (t.PhoneticTokens[i] == q.PhoneticJoined) return 860 - i * 10;
                    if (t.PhoneticTokens[i].StartsWith(q.PhoneticJoined, StringComparison.Ordinal) && q.PhoneticJoined.Length >= 3)
                        return 790 - i * 10;
                }

                if (q.PhoneticJoined.Length >= 3)
                {
                    int pj = t.PhoneticJoined.IndexOf(q.PhoneticJoined, StringComparison.Ordinal);
                    if (pj >= 0) return 700 - Math.Min(60, pj);
                }
            }

            // 3. Multi-word token matching (every query token matches some target token or phonetic token)
            if (q.Tokens.Length > 1)
            {
                int total = 0;
                bool allOk = true;
                for (int qi = 0; qi < q.Tokens.Length; qi++)
                {
                    int tokBest = BestTokenScore(t, q.Tokens[qi], q.PhoneticTokens[qi]);
                    if (tokBest <= 0)
                    {
                        allOk = false;
                        break;
                    }
                    total += tokBest;
                }
                if (allOk)
                    return Math.Max(400, (total / q.Tokens.Length) - 40);
            }

            // 4. Acronym match (e.g. "vsc" -> "Visual Studio Code", "всц" -> layout "vsc")
            if (q.Joined.Length >= 2 && q.Joined.Length <= 6 && !string.IsNullOrEmpty(t.Acronym))
            {
                if (t.Acronym == q.Joined) return 770;
                if (t.Acronym.StartsWith(q.Joined, StringComparison.Ordinal)) return 680;
            }

            // 5. Fuzzy typo / small edit distance on tokens (e.g. "калькулятор" -> "calculator", "скщьу" -> "chrome")
            if (q.PhoneticTokens.Length == 1 && q.PhoneticTokens[0].Length >= 4)
            {
                string pq = q.PhoneticTokens[0];
                int maxDist = pq.Length >= 7 ? 2 : 1;
                int bestTok = 0;
                for (int ti = 0; ti < t.PhoneticTokens.Length; ti++)
                {
                    string pt = t.PhoneticTokens[ti];
                    if (Math.Abs(pt.Length - pq.Length) <= maxDist)
                    {
                        int d = DamerauLevenshtein(pq, pt, maxDist);
                        if (d <= maxDist)
                        {
                            int s = 610 - d * 90 - ti * 10;
                            if (s > bestTok) bestTok = s;
                        }
                    }
                    else if (pt.Length > pq.Length + 1 && pq.Length >= 4)
                    {
                        // Check prefix of length pq.Length (handles prefix with 1 typo like "кальк" vs "kalkulator")
                        string pref = pt.Substring(0, pq.Length);
                        int d = DamerauLevenshtein(pq, pref, 1);
                        if (d <= 1)
                        {
                            int s = 560 - d * 80 - ti * 10;
                            if (s > bestTok) bestTok = s;
                        }
                    }
                }
                if (bestTok > 0) return bestTok;
            }

            // 6. Ordered subsequence fallback (for abbreviated latin/swapped typing like "chrm", "dscd", "tgl")
            if (q.Joined.Length >= 3)
            {
                int subSeqScore = OrderedSubsequenceScore(t.Joined, q.Joined);
                if (subSeqScore > 0) return subSeqScore;

                if (!string.IsNullOrEmpty(q.PhoneticJoined) && q.PhoneticJoined.Length >= 3)
                {
                    int pSubScore = OrderedSubsequenceScore(t.PhoneticJoined, q.PhoneticJoined);
                    if (pSubScore > 0) return pSubScore;
                }
            }

            return 0;
        }

        private static int BestTokenScore(IndexedText t, string tok, string ptok)
        {
            int best = 0;
            for (int i = 0; i < t.Tokens.Length; i++)
            {
                string tt = t.Tokens[i];
                if (tt == tok) return 900 - i * 10;
                if (tt.StartsWith(tok, StringComparison.Ordinal))
                    best = Math.Max(best, 800 - i * 10);
                else if (tok.Length >= 3 && tt.Contains(tok))
                    best = Math.Max(best, 680 - i * 10);
            }
            for (int i = 0; i < t.PhoneticTokens.Length; i++)
            {
                string pt = t.PhoneticTokens[i];
                if (pt == ptok && ptok.Length >= 2)
                    best = Math.Max(best, 830 - i * 10);
                else if (ptok.Length >= 3 && pt.StartsWith(ptok, StringComparison.Ordinal))
                    best = Math.Max(best, 740 - i * 10);
                else if (ptok.Length >= 4 && pt.Contains(ptok))
                    best = Math.Max(best, 640 - i * 10);
                else if (ptok.Length >= 4 && Math.Abs(pt.Length - ptok.Length) <= 1)
                {
                    int d = DamerauLevenshtein(ptok, pt, 1);
                    if (d <= 1) best = Math.Max(best, 560 - i * 10);
                }
            }
            return best;
        }

        private static int OrderedSubsequenceScore(string hay, string needle)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle)) return 0;
            if (needle.Length > hay.Length) return 0;

            int hi = 0;
            int gaps = 0;
            int firstMatch = -1;
            int prevMatch = -1;

            for (int ni = 0; ni < needle.Length; ni++)
            {
                char nc = needle[ni];
                int found = -1;
                while (hi < hay.Length)
                {
                    if (hay[hi] == nc)
                    {
                        found = hi;
                        hi++;
                        break;
                    }
                    hi++;
                }
                if (found < 0) return 0;
                if (firstMatch < 0) firstMatch = found;
                if (prevMatch >= 0) gaps += (found - prevMatch - 1);
                prevMatch = found;
            }

            // Reject overly scattered accidental matches
            if (gaps > needle.Length * 3) return 0;
            int score = 460 - gaps * 18 - firstMatch * 8;
            return score > 220 ? score : 220;
        }

        private static int DamerauLevenshtein(string a, string b, int limit)
        {
            int la = a.Length;
            int lb = b.Length;
            if (Math.Abs(la - lb) > limit) return limit + 1;

            var d = new int[la + 1, lb + 1];
            for (int i = 0; i <= la; i++) d[i, 0] = i;
            for (int j = 0; j <= lb; j++) d[0, j] = j;

            for (int i = 1; i <= la; i++)
            {
                int rowMin = int.MaxValue;
                for (int j = 1; j <= lb; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    int val = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);

                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        val = Math.Min(val, d[i - 2, j - 2] + 1);

                    d[i, j] = val;
                    if (val < rowMin) rowMin = val;
                }
                if (rowMin > limit && i > 1) return limit + 1;
            }
            return d[la, lb];
        }
    }
}
