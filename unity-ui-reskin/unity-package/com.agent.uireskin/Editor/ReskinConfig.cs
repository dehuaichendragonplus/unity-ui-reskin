// UIReskin 项目适配：本地化文字组件、语言字体目录、材质命名、默认字体、截图目录、写材质后的钩子。
// 不内置任何项目的组件名 / 字体名 / 命名规则。取值顺序：
//   1. 项目事实 .uireskin/facts.json 的 "text" 段（代理调研核实后写入，权威）；
//   2. 事实里没写的字段：通用探测（TypeCache 找带材质名字段的组件、按目录名分组的 TMP 字体、prefab 抽样），结果缓存在 Library/UIReskin/detected.json。
//      探测值只是候选：Dump() 会标出来，核实后写进项目事实。
// 常用：ReskinConfig.Dump() 打印生效值、来源与待调研项；ReskinConfig.Redetect() 丢掉探测缓存重来。
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UIReskin
{
    [Serializable]
    public class ReskinConfigData
    {
        // ---- 本地化文字组件（ReskinProbe / ReskinHealth / ReskinTextStyle / ReskinMaterialStyle 用） ----
        /// 组件类型名（GetComponent(string) 用短名）；空 = 项目没有这类组件 / 未调研，文字样式工具只改 TMP 本身
        public string locTextType = "";
        /// 组件上记录「样式材质名」的序列化字段（拼写照组件源码）
        public string locMaterialField = "";
        /// 样式字段值的前缀：字段值 = 前缀 + 样式名（按组件源码里查样式的规则填，填错会写成运行时查不到的名字）
        public string locMaterialPrefix = "";
        /// 体检里「样式字段 ≠ 预制体当前材质的样式名」算问题（true：组件 Awake 按材质名反推字段的体系）还是只作提示（false：运行时按字段查字库的体系）
        public bool healthFieldMatchesMaterial = false;
        /// 组件上的多语言 key 字段（runtime.json 的 term 用）
        public string locTermField = "";

        // ---- 字体 ----
        /// 新建界面 / 换字体时用的默认 TMP 字体资产名；空 = 用工程 prefab 里引用最多的 TMP 字体
        public string defaultFont = "";
        /// 语言字体根目录（以 / 结尾），每种语言一个子目录；空 = 项目没有按语言分目录的字体体系
        public string fontRoot = "";
        /// TMP 字体资产名模板（{lang} = 语言目录名）
        public string fontAssetPattern = "";
        /// 样式材质名模板（不含 .mat；{lang} 语言，{style} 样式名），文件在 fontRoot/{lang}/ 下
        public string materialPattern = "";
        /// 字体资产自带默认材质对应的样式名（TMP 默认材质名是「<字体> Material」）
        public string defaultMaterialStyle = "Material";
        /// ReskinTextStyle 找不到节点当前样式时用来克隆的源样式名
        public string fallbackSourceStyle = "";
        /// ReskinMaterialStyle 的 materials[].source 缺省值
        public string defaultCloneSource = "";
        /// 报告里打印 dilate 的语言
        public string reportLang = "";
        /// 描边像素校准系数（理论公式 → 实测粗细比）。每个项目的字体图集不同，要按 diff 报告「实测宽比」校准后写进项目事实；1.54 是 FoodFlow1 的实测值，仅作起点
        public float outlineCalibration = 1.54f;

        // ---- 写完样式材质后的钩子 ----
        /// 写入新样式材质后调用的静态无参方法 "Namespace.Type.Method"（例：把新材质登记进运行时字库清单）；也可在项目代码上标 [UIReskin.UIReskinHook]
        public string afterMaterialsHook = "";

        // ---- 截图 / 产物 ----
        /// 截图与报告的根目录（相对 Unity 工程根）
        public string shotsRoot = "Library/ReskinShots";
        /// Pad 判定：高/宽 ≤ 该值视为 Pad
        public float padAspectMax = 1.605f;
        /// Pad 预制体后缀
        public string padSuffix = "_Pad";
    }

    public static class ReskinConfig
    {
        public const string CachePath = "Library/UIReskin/detected.json";
        static ReskinConfigData _data;
        static string _source = "", _detectLog = "", _factsText = null;

        public static ReskinConfigData Data
        {
            get
            {
                if (_data == null || ReskinFacts.Section("text") != _factsText) Reload();
                return _data;
            }
        }

        /// 探测（有缓存读缓存）→ 叠加项目事实 "text" 段（只覆盖事实里写了的字段）
        public static string Reload()
        {
            var cache = Path.Combine(ReskinCore.ProjectRoot, CachePath);
            ReskinConfigData d = null;
            if (File.Exists(cache))
            {
                try { d = JsonUtility.FromJson<ReskinConfigData>(File.ReadAllText(cache)); _source = "探测缓存"; } catch { d = null; }
            }
            if (d == null) { d = Detect(out _detectLog); _source = "通用探测"; SaveCache(d); }
            _factsText = ReskinFacts.Section("text");
            if (_factsText != null)
            {
                try { JsonUtility.FromJsonOverwrite(_factsText, d); _source += " + 项目事实"; }
                catch (Exception e) { Debug.LogError("[RESKIN] facts.json 的 text 段解析失败，忽略：" + e.Message); }
            }
            if (!string.IsNullOrEmpty(d.fontRoot) && !d.fontRoot.EndsWith("/")) d.fontRoot += "/";
            _data = d;
            return "[RESKIN] 配置来源：" + _source;
        }

        /// 丢掉探测缓存重新探测
        public static string Redetect()
        {
            var cache = Path.Combine(ReskinCore.ProjectRoot, CachePath);
            if (File.Exists(cache)) File.Delete(cache);
            _data = null;
            return Dump();
        }

        static void SaveCache(ReskinConfigData d)
        {
            try
            {
                var cache = Path.Combine(ReskinCore.ProjectRoot, CachePath);
                Directory.CreateDirectory(Path.GetDirectoryName(cache));
                File.WriteAllText(cache, JsonUtility.ToJson(d, true));
            }
            catch (Exception e) { Debug.LogWarning("[RESKIN] 探测结果缓存失败：" + e.Message); }
        }

        /// 生效值 + 来源：事实里写了的字段标「事实」，其余标「探测，待核实」；最后列出建议写进项目事实的 JSON
        public static string Dump()
        {
            _data = null; var head = Reload();
            var sb = new System.Text.StringBuilder(head + "\n" + ReskinFacts.Describe() + "\n");
            if (!string.IsNullOrEmpty(_detectLog)) sb.AppendLine("探测记录：" + _detectLog);
            var facts = _factsText ?? "";
            foreach (var f in typeof(ReskinConfigData).GetFields())
            {
                bool fromFacts = facts.Contains("\"" + f.Name + "\"");
                sb.AppendLine($"  {f.Name,-28} = {f.GetValue(Data)}   [{(fromFacts ? "事实" : "探测，待核实")}]");
            }
            sb.AppendLine("核实无误的值写进 .uireskin/facts.json 的 \"text\" 段（同时在 facts.md 写证据），下次直接用。");
            return sb.ToString();
        }

        // ---------- 自动探测 ----------
        static readonly string[] MatFieldNames = { "m_Matrial", "m_Material", "m_MaterialName", "materialName", "MaterialName" };
        static readonly string[] TermFieldNames = { "Term", "term", "m_Term", "Key", "key", "m_Key" };

        public static ReskinConfigData Detect(out string log)
        {
            var d = new ReskinConfigData();
            var notes = new System.Collections.Generic.List<string>();

            // 1) 本地化文字组件候选：项目里带「材质名字符串字段」的组件；多个时按 prefab 引用次数取最多的（只是候选，需核实）
            var cands = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                .Where(t => !t.IsAbstract && t.Namespace?.StartsWith("Unity") != true && t.Namespace?.StartsWith("TMPro") != true && FindStringField(t, MatFieldNames) != null).ToList();
            Type locT = null;
            if (cands.Count == 1) locT = cands[0];
            else if (cands.Count > 1)
            {
                var usage = cands.ToDictionary(t => t, ScriptUsage);
                locT = usage.OrderByDescending(kv => kv.Value).First().Key;
                notes.Add("文字组件候选 " + string.Join("、", usage.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.Name}(prefab 引用 {kv.Value})")));
            }
            if (locT != null)
            {
                d.locTextType = locT.Name;
                d.locMaterialField = FindStringField(locT, MatFieldNames) ?? "";
                d.locTermField = FindStringField(locT, TermFieldNames) ?? "";
                notes.Add($"文字组件取 {locT.FullName}.{d.locMaterialField}");
            }
            else notes.Add("没找到带样式字段的本地化文字组件");

            // 2) 语言字体：字体资产名里包含所在目录名（.../En/LocaleFont_En SDF.asset）→ 模板 + 根目录；取语言数最多的一组
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
            foreach (var g in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var name = Path.GetFileNameWithoutExtension(path);
                var dir = Path.GetDirectoryName(path).Replace('\\', '/');
                var lang = dir.Substring(dir.LastIndexOf('/') + 1);
                if (lang.Length < 2 || lang.Length > 8 || !name.Contains(lang)) continue;
                var key = dir.Substring(0, dir.LastIndexOf('/') + 1) + "|" + ReplaceOnce(name, lang, "{lang}");
                if (!groups.TryGetValue(key, out var set)) groups[key] = set = new System.Collections.Generic.HashSet<string>();
                set.Add(lang);
            }
            string best = null; int bestN = 1;
            foreach (var kv in groups) if (kv.Value.Count > bestN) { best = kv.Key; bestN = kv.Value.Count; }
            if (best != null)
            {
                var parts = best.Split('|');
                d.fontRoot = parts[0]; d.fontAssetPattern = parts[1]; d.materialPattern = parts[1] + " {style}";
                var langs = groups[best];
                d.reportLang = langs.Contains("En") ? "En" : langs.Contains("en") ? "en" : System.Linq.Enumerable.First(langs);
                // 克隆源：报告语言目录里已有的样式材质，优先 Shadow / OutLine
                var styles = new System.Collections.Generic.List<string>();
                var prefixName = d.fontAssetPattern.Replace("{lang}", d.reportLang) + " ";
                foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { d.fontRoot + d.reportLang }))
                {
                    var mn = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g));
                    if (mn.StartsWith(prefixName)) styles.Add(mn.Substring(prefixName.Length));
                }
                string Pick(params string[] keys) { foreach (var k in keys) foreach (var st in styles) if (st.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return st; return styles.Count > 0 ? styles[0] : ""; }
                d.defaultCloneSource = Pick("Shadow", "OutLine", "Outline");
                d.fallbackSourceStyle = Pick("OutLine", "Outline", "Shadow");
                notes.Add($"字体 {d.fontRoot}{{{string.Join("/", langs)}}}/{d.fontAssetPattern}，样式材质 {styles.Count} 种");
            }
            else { d.fontRoot = ""; notes.Add("没找到按语言分目录的 TMP 字体（文字样式材质工具不可用）"); }

            // 3) 样式字段前缀：抽样预制体 YAML 里的字段值
            if (!string.IsNullOrEmpty(d.locMaterialField))
            {
                int total = 0, sdf = 0, files = 0;
                var re = new Regex("^\\s*" + Regex.Escape(d.locMaterialField) + ": (.+)$", RegexOptions.Multiline);
                try
                {
                    foreach (var f in Directory.EnumerateFiles(Application.dataPath, "*.prefab", SearchOption.AllDirectories))
                    {
                        if (++files > 4000 || total >= 60) break;
                        string text; try { text = File.ReadAllText(f); } catch { continue; }
                        if (!text.Contains(d.locMaterialField + ": ")) continue;
                        foreach (Match m in re.Matches(text))
                        {
                            var v = m.Groups[1].Value.Trim().Trim('\'', '"');
                            if (v.Length == 0) continue;
                            total++; if (v.StartsWith("SDF ")) sdf++;
                        }
                    }
                }
                catch (Exception e) { notes.Add("前缀抽样失败 " + e.Message); }
                d.locMaterialPrefix = total > 0 && sdf * 10 >= total * 6 ? "SDF " : "";
                notes.Add($"字段前缀 '{d.locMaterialPrefix}'（抽样 {total} 个值，SDF 开头 {sdf}）");
            }

            // 3.5) 默认字体候选：prefab 里引用最多的 TMP 字体
            d.defaultFont = MostUsedTmpFont(out var fontLog);
            if (!string.IsNullOrEmpty(fontLog)) notes.Add(fontLog);

            // 4) 写材质后的钩子：约定任意程序集里的静态无参方法 UIReskinHooks.AfterMaterials
            foreach (var t in TypeCache.GetTypesWithAttribute<UIReskinHookAttribute>())
                foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (m.GetParameters().Length == 0 && m.GetCustomAttributes(typeof(UIReskinHookAttribute), false).Length > 0)
                    { d.afterMaterialsHook = t.FullName + "." + m.Name; notes.Add("钩子 " + d.afterMaterialsHook); }

            log = string.Join("；", notes);
            return d;
        }

        static string FindStringField(Type t, string[] names)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            for (var x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                foreach (var n in names)
                {
                    var f = x.GetField(n, F | System.Reflection.BindingFlags.DeclaredOnly);
                    if (f != null && f.FieldType == typeof(string)) return n;
                }
            return null;
        }

        /// 抽样 prefab，统计某个脚本被引用的次数（按脚本 guid）
        static int ScriptUsage(Type t)
        {
            var guid = AssetDatabase.FindAssets(t.Name + " t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == t.Name).Select(AssetDatabase.AssetPathToGUID).FirstOrDefault();
            if (string.IsNullOrEmpty(guid)) return 0;
            int n = 0, files = 0;
            foreach (var f in Directory.EnumerateFiles(Application.dataPath, "*.prefab", SearchOption.AllDirectories))
            {
                if (++files > 3000) break;
                try { var txt = File.ReadAllText(f); int i = 0; while ((i = txt.IndexOf(guid, i, StringComparison.Ordinal)) >= 0) { n++; i += 32; } } catch { }
            }
            return n;
        }

        /// prefab 里引用最多的 TMP 字体资产名（m_fontAsset guid 计数）
        public static string MostUsedTmpFont(out string log)
        {
            var cnt = new System.Collections.Generic.Dictionary<string, int>(); int files = 0;
            var re = new Regex(@"m_fontAsset: \{fileID: -?\d+, guid: ([0-9a-f]{32})");
            try
            {
                foreach (var f in Directory.EnumerateFiles(Application.dataPath, "*.prefab", SearchOption.AllDirectories))
                {
                    if (++files > 3000) break;
                    string txt; try { txt = File.ReadAllText(f); } catch { continue; }
                    foreach (Match m in re.Matches(txt)) cnt[m.Groups[1].Value] = cnt.TryGetValue(m.Groups[1].Value, out var c) ? c + 1 : 1;
                }
            }
            catch (Exception e) { log = "字体抽样失败 " + e.Message; return ""; }
            var top = cnt.OrderByDescending(kv => kv.Value).Take(3).Select(kv => (name: Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(kv.Key)), n: kv.Value)).Where(x => !string.IsNullOrEmpty(x.name)).ToList();
            log = top.Count > 0 ? "常用 TMP 字体 " + string.Join("、", top.Select(x => $"{x.name}×{x.n}")) : "";
            return top.Count > 0 ? top[0].name : "";
        }

        static string ReplaceOnce(string s, string a, string b) { int i = s.IndexOf(a, StringComparison.Ordinal); return i < 0 ? s : s.Substring(0, i) + b + s.Substring(i + a.Length); }


        [MenuItem("Tools/UIReskin/打印当前配置")]
        static void MenuDump() => Debug.Log(Dump());

        [MenuItem("Tools/UIReskin/重新探测配置")]
        static void MenuRedetect() => Debug.Log(Redetect());

        // ---------- 命名辅助 ----------
        public static string FontAssetName(string lang) => Data.fontAssetPattern.Replace("{lang}", lang);
        public static string MaterialName(string lang, string style) => Data.materialPattern.Replace("{lang}", lang).Replace("{style}", style);
        public static string MaterialPath(string lang, string style) => $"{Data.fontRoot}{lang}/{MaterialName(lang, style)}.mat";
        public static string FontAssetPath(string lang) => $"{Data.fontRoot}{lang}/{FontAssetName(lang)}.asset";

        /// 组件样式字段值（带前缀）→ 样式名
        public static string StyleFromField(string fieldValue)
        {
            if (string.IsNullOrEmpty(fieldValue)) return fieldValue;
            var pre = Data.locMaterialPrefix ?? "";
            var v = (pre.Length > 0 && fieldValue.StartsWith(pre) ? fieldValue.Substring(pre.Length) : fieldValue).Trim();
            // 和常见字库查找一样容错：材质模板里已含 "SDF" 时，字段值带的 "SDF " 前缀去掉（"SDF Shadow Blue" 与 "Shadow Blue" 等价）
            if (v.StartsWith("SDF ") && Data.materialPattern.Contains("SDF {style}")) v = v.Substring(4).Trim();
            return v;
        }

        /// 样式名 → 组件样式字段值（带前缀）
        public static string FieldFromStyle(string style) => (Data.locMaterialPrefix ?? "") + style;

        /// 从 TMP 字体资产名反推语言（按 fontAssetPattern 匹配）；匹配不上返回 fallback
        public static string LangOfFont(string fontName, string fallback)
        {
            if (string.IsNullOrEmpty(fontName)) return fallback;
            var m = Regex.Match(fontName, "^" + PatternToRegex(Data.fontAssetPattern) + "$");
            return m.Success ? m.Groups["lang"].Value : fallback;
        }

        /// 从材质名反推样式名（按 materialPattern 匹配）；匹配不上返回 null
        public static string StyleOfMaterial(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return null;
            var m = Regex.Match(materialName, "^" + PatternToRegex(Data.materialPattern) + "$");
            return m.Success && m.Groups["style"].Success ? m.Groups["style"].Value.Trim() : null;
        }

        static string PatternToRegex(string pattern) =>
            Regex.Escape(pattern).Replace(@"\{lang}", "(?<lang>.+?)").Replace(@"\{style}", "(?<style>.*)");

        /// 本地化文字组件（没有配置或节点上没有返回 null）
        public static Component LocText(Component c) =>
            string.IsNullOrEmpty(Data.locTextType) || c == null ? null : c.GetComponent(Data.locTextType);

        /// 执行 afterMaterialsHook
        public static string RunAfterMaterialsHook()
        {
            var full = Data.afterMaterialsHook;
            if (string.IsNullOrEmpty(full)) return "";
            int i = full.LastIndexOf('.');
            if (i <= 0) return "  ⚠ afterMaterialsHook 格式应为 Namespace.Type.Method：" + full;
            string typeName = full.Substring(0, i), method = full.Substring(i + 1);
            Type t = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(typeName); if (t != null) break; }
            var m = t?.GetMethod(method, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m == null) return "  ⚠ 找不到钩子 " + full + "（类型或无参静态方法不存在），未执行";
            try { return "  钩子 " + full + "：" + (m.Invoke(null, null)?.ToString() ?? "完成"); }
            catch (Exception e) { return "  ⚠ 钩子 " + full + " 抛异常：" + (e.InnerException ?? e).Message; }
        }
    }

    /// 标在项目自己的 Editor 代码上：类和其中一个静态无参方法都标上，写完新样式材质后自动调用（例：把新材质登记进运行时字库清单）。
    /// [UIReskin.UIReskinHook] static class MyHooks { [UIReskin.UIReskinHook] static string AfterMaterials() { ...; return "字库已刷新"; } }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class UIReskinHookAttribute : Attribute { }
}
