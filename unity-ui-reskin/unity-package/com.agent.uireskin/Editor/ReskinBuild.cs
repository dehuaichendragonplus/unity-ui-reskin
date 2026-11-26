// 新建界面：layout.json（scripts/spec_to_layout.py 生成）→ 骨架 prefab，并按推断的功能（role）建好 UGUI 控件。
// 生成内容：
//   - 节点层级与 RectTransform（锚点按 layout 的 anchor：stretch / top|middle|bottom - left|center|right|stretchX）
//   - 图片：Image；切图按「工程里同名 Sprite → 导入 layout 里的图层 PNG → 品红半透明占位」顺序解析（导入由 ReskinAssets 负责，目录 / 导入设置对齐邻居）
//   - 文字：TextMeshProUGUI（字号、颜色、字距、对齐、顶点渐变、不换行），字体：项目事实 text.defaultFont → 工程 prefab 里引用最多的 TMP 字体 → TMP 默认字体
//   - 控件：button / close → Button；progress → Slider（不可拖，fill 子节点转拉伸）或 Filled Image；toggle → Toggle；
//           list → ScrollRect + Viewport(RectMask2D) + Content + Vertical/Horizontal/Grid LayoutGroup（子项保持原尺寸，第一项是模板）
//   - 隐藏状态组的节点默认不激活
// 用法（execute_code，编辑器非 Play）：
//   UIReskin.ReskinBuild.Build("Library/ReskinShots/<界面>/layout.json", dryRun: true)    // 只出计划：节点数、切图命中 / 导入 / 占位、控件
//   UIReskin.ReskinBuild.Build("Library/ReskinShots/<界面>/layout.json", dryRun: false)   // 写入 prefab（已存在时拒绝，overwrite: true 才覆盖）
// 之后：ReskinTextStyle.Apply(styles.json) 落描边 / 投影；ReskinAssets.EnsureAtlas / EnsureLoadable 配图集与资源管理；
//       Play 中 ReskinMatrix.Snap(name, () => ReskinBuild.PreviewCo(prefab), ...) 与效果图对齐。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UIReskin
{
    public static class ReskinBuild
    {
        [Serializable] public class LNode
        {
            public string name, path, parent, kind, anchor, role, sprite, png, text, align, listLayout, state, label;
            public float[] rect, fill, gradientTop, gradientBottom;
            public float fontSize, charSpacing, opacity = 1;
            public bool active = true, button;
        }
        [Serializable] public class Layout
        {
            public string prefab, font, spriteDir;
            public float refWidth, refHeight;
            public List<LNode> nodes = new List<LNode>();
        }

        public static string Build(string layoutPath, bool dryRun = true, bool overwrite = false)
        {
            if (Application.isPlaying && !dryRun) return "请先退出 Play 再生成 prefab";
            var L = JsonUtility.FromJson<Layout>(File.ReadAllText(ReskinCore.Abs(layoutPath)));
            if (string.IsNullOrEmpty(L.prefab) || !L.prefab.StartsWith("Assets/") || !L.prefab.EndsWith(".prefab")) return "layout.prefab 必须是 Assets/...prefab：" + L.prefab;
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(L.prefab) != null;
            if (exists && !overwrite && !dryRun) return $"{L.prefab} 已存在，拒绝覆盖（确认要覆盖传 overwrite: true）";
            var sb = new StringBuilder($"[RESKIN] 生成 {(dryRun ? "DRY-RUN" : "APPLY")} {L.prefab}  参考 {L.refWidth}x{L.refHeight}  节点 {L.nodes.Count}{(exists ? "（已存在）" : "")}\n");
            var font = PickFont(L.font, sb);
            var spriteDir = string.IsNullOrEmpty(L.spriteDir) ? ReskinAssets.DefaultSpriteDir(L.prefab) : L.spriteDir.TrimEnd('/');

            // 1) 切图解析（dry-run 只统计）
            var sprites = new Dictionary<string, Sprite>();
            int hit = 0, imported = 0, placeholder = 0;
            var placeholders = new List<string>();
            foreach (var n in L.nodes.Where(x => x.kind == "image"))
            {
                var sp = ReskinAssets.FindSprite(n.sprite);
                if (sp != null) { hit++; sprites[n.path] = sp; continue; }
                if (!string.IsNullOrEmpty(n.png) && File.Exists(n.png))
                {
                    imported++;
                    if (!dryRun) { sp = ReskinAssets.ImportSprite(n.png, spriteDir, n.sprite, sb); if (sp != null) sprites[n.path] = sp; }
                    continue;
                }
                placeholder++; placeholders.Add(n.path);
            }
            sb.AppendLine($"  切图：工程同名命中 {hit}，导入图层 PNG {imported}（→ {spriteDir}），占位 {placeholder}");
            foreach (var p in placeholders.Take(30)) sb.AppendLine("    占位 " + p);
            var roles = L.nodes.Where(x => !string.IsNullOrEmpty(x.role) && x.role != "staticText" && x.role != "dynamicText").GroupBy(x => x.role).Select(g => $"{g.Key} {g.Count()}");
            sb.AppendLine("  控件：" + string.Join("，", roles));
            if (dryRun) return sb.AppendLine("（未写入）").ToString();
            if (imported > 0) AssetDatabase.Refresh();

            // 2) 建层级
            var root = new GameObject(Path.GetFileNameWithoutExtension(L.prefab), typeof(RectTransform));
            try
            {
                var rrt = (RectTransform)root.transform;
                rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.offsetMin = rrt.offsetMax = Vector2.zero; rrt.pivot = new Vector2(0.5f, 0.5f);
                var rootRect = new[] { 0f, 0f, L.refWidth, L.refHeight };
                var go = new Dictionary<string, GameObject> { [""] = root };
                var rectOf = new Dictionary<string, float[]> { [""] = rootRect };
                foreach (var n in L.nodes)
                {
                    var parent = go.TryGetValue(n.parent ?? "", out var pg) ? pg : root;
                    var g = new GameObject(n.name, typeof(RectTransform));
                    g.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0;
                    g.transform.SetParent(parent.transform, false);
                    go[n.path] = g; rectOf[n.path] = n.rect;
                    var r = n.rect;
                    if (n.kind == "text") r = TextRect(n);
                    Place((RectTransform)g.transform, r, rectOf.TryGetValue(n.parent ?? "", out var pr) ? pr : rootRect, n.anchor);
                    if (n.kind == "image") MakeImage(g, n, sprites.TryGetValue(n.path, out var sp) ? sp : null);
                    else if (n.kind == "text") MakeText(g, n, font);
                    if (!n.active) g.SetActive(false);
                }
                // 3) 控件（自底向上：先子后父，列表最后改结构）
                foreach (var n in L.nodes.AsEnumerable().Reverse())
                {
                    if (string.IsNullOrEmpty(n.role) || !go.TryGetValue(n.path, out var g)) continue;
                    switch (n.role)
                    {
                        case "button": case "close": MakeButton(g); break;
                        case "progress": MakeProgress(g, n); break;
                        case "toggle": MakeToggle(g); break;
                    }
                }
                foreach (var n in L.nodes.Where(x => x.role == "list"))
                    if (go.TryGetValue(n.path, out var g)) MakeList(g, n.listLayout, sb);

                Directory.CreateDirectory(Path.GetDirectoryName(ReskinCore.Abs(L.prefab)));
                PrefabUtility.SaveAsPrefabAsset(root, L.prefab, out bool ok);
                sb.AppendLine(ok ? $"已生成 {L.prefab}" : "⚠ 保存失败");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            sb.AppendLine("下一步：ReskinTextStyle.Apply(styles.json) → ReskinAssets.EnsureAtlas(spriteDir) / EnsureLoadable(prefab) → Play 预览对齐 → 按 bindings.json 写业务代码");
            return sb.ToString();
        }

        // ---------- 布局 ----------
        static float[] TextRect(LNode n)
        {
            // PSD 文字框是字形紧框：放宽避免换行 / 裁字，按对齐方式保持对应边
            var r = n.rect; float fs = Mathf.Max(1, n.fontSize);
            float w = Mathf.Max(r[2] * 1.15f, r[2] + fs), h = Mathf.Max(r[3], fs * 1.35f);
            float x = n.align == "left" ? r[0] : n.align == "right" ? r[0] + r[2] - w : r[0] + r[2] / 2 - w / 2;
            float y = r[1] + r[3] / 2 - h / 2;
            return new[] { x, y, w, h };
        }

        /// r / p：参考画布坐标（左上原点、y 向下）
        static void Place(RectTransform rt, float[] r, float[] p, string anchor)
        {
            rt.pivot = new Vector2(0.5f, 0.5f);
            float rcx = r[0] + r[2] / 2, rcy = r[1] + r[3] / 2;
            anchor = string.IsNullOrEmpty(anchor) ? "middle-center" : anchor;
            if (anchor == "stretch")
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(r[0] - p[0], (p[1] + p[3]) - (r[1] + r[3]));
                rt.offsetMax = new Vector2((r[0] + r[2]) - (p[0] + p[2]), -(r[1] - p[1]));
                return;
            }
            var parts = anchor.Split('-');
            float ay = parts[0] == "top" ? 1 : parts[0] == "bottom" ? 0 : 0.5f;
            string hx = parts.Length > 1 ? parts[1] : "center";
            float axMin, axMax;
            if (hx == "stretchX") { axMin = 0; axMax = 1; }
            else { axMin = axMax = hx == "left" ? 0 : hx == "right" ? 1 : 0.5f; }
            rt.anchorMin = new Vector2(axMin, ay); rt.anchorMax = new Vector2(axMax, ay);
            float Ax = p[0] + (axMin + axMax) / 2 * p[2];
            float Ay = p[1] + (1 - ay) * p[3];
            rt.sizeDelta = new Vector2(hx == "stretchX" ? r[2] - p[2] : r[2], r[3]);
            rt.anchoredPosition = new Vector2(rcx - Ax, -(rcy - Ay));
        }

        // ---------- 组件 ----------
        static Color Col(float[] c, float a = 1f) => c == null || c.Length < 3 ? new Color(1, 1, 1, a) : new Color(c[0] / 255f, c[1] / 255f, c[2] / 255f, a);

        static void MakeImage(GameObject g, LNode n, Sprite sp)
        {
            var img = g.AddComponent<Image>();
            img.raycastTarget = false;
            if (sp != null) { img.sprite = sp; img.color = new Color(1, 1, 1, Mathf.Clamp01(n.opacity <= 0 ? 1 : n.opacity)); if (sp.border != Vector4.zero) img.type = Image.Type.Sliced; }
            else img.color = new Color(1f, 0f, 1f, 0.25f);   // 占位：品红半透明，一眼能看出缺图
            if (n.role == "progress") { img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillAmount = 1; }
        }

        static void MakeText(GameObject g, LNode n, TMP_FontAsset font)
        {
            var t = g.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = n.text ?? "";
            t.fontSize = n.fontSize > 0 ? n.fontSize : 30;
            t.characterSpacing = n.charSpacing;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = n.align == "left" ? TextAlignmentOptions.MidlineLeft : n.align == "right" ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Center;
            SetNoWrap(t);
            if (n.gradientTop != null && n.gradientTop.Length >= 3 && n.gradientBottom != null && n.gradientBottom.Length >= 3)
            {
                t.color = Color.white; t.enableVertexGradient = true;
                t.colorGradient = new VertexGradient(Col(n.gradientTop), Col(n.gradientTop), Col(n.gradientBottom), Col(n.gradientBottom));
            }
            else t.color = Col(n.fill);
        }

        static void SetNoWrap(TMP_Text t)
        {
            // TMP 3.2+ / ugui 2.0 用 textWrappingMode，旧版用 enableWordWrapping（反射兼容两代）
            var p = t.GetType().GetProperty("textWrappingMode");
            if (p != null) { p.SetValue(t, Enum.Parse(p.PropertyType, "NoWrap")); return; }
            t.GetType().GetProperty("enableWordWrapping")?.SetValue(t, false);
        }

        static Graphic FirstGraphic(GameObject g) =>
            g.GetComponent<Graphic>() ?? g.GetComponentsInChildren<Image>(true).FirstOrDefault();

        static void MakeButton(GameObject g)
        {
            var graphic = g.GetComponent<Graphic>();
            if (graphic == null)
            {
                // 组：加一张透明图接射线（尺寸 = 组），目标图形用组里第一张图
                var hit = g.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0); graphic = hit;
            }
            graphic.raycastTarget = true;
            var b = g.GetComponent<Button>() ?? g.AddComponent<Button>();
            b.targetGraphic = g.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.gameObject != g && i.sprite != null) ?? graphic;
            b.transition = Selectable.Transition.ColorTint;
        }

        static void MakeProgress(GameObject g, LNode n)
        {
            if (n.kind == "image") return;   // 单图：MakeImage 已设成 Filled
            var imgs = g.GetComponentsInChildren<Image>(true).Where(i => i.gameObject != g).ToList();
            if (imgs.Count < 2) return;
            var bg = imgs.OrderByDescending(i => i.rectTransform.rect.width * i.rectTransform.rect.height).First();
            var fill = imgs.FirstOrDefault(i => i != bg && System.Text.RegularExpressions.Regex.IsMatch(i.name, "(fill|bar|进度条|填充)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                       ?? imgs.Where(i => i != bg).OrderByDescending(i => i.rectTransform.rect.width).First();
            // fill 转成相对父节点的四边拉伸（Slider 只改 anchorMax.x，保留四边内缩）
            var frt = fill.rectTransform; var prt = (RectTransform)frt.parent;
            var pr = prt.rect; var fr = frt.rect; Vector2 fpos = frt.localPosition;
            float left = (fpos.x + fr.xMin) - pr.xMin, right = pr.xMax - (fpos.x + fr.xMax);
            float bottom = (fpos.y + fr.yMin) - pr.yMin, top = pr.yMax - (fpos.y + fr.yMax);
            // PSD 里画的填充长度代表「当前值」而不是内缩：左右用同样的内缩（满值时填满整条），画的长度换算成初始 value
            float inner = Mathf.Max(1, pr.width - 2 * left);
            float value = Mathf.Clamp01(fr.width / inner);
            frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(value, 1);
            frt.offsetMin = new Vector2(left, bottom); frt.offsetMax = new Vector2(-left, -top);
            var s = g.AddComponent<Slider>();
            s.interactable = false; s.transition = Selectable.Transition.None; s.navigation = new Navigation { mode = Navigation.Mode.None };
            s.fillRect = frt; s.direction = Slider.Direction.LeftToRight; s.minValue = 0; s.maxValue = 1; s.SetValueWithoutNotify(value);
        }

        static void MakeToggle(GameObject g)
        {
            var imgs = g.GetComponentsInChildren<Image>(true).ToList();
            if (imgs.Count == 0) return;
            var on = imgs.FirstOrDefault(i => System.Text.RegularExpressions.Regex.IsMatch(i.name, "(on|check|勾|选中|mark)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ?? imgs.Last();
            var bgImg = imgs.FirstOrDefault(i => i != on) ?? on;
            bgImg.raycastTarget = true;
            var t = g.AddComponent<Toggle>();
            t.targetGraphic = bgImg; t.graphic = on; t.isOn = true;
        }

        static void MakeList(GameObject g, string layout, StringBuilder sb)
        {
            var grt = (RectTransform)g.transform;
            var kids = grt.Cast<Transform>().Select(x => (RectTransform)x).ToList();
            if (kids.Count == 0) return;
            // 背景：覆盖整组 ≥ 80% 的图片留在外面
            var gr = grt.rect; float ga = Mathf.Max(1, gr.width * gr.height);
            var backs = kids.Where(k => k.GetComponent<Image>() != null && k.childCount == 0 && k.rect.width * k.rect.height >= 0.8f * ga).ToList();
            var items = kids.Except(backs).ToList();
            if (items.Count == 0) return;
            bool vertical = layout != "horizontal";
            // 视口
            var vp = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            vp.SetParent(grt, false); vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.offsetMin = vp.offsetMax = Vector2.zero; vp.pivot = new Vector2(0.5f, 0.5f);
            var hitImg = vp.gameObject.AddComponent<Image>(); hitImg.color = new Color(1, 1, 1, 0);   // 拖动接射线
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(vp, false);
            content.anchorMin = vertical ? new Vector2(0, 1) : new Vector2(0, 0); content.anchorMax = vertical ? new Vector2(1, 1) : new Vector2(0, 1);
            content.pivot = vertical ? new Vector2(0.5f, 1) : new Vector2(0, 0.5f);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            // 子项按原位置排序后移进 Content，间距 = 相邻子项间隙中位数
            var ordered = vertical ? items.OrderByDescending(k => k.localPosition.y).ToList() : items.OrderBy(k => k.localPosition.x).ToList();
            var gaps = new List<float>();
            for (int i = 1; i < ordered.Count; i++)
            {
                var a = ordered[i - 1]; var b = ordered[i];
                gaps.Add(vertical ? (a.localPosition.y + a.rect.yMin) - (b.localPosition.y + b.rect.yMax) : (b.localPosition.x + b.rect.xMin) - (a.localPosition.x + a.rect.xMax));
            }
            float spacing = gaps.Count > 0 ? gaps.OrderBy(x => x).ElementAt(gaps.Count / 2) : 0;
            var first = ordered[0];
            float padStart = vertical ? gr.yMax - (first.localPosition.y + first.rect.yMax) : (first.localPosition.x + first.rect.xMin) - gr.xMin;
            foreach (var k in ordered) k.SetParent(content, true);
            if (layout == "grid")
            {
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = first.rect.size; grid.spacing = new Vector2(Mathf.Max(0, spacing), Mathf.Max(0, spacing));
                grid.padding = new RectOffset(0, 0, Mathf.RoundToInt(Mathf.Max(0, padStart)), 0);
            }
            else
            {
                HorizontalOrVerticalLayoutGroup lg = vertical ? (HorizontalOrVerticalLayoutGroup)content.gameObject.AddComponent<VerticalLayoutGroup>() : content.gameObject.AddComponent<HorizontalLayoutGroup>();
                lg.spacing = spacing; lg.childAlignment = vertical ? TextAnchor.UpperCenter : TextAnchor.MiddleLeft;
                lg.childControlWidth = lg.childControlHeight = false; lg.childForceExpandWidth = lg.childForceExpandHeight = false;
                int pad = Mathf.RoundToInt(Mathf.Max(0, padStart));
                lg.padding = vertical ? new RectOffset(0, 0, pad, pad) : new RectOffset(pad, pad, 0, 0);
            }
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            if (vertical) fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; else fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = g.AddComponent<ScrollRect>();
            sr.viewport = vp; sr.content = content; sr.vertical = vertical; sr.horizontal = !vertical;
            sr.movementType = ScrollRect.MovementType.Elastic; sr.scrollSensitivity = 30;
            // 列表项里的图片保证被遮罩裁剪
            foreach (var m in content.GetComponentsInChildren<MaskableGraphic>(true)) m.maskable = true;
            sb.AppendLine($"  列表 {g.name}：{(layout ?? "vertical")}，子项 {ordered.Count}（模板 = {first.name}），间距 {spacing:F0}，背景留在外面 {backs.Count}");
        }

        // ---------- 字体 ----------
        static TMP_FontAsset PickFont(string name, StringBuilder sb)
        {
            TMP_FontAsset Find(string n)
            {
                if (string.IsNullOrEmpty(n)) return null;
                foreach (var g in AssetDatabase.FindAssets($"\"{n}\" t:TMP_FontAsset"))
                {
                    var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g));
                    if (f != null && f.name == n) return f;
                }
                return null;
            }
            var font = Find(name);
            string why = "layout.font（项目事实 text.defaultFont）";
            if (font == null && !string.IsNullOrEmpty(ReskinConfig.Data.defaultFont)) { font = Find(ReskinConfig.Data.defaultFont); why = "ReskinConfig.defaultFont（事实或 prefab 里最常用的字体）"; }
            if (font == null) { try { font = TMP_Settings.defaultFontAsset; why = "TMP 默认字体"; } catch { } }   // 没导入 TMP Essentials 时 TMP_Settings 为空
            if (font == null) { var any = AssetDatabase.FindAssets("t:TMP_FontAsset").FirstOrDefault(); if (any != null) { font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(any)); why = "工程里第一个 TMP 字体"; } }
            if (font == null) sb.AppendLine("  ⚠ 工程里没有任何 TMP 字体（未导入 TMP Essentials？）：文字节点先不带字体，导入后重新生成或手动指定");
            sb.AppendLine($"  字体：{(font != null ? font.name : "无")}（{why}）");
            return font;
        }

        // ---------- Play 预览 ----------
        /// Play 中把 prefab 实例化到场景里的根画布下（没有就临时建一个），供 ReskinMatrix.Snap 测量 / 截图。
        public static IEnumerator PreviewCo(string prefabPath, float refW = 768, float refH = 1664)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogError("[RESKIN] 找不到 " + prefabPath); yield break; }
            var old = ReskinCore.FindViewRoot(prefab.name);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            yield return null;
            var canvas = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode != RenderMode.WorldSpace)
                .OrderByDescending(c => c.sortingOrder).FirstOrDefault();
            if (canvas == null)
            {
                var cgo = new GameObject("ReskinPreviewCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
                var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(refW, refH); sc.matchWidthOrHeight = 0;
            }
            var inst = UnityEngine.Object.Instantiate(prefab, canvas.transform, false);
            inst.name = prefab.name;
            inst.transform.SetAsLastSibling();
            yield return null;
        }
    }
}
