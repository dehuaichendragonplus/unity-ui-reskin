// UIReskin 项目适配：本地化文字组件、语言字体目录、材质命名、截图目录、写材质后的钩子。
// 零配置：首次使用时自动探测（结果缓存到 Library/UIReskin/detected.json，不进版本库），探测不到的项对应工具会明确报「跳过 / 不支持」。
// 可选覆盖：Unity 工程根目录下 ProjectSettings/UIReskinConfig.json，写哪个字段就覆盖哪个（JsonUtility 格式，字段名同 ReskinConfigData）。
// 常用：ReskinConfig.Dump() 打印生效值与来源；ReskinConfig.Redetect() 重新探测（换了字体目录 / 组件后）。
// 典型探测结果（DragonPlus LocalizeTextMeshProUGUI 体系）：组件 LocalizeTextMeshProUGUI、样式字段 m_Matrial、key 字段 Term，
//   字体 <fontRoot><语言>/LocaleFont_<语言> SDF.asset，样式材质 LocaleFont_<语言> SDF <样式>.mat。
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UIReskin
{
    [Serializable]
    public class ReskinConfigData
    {
        // ---- 本地化文字组件（ReskinProbe / ReskinHealth / ReskinTextStyle / ReskinMaterialStyle 用） ----
        /// 组件类型名（GetComponent(string) 用短名）。项目没有这类组件时填 ""，文字样式工具只改 TMP 本身。
        public string locTextType = "LocalizeTextMeshProUGUI";
        /// 组件上记录「样式材质名」的序列化字段（拼写照组件源码，FoodFlow1 体系就是 m_Matrial）。
        public string locMaterialField = "m_Matrial";
        /// 样式字段值的前缀：字段值 = 前缀 + 样式名。FoodFlow1 原版组件不带前缀（""）；BubbleWord3 FishPool 是 "SDF "（字段值 "SDF Shadow Blue"）。
        /// 按项目组件源码里查样式的规则填，填错会把字段写成运行时查不到的名字。
        public string locMaterialPrefix = "";
        /// 体检里「样式字段 ≠ 预制体当前材质的样式名」算问题（true）还是只作提示（false，默认）。
        /// FoodFlow1 体系 Awake 按当前材质名反推字段，两者必须一致 → 可在覆盖配置里设 true；
        /// 运行时按字段去字库找材质、预制体材质只是占位的项目（BubbleWord3 FishPool）设 true 会全是误报。
        public bool healthFieldMatchesMaterial = false;
        /// 组件上的多语言 key 字段（runtime.json 的 term 用），没有填 ""。
        public string locTermField = "Term";

        // ---- 语言字体与样式材质 ----
        /// 语言字体根目录（以 / 结尾）。每种语言一个子目录，目录名即语言名（De / En / Zh ...）。
        public string fontRoot = "Assets/Res/Fonts/";
        /// TMP 字体资产名模板（{lang} = 语言目录名）。用来识别语言目录、读 pointSize、从文字的字体反推语言。
        public string fontAssetPattern = "LocaleFont_{lang} SDF";
        /// 样式材质名模板（不含 .mat；{lang} 语言，{style} 样式名）。文件位于 fontRoot/{lang}/ 下。
        public string materialPattern = "LocaleFont_{lang} SDF {style}";
        /// 字体资产自带默认材质对应的样式名（不是独立 .mat 文件，体检时跳过）。
        public string defaultMaterialStyle = "Material";
        /// ReskinTextStyle 找不到节点当前样式时用来克隆的源样式名。
        public string fallbackSourceStyle = "Button OutLine Orange";
        /// ReskinMaterialStyle 的 materials[].source 缺省值。
        public string defaultCloneSource = "Shadow Blue";
        /// 文字里用于打印 dilate 的语言（报告只打一种语言，避免刷屏）。
        public string reportLang = "En";
        /// 描边像素校准系数：理论公式算出的描边在实际 SDF 字体上的粗细比。FoodFlow1 实测 1.54；新项目首次使用按 diff 报告「实测宽比」校准。
        public float outlineCalibration = 1.54f;

        // ---- 写完样式材质后的钩子 ----
        /// 写入新样式材质后调用的静态方法全名 "Namespace.Type.Method"（无参数，返回值转成字符串打进报告）。
        /// 典型用途：运行时按「样式清单资产」找材质的项目（如 BubbleWord3 FishPoolFontLibrary），新材质不进清单就找不到。
        /// 不需要就留空。方法写在项目自己的 Editor 程序集里即可（反射调用，不需要互相引用）。
        public string afterMaterialsHook = "";

        // ---- 截图 / 产物 ----
        /// 截图与报告的根目录（相对 Unity 工程根）。execute_code 禁止绝对路径，一律用相对路径。
        public string shotsRoot = "Library/ReskinShots";
        /// Pad 判定：高/宽 ≤ 该值视为 Pad（ReskinMatrix 选 _Pad 预制体）。
        public float padAspectMax = 1.605f;
        /// Pad 预制体后缀（项目没有 Pad 预制体就保持默认，调用时 prefabPad 传 null）。
        public string padSuffix = "_Pad";
    }

    public static class ReskinConfig
    {
        public const string RelPath = "ProjectSettings/UIReskinConfig.json";
        public const string CachePath = "Library/UIReskin/detected.json";
        static ReskinConfigData _data;
        static string _source = "";

        public static ReskinConfigData Data
        {
            get
            {
                if (_data == null) Reload();
                return _data;
            }
        }

        /// 读缓存的探测结果（没有就探测一次），再叠加 ProjectSettings/UIReskinConfig.json 覆盖
        public static string Reload()
        {
            var cache = Path.Combine(ReskinCore.ProjectRoot, CachePath);
            ReskinConfigData d = null;
            if (File.Exists(cache))
            {
                try { d = JsonUtility.FromJson<ReskinConfigData>(File.ReadAllText(cache)); _source = "探测缓存 " + CachePath; } catch { d = null; }
            }
            if (d == null) { d = Detect(out var log); _source = "自动探测（" + log + "）"; SaveCache(d); }
            var p = Path.Combine(ReskinCore.ProjectRoot, RelPath);
            if (File.Exists(p))
            {
                try { JsonUtility.FromJsonOverwrite(File.ReadAllText(p), d); _source += " + 覆盖 " + RelPath; }
                catch (Exception e) { Debug.LogError($"[RESKIN] {RelPath} 解析失败，忽略：{e.Message}"); }
            }
            if (!string.IsNullOrEmpty(d.fontRoot) && !d.fontRoot.EndsWith("/")) d.fontRoot += "/";
            _data = d;
            return "[RESKIN] 配置来源：" + _source;
        }

        /// 丢掉缓存重新探测
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

        // ---------- 自动探测 ----------
        static readonly string[] MatFieldNames = { "m_Matrial", "m_Material", "m_MaterialName", "materialName", "MaterialName" };
        static readonly string[] TermFieldNames = { "Term", "term", "m_Term", "Key", "key", "m_Key" };

        public static ReskinConfigData Detect(out string log)
        {
            var d = new ReskinConfigData();
            var notes = new System.Collections.Generic.List<string>();

            // 1) 本地化文字组件：优先 LocalizeTextMeshProUGUI；否则找带「材质名字符串字段」的 Component
            Type locT = null;
            foreach (var t in TypeCache.GetTypesDerivedFrom<Component>())
                if (t.Name == "LocalizeTextMeshProUGUI") { locT = t; break; }
            if (locT == null)
                foreach (var t in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                    if (!t.IsAbstract && FindStringField(t, MatFieldNames) != null && t.Namespace?.StartsWith("Unity") != true) { locT = t; break; }
            if (locT != null)
            {
                d.locTextType = locT.Name;
                d.locMaterialField = FindStringField(locT, MatFieldNames) ?? "";
                d.locTermField = FindStringField(locT, TermFieldNames) ?? "";
                notes.Add($"文字组件 {locT.FullName}.{d.locMaterialField}");
                if (string.IsNullOrEmpty(d.locMaterialField)) { d.locTextType = ""; notes.Add("该组件没有样式字段，按无组件处理"); }
            }
            else { d.locTextType = ""; d.locMaterialField = ""; d.locTermField = ""; notes.Add("无本地化文字组件"); }

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

        static string ReplaceOnce(string s, string a, string b) { int i = s.IndexOf(a, StringComparison.Ordinal); return i < 0 ? s : s.Substring(0, i) + b + s.Substring(i + a.Length); }

        public static string Dump() { _data = null; return Reload() + "\n" + JsonUtility.ToJson(Data, true); }

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
