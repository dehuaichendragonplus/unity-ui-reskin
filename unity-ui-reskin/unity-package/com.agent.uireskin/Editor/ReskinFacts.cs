// 项目事实：仓库里的 .uireskin/facts.json（代理调研核实后写入，见 skill 的 references 与 facts.md）。
// 本工具包不内置任何项目 / 框架知识，凡是因项目而异的值都从这里读；读不到就当「未调研」，由调用方给候选并提示先调研。
// 查找：从 Unity 工程根目录往上逐级找 .uireskin/facts.json（到文件系统根为止），环境变量 UIRESKIN_FACTS 可直接指定文件。
using System;
using System.IO;
using UnityEngine;

namespace UIReskin
{
    [Serializable] public class FactsUi { public float refWidth, refHeight; public string match, prefabDir, spriteDir, defaultFrame; }
    [Serializable] public class FactsMatMap { public string from, to; }
    [Serializable] public class FactsLocalization
    {
        public string component, idField, keyField, titleFont, materialMapFontPrefix, fixPolicy;
        public string[] requiredFonts, forbidUnder;
        public FactsMatMap[] materialMap;
    }
    [Serializable] public class FactsFramework
    {
        public string viewBase, bindingAttribute, bindingPathMember, bindingOptionalMember, bindingComponentMember, descriptorAttribute, descriptorKeyMember, open, close, register;
        public string[] referenceViews;
    }
    [Serializable] public class FactsRoot
    {
        public int schema; public string updated;
        public FactsUi ui = new FactsUi();
        public FactsLocalization localization = new FactsLocalization();
        public FactsFramework framework = new FactsFramework();
    }

    public static class ReskinFacts
    {
        static string _path; static string _json; static FactsRoot _root; static DateTime _stamp;

        /// 事实文件路径；没有返回 null
        public static string Path
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("UIRESKIN_FACTS");
                if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
                var dir = new DirectoryInfo(ReskinCore.ProjectRoot);
                for (int i = 0; dir != null && i < 6; i++, dir = dir.Parent)
                {
                    var p = System.IO.Path.Combine(dir.FullName, ".uireskin", "facts.json");
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        /// 原始 JSON（文件改了自动重读）；没有返回 null
        public static string Json
        {
            get
            {
                var p = Path;
                if (p == null) { _path = null; _json = null; _root = null; return null; }
                var t = File.GetLastWriteTimeUtc(p);
                if (p != _path || t != _stamp) { _path = p; _stamp = t; _json = File.ReadAllText(p); _root = null; }
                return _json;
            }
        }

        public static FactsRoot Root
        {
            get
            {
                var j = Json;
                if (j == null) return new FactsRoot();
                if (_root == null)
                {
                    try { _root = JsonUtility.FromJson<FactsRoot>(j) ?? new FactsRoot(); }
                    catch (Exception e) { Debug.LogError("[RESKIN] facts.json 解析失败：" + e.Message); _root = new FactsRoot(); }
                }
                return _root;
            }
        }

        public static bool Exists => Path != null;

        /// 取 JSON 里某个顶层对象的原文（如 "text"），给 JsonUtility.FromJsonOverwrite 只覆盖出现过的字段用；没有返回 null
        public static string Section(string key)
        {
            var j = Json; if (j == null) return null;
            int k = j.IndexOf("\"" + key + "\"", StringComparison.Ordinal); if (k < 0) return null;
            int i = j.IndexOf('{', k); if (i < 0) return null;
            int depth = 0; bool str = false;
            for (int p = i; p < j.Length; p++)
            {
                char c = j[p];
                if (str) { if (c == '\\') p++; else if (c == '"') str = false; continue; }
                if (c == '"') str = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return j.Substring(i, p - i + 1);
            }
            return null;
        }

        public static string Describe() => Exists ? $"项目事实 {Path}（更新 {Root.updated}）" : "项目事实：无（.uireskin/facts.json），用到哪项调研哪项，核实后写入";
    }
}
