// macOS Vision 文字识别：输出 JSON 行框（像素、左上原点）。对描边游戏字识别率远高于 Tesseract。
// 由 reskin_common.py 自动编译到 ~/.cache/ui-reskin/ocr，无需手动运行。
// 用法: ocr <image.png> [lang1,lang2]
import Vision; import AppKit; import Foundation
let args = CommandLine.arguments
let url = URL(fileURLWithPath: args[1])
guard let img = NSImage(contentsOf: url) else { print("[]"); exit(1) }
var r = NSRect(origin: .zero, size: img.size)
guard let cg = img.cgImage(forProposedRect: &r, context: nil, hints: nil) else { print("[]"); exit(1) }
let W = Double(cg.width), H = Double(cg.height)
let req = VNRecognizeTextRequest()
req.recognitionLevel = .accurate
req.recognitionLanguages = args.count > 2 ? args[2].split(separator: ",").map(String.init) : ["en-US", "zh-Hans"]
req.usesLanguageCorrection = false
req.minimumTextHeight = 0.006
try? VNImageRequestHandler(cgImage: cg).perform([req])
var out: [[String: Any]] = []
for o in req.results ?? [] {
  guard let c = o.topCandidates(1).first else { continue }
  let b = o.boundingBox
  out.append(["text": c.string, "conf": c.confidence,
              "rect": [b.minX * W, (1 - b.maxY) * H, b.width * W, b.height * H]])
}
let data = try! JSONSerialization.data(withJSONObject: out, options: [])
print(String(data: data, encoding: .utf8)!)
