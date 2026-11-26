// 新建 / 替换界面的资源接线：切图查找与导入、图集归属、资源管理登记（Resources / Addressables / YooAsset 自动识别）。
// 写入类方法默认 dryRun = true（只报告）；确认后传 false。都按「工程现有约定」做：照抄邻居的导入设置、现有图集的打包设置、同组收集器的规则。
// 用法（execute_code）：
//   UIReskin.ReskinAssets.EnsureAtlas("Assets/<模块>/Textures/UIXxx", dryRun: true)
//   UIReskin.ReskinAssets.EnsureLoadable(new[]{ "Assets/<模块>/Prefabs/UI/UIXxx.prefab" }, dryRun: true)
//   UIReskin.ReskinAssets.Survey()   // 只读：工程用的是哪种资源管理、有哪些图集、按文件夹还是按清单打包
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace UIReskin
{
    public static class ReskinAssets
    {
        // PSD 默认图层名：不是资源名，不去工程里找同名 Sprite（避免误绑到无关的图）
        static readonly Regex DefaultLayerName = new Regex(@"^(图层|组|形状|矩形|圆角矩形|椭圆|多边形|直线|Layer|Group|Rectangle|Rounded Rectangle|Ellipse|Shape|Polygon|Line)\s*\d*(\s*(拷贝|copy)\s*\d*)*$", RegexOptions.IgnoreCase);

        // ---------- 切图 ----------
        public static Sprite FindSprite(string name)
        {
            if (string.IsNullOrEmpty(name) || DefaultLayerName.IsMatch(name.Trim())) return null;
            var n = name.Trim();
            foreach (var g in AssetDatabase.FindAssets($"\"{n}\" t:Sprite"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("/_Tmp")) continue;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                    if (o is Sprite s && s.name == n) return s;
            }
            return null;
        }

        /// 新切图目录：从 prefab 目录往上 3 层找 Textures / Texture / Sprites / Images / Art 目录，放进 <它>/<prefab 名>；找不到就放 prefab 同级 <名>_Sprites
        public static string DefaultSpriteDir(string prefabPath)
        {
            var name = Path.GetFileNameWithoutExtension(prefabPath);
            var dir = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var cand = new[] { "Textures", "Texture", "Sprites", "Sprite", "Images", "Image", "Art" };
            var d = dir;
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(d) && d.StartsWith("Assets"); i++)
            {
                foreach (var c in cand)
                {
                    var p = d + "/" + c;
                    if (AssetDatabase.IsValidFolder(p)) return p + "/" + name;
                    if (AssetDatabase.IsValidFolder(p + "/UI")) return p + "/UI/" + name;
                }
                d = Path.GetDirectoryName(d)?.Replace('\\', '/');
            }
            return dir + "/" + name + "_Sprites";
        }

        static string SafeFile(string s) => Regex.Replace(string.IsNullOrEmpty(s) ? "sprite" : s.Trim(), @"[^\w一-鿿.-]+", "_");

        /// 把图层 PNG 导入成 Sprite：导入设置照抄目录里已有的贴图（没有就用 Sprite / 无 mipmap / alpha 透明的默认）
        public static Sprite ImportSprite(string pngAbs, string dir, string name, StringBuilder log)
        {
            EnsureFolder(dir);
            // 去重：目录里已有内容完全相同的图（PSD 里的「拷贝」图层常是同一张）→ 直接复用
            var bytes = File.ReadAllBytes(pngAbs);
            foreach (var existing in Directory.GetFiles(ReskinCore.Abs(dir), "*.png"))
            {
                var fi = new FileInfo(existing);
                if (fi.Length != bytes.Length || !bytes.SequenceEqual(File.ReadAllBytes(existing))) continue;
                var rel = dir + "/" + Path.GetFileName(existing);
                var reuse = AssetDatabase.LoadAllAssetsAtPath(rel).OfType<Sprite>().FirstOrDefault();
                if (reuse != null) { log?.AppendLine($"    复用同内容切图 {rel} ← {name}"); return reuse; }
            }
            var file = SafeFile(name) + ".png";
            var path = dir + "/" + file;
            for (int i = 2; File.Exists(ReskinCore.Abs(path)); i++) path = dir + "/" + SafeFile(name) + "_" + i + ".png";
            File.Copy(pngAbs, ReskinCore.Abs(path));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { log?.AppendLine("    ⚠ 导入失败 " + path); return null; }
            var neighbor = AssetDatabase.FindAssets("t:Texture2D", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p != path)
                .Select(p => AssetImporter.GetAtPath(p) as TextureImporter).FirstOrDefault(x => x != null && x.textureType == TextureImporterType.Sprite);
            if (neighbor != null)
            {
                var ts = new TextureImporterSettings(); neighbor.ReadTextureSettings(ts);
                ts.spriteMode = (int)SpriteImportMode.Single; imp.SetTextureSettings(ts);
                foreach (var plat in new[] { "Android", "iPhone", "Standalone", "WebGL" })
                {
                    var ps = neighbor.GetPlatformTextureSettings(plat);
                    if (ps != null && ps.overridden) imp.SetPlatformTextureSettings(ps);
                }
            }
            else
            {
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false; imp.alphaIsTransparency = true; imp.npotScale = TextureImporterNPOTScale.None;
            }
            imp.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        }

        static void EnsureFolder(string dir)
        {
            dir = dir.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(dir)) return;
            var parent = Path.GetDirectoryName(dir).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
        }

        // ---------- 图集 ----------
        static List<(string path, SpriteAtlas atlas)> AllAtlases()
        {
            var res = new List<(string, SpriteAtlas)>();
            foreach (var g in AssetDatabase.FindAssets("t:SpriteAtlas"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(p);
                if (a != null) res.Add((p, a));
            }
            return res;
        }

        /// 切图目录是否已被某个图集收进去；没有就按工程现有图集的设置新建一个（按文件夹打包），工程完全不用图集时不建
        public static string EnsureAtlas(string spriteDir, bool dryRun = true)
        {
            spriteDir = spriteDir.TrimEnd('/');
            var sb = new StringBuilder($"[RESKIN] 图集 {(dryRun ? "DRY-RUN" : "APPLY")} {spriteDir}\n");
            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { spriteDir }).Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>()).ToList();
            if (sprites.Count == 0) return sb.AppendLine("  目录里没有 Sprite").ToString();
            var atlases = AllAtlases();
            if (atlases.Count == 0) return sb.AppendLine("  工程里没有任何 SpriteAtlas：沿用工程做法不建图集（需要时再按项目约定建）").ToString();
            var covering = atlases.Where(a => sprites.All(s => a.atlas.CanBindTo(s))).ToList();
            var partial = atlases.Where(a => sprites.Any(s => a.atlas.CanBindTo(s)) && !covering.Contains(a)).ToList();
            if (covering.Count > 0) return sb.AppendLine($"  已被图集收录：{string.Join("、", covering.Select(a => a.path))}（{sprites.Count} 张）").ToString();
            if (partial.Count > 0)
            {
                // 部分收录 = 该图集按清单打包：把剩下的图加进去
                var a = partial[0];
                var miss = sprites.Where(s => !a.atlas.CanBindTo(s)).ToList();
                sb.AppendLine($"  图集 {a.path} 只收了一部分，补 {miss.Count} 张");
                if (!dryRun && a.path.EndsWith(".spriteatlas"))
                {
                    a.atlas.Add(miss.Cast<UnityEngine.Object>().ToArray());
                    SpriteAtlasUtility.PackAtlases(new[] { a.atlas }, EditorUserBuildSettings.activeBuildTarget);
                    AssetDatabase.SaveAssets();
                }
                else if (!dryRun) sb.AppendLine("  ⚠ V2 图集请在 Inspector 里加（或改用按文件夹打包）");
                return sb.ToString();
            }
            // 新建：设置照抄工程里最常见的那种图集；按项目图集版本选 V1 / V2
            bool v2 = EditorSettings.spritePackerMode.ToString().Contains("V2");
            var template = atlases.FirstOrDefault(x => v2 ? x.path.EndsWith(".spriteatlasv2") : x.path.EndsWith(".spriteatlas"));
            var path = spriteDir + (v2 ? ".spriteatlasv2" : ".spriteatlas");
            sb.AppendLine($"  未被任何图集收录 → 新建 {path}（按文件夹打包，设置照抄 {(template.atlas != null ? template.path : "Unity 默认")}）");
            if (dryRun) return sb.ToString();
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(spriteDir);
            if (v2)
            {
                var asset = new SpriteAtlasAsset();
                asset.Add(new[] { folder });
                SpriteAtlasAsset.Save(asset, path);
                AssetDatabase.ImportAsset(path);
                var imp = AssetImporter.GetAtPath(path) as SpriteAtlasImporter;
                var timp = template.atlas != null ? AssetImporter.GetAtPath(template.path) as SpriteAtlasImporter : null;
                if (imp != null && timp != null)
                {
                    imp.packingSettings = timp.packingSettings; imp.textureSettings = timp.textureSettings;
                    foreach (var plat in new[] { "Android", "iPhone", "Standalone", "WebGL" }) { var ps = timp.GetPlatformSettings(plat); if (ps != null && ps.overridden) imp.SetPlatformSettings(ps); }
                    imp.SaveAndReimport();
                }
            }
            else
            {
                var atlas = new SpriteAtlas();
                if (template.atlas != null)
                {
                    atlas.SetPackingSettings(template.atlas.GetPackingSettings());
                    atlas.SetTextureSettings(template.atlas.GetTextureSettings());
                    foreach (var plat in new[] { "Android", "iPhone", "Standalone", "WebGL" }) { var ps = template.atlas.GetPlatformSettings(plat); if (ps != null && ps.overridden) atlas.SetPlatformSettings(ps); }
                }
                AssetDatabase.CreateAsset(atlas, path);
                atlas.Add(new[] { folder });
                SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
                AssetDatabase.SaveAssets();
            }
            return sb.AppendLine("  已新建并打包").ToString();
        }

        // ---------- 资源管理 ----------
        static Type FindType(string full) { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType(full); if (t != null) return t; } return null; }
        static object Get(object o, string name)
        {
            if (o == null) return null;
            var t = o as Type ?? o.GetType(); var inst = o is Type ? null : o;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var p = t.GetProperty(name, F); if (p != null) return p.GetValue(inst);
            var f = t.GetField(name, F); return f?.GetValue(inst);
        }
        static void Set(object o, string name, object v)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = o.GetType(); var p = t.GetProperty(name, F);
            if (p != null && p.CanWrite) { p.SetValue(o, v); return; }
            t.GetField(name, F)?.SetValue(o, v);
        }

        public static string Survey()
        {
            var sb = new StringBuilder("[RESKIN] 资源管理普查\n");
            var addr = FindType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
            var addrSettings = addr != null ? Get(addr, "Settings") : null;
            sb.AppendLine($"  Addressables：{(addrSettings != null ? "启用" : addr != null ? "已安装未建配置" : "无")}");
            var yoo = FindType("YooAsset.Editor.AssetBundleCollectorSettingData");
            if (yoo != null)
            {
                int n = 0; var lines = new List<string>();
                foreach (var pkg in (IEnumerable)Get(Get(yoo, "Setting"), "Packages") ?? new object[0])
                    foreach (var grp in (IEnumerable)Get(pkg, "Groups") ?? new object[0])
                        foreach (var col in (IEnumerable)Get(grp, "Collectors") ?? new object[0]) { n++; if (lines.Count < 12) lines.Add($"{Get(pkg, "PackageName")}/{Get(grp, "GroupName")}: {Get(col, "CollectPath")} [{Get(col, "CollectorType")}, {Get(col, "PackRuleName")}]"); }
                sb.AppendLine($"  YooAsset：收集器 {n} 个"); foreach (var l in lines) sb.AppendLine("    " + l);
            }
            else sb.AppendLine("  YooAsset：无");
            var atl = AllAtlases();
            sb.AppendLine($"  图集：{atl.Count} 个（图集模式 {EditorSettings.spritePackerMode}）");
            foreach (var a in atl.Take(10))
            {
                var packs = a.atlas.GetPackables();
                sb.AppendLine($"    {a.path}：packables {packs.Length}（文件夹 {packs.Count(x => x is DefaultAsset)}，单图 {packs.Count(x => !(x is DefaultAsset))}）");
            }
            return sb.ToString();
        }

        /// 确保资源能在运行时加载：Resources 下 → 直接可用；Addressables → 建条目（组取同目录 / 上级目录已有条目所在组，否则默认组）；
        /// YooAsset → 已被收集器覆盖就跳过，否则照抄路径最接近的收集器（同组、同规则）新建一个收集该目录的收集器。
        public static string EnsureLoadable(string[] assetPaths, bool dryRun = true)
        {
            var sb = new StringBuilder($"[RESKIN] 资源登记 {(dryRun ? "DRY-RUN" : "APPLY")}\n");
            bool any = false;
            // Addressables
            var addr = FindType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
            var settings = addr != null ? Get(addr, "Settings") : null;
            // YooAsset
            var yoo = FindType("YooAsset.Editor.AssetBundleCollectorSettingData");
            var yooSetting = yoo != null ? Get(yoo, "Setting") : null;
            foreach (var path in assetPaths)
            {
                if (path.Contains("/Resources/")) { sb.AppendLine($"  {path}：在 Resources 下，直接可加载"); any = true; continue; }
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) { sb.AppendLine($"  ⚠ {path} 不存在"); continue; }
                if (settings != null)
                {
                    any = true;
                    var find = settings.GetType().GetMethod("FindAssetEntry", new[] { typeof(string) });
                    if (find?.Invoke(settings, new object[] { guid }) != null) { sb.AppendLine($"  {path}：Addressables 已有条目"); continue; }
                    object group = null; int best = -1;
                    foreach (var g in (IEnumerable)Get(settings, "groups") ?? new object[0])
                        foreach (var e in (IEnumerable)Get(g, "entries") ?? new object[0])
                        {
                            var ap = Get(e, "AssetPath") as string; if (ap == null) continue;
                            int common = CommonDirDepth(ap, path);
                            if (common > best) { best = common; group = g; }
                        }
                    group = group ?? Get(settings, "DefaultGroup");
                    sb.AppendLine($"  {path}：Addressables 新建条目 → 组 {Get(group, "Name")}");
                    if (!dryRun)
                    {
                        var m = settings.GetType().GetMethods().FirstOrDefault(x => x.Name == "CreateOrMoveEntry" && x.GetParameters().Length >= 2);
                        var ps = m.GetParameters();
                        var args = new object[ps.Length]; args[0] = guid; args[1] = group;
                        for (int i = 2; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType == typeof(bool) ? (object)false : null);
                        m.Invoke(settings, args);
                        EditorUtility.SetDirty((UnityEngine.Object)settings);
                    }
                    continue;
                }
                if (yooSetting != null)
                {
                    any = true;
                    object bestCol = null, bestGrp = null; int bestDepth = -1; bool covered = false;
                    foreach (var pkg in (IEnumerable)Get(yooSetting, "Packages") ?? new object[0])
                        foreach (var grp in (IEnumerable)Get(pkg, "Groups") ?? new object[0])
                            foreach (var col in (IEnumerable)Get(grp, "Collectors") ?? new object[0])
                            {
                                var cp = (Get(col, "CollectPath") as string ?? "").TrimEnd('/');
                                if (cp.Length == 0) continue;
                                if (path == cp || path.StartsWith(cp + "/")) { covered = true; sb.AppendLine($"  {path}：YooAsset 已被收集器 {cp}（{Get(grp, "GroupName")}）覆盖"); break; }
                                int d = CommonDirDepth(cp + "/x", path);
                                if (d > bestDepth) { bestDepth = d; bestCol = col; bestGrp = grp; }
                            }
                    if (covered) continue;
                    var dir = Path.GetDirectoryName(path).Replace('\\', '/');
                    if (bestCol == null) { sb.AppendLine($"  ⚠ {path}：YooAsset 没有可参照的收集器，按项目约定手工加"); continue; }
                    sb.AppendLine($"  {path}：YooAsset 未覆盖 → 在组 {Get(bestGrp, "GroupName")} 新建收集器 {dir}（规则照抄 {Get(bestCol, "CollectPath")}：{Get(bestCol, "CollectorType")} / {Get(bestCol, "AddressRuleName")} / {Get(bestCol, "PackRuleName")} / {Get(bestCol, "FilterRuleName")}）");
                    if (!dryRun)
                    {
                        var col = Activator.CreateInstance(bestCol.GetType());
                        foreach (var f in new[] { "CollectorType", "AddressRuleName", "PackRuleName", "FilterRuleName", "AssetTags", "UserData" })
                        { var v = Get(bestCol, f); if (v != null) Set(col, f, v); }
                        Set(col, "CollectPath", dir);
                        var guidProp = AssetDatabase.AssetPathToGUID(dir);
                        Set(col, "CollectorGUID", guidProp);
                        var create = yoo.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(m2 => m2.Name == "CreateCollector" && m2.GetParameters().Length == 2);
                        if (create != null) create.Invoke(null, new[] { bestGrp, col });
                        else ((IList)Get(bestGrp, "Collectors")).Add(col);
                        yoo.GetMethod("SaveFile", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
                    }
                    continue;
                }
            }
            if (!any) sb.AppendLine("  没识别到 Addressables / YooAsset，且资源不在 Resources 下：按项目自己的加载方式（自研打包配置 / 资源表）登记");
            if (!dryRun) AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static int CommonDirDepth(string a, string b)
        {
            var x = a.Split('/'); var y = b.Split('/'); int i = 0;
            while (i < x.Length - 1 && i < y.Length - 1 && x[i] == y[i]) i++;
            return i;
        }
    }
}
