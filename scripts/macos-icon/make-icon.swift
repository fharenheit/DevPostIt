// macOS 앱 아이콘을 만듭니다. 트레이 아이콘과 같은 노란 줄 메모지입니다.
// macOS 26은 규격 둥근 사각형이 아닌 아이콘을 회색 판 위에 줄여 얹으므로 메모지가 규격 영역을 채웁니다.
// 사용법: swift scripts/macos-icon/make-icon.swift <출력 폴더.iconset>
import AppKit

func render(pixels: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let transform = NSAffineTransform()
    transform.scale(by: CGFloat(pixels) / 1024)
    transform.concat()

    // macOS 아이콘 격자: 1024 안의 824 영역.
    let paper = NSBezierPath(roundedRect: NSRect(x: 100, y: 100, width: 824, height: 824), xRadius: 185, yRadius: 185)
    NSColor(srgbRed: 255 / 255, green: 232 / 255, blue: 143 / 255, alpha: 1).setFill()
    paper.fill()

    // 접힌 모서리. 바깥 모양을 바꾸지 않도록 메모지 안에 그립니다.
    paper.addClip()
    let flap = NSBezierPath()
    flap.move(to: NSPoint(x: 924, y: 330))
    flap.line(to: NSPoint(x: 694, y: 100))
    flap.line(to: NSPoint(x: 924, y: 100))
    flap.close()
    NSColor(srgbRed: 245 / 255, green: 205 / 255, blue: 90 / 255, alpha: 1).setFill()
    flap.fill()
    let crease = NSBezierPath()
    crease.move(to: NSPoint(x: 924, y: 330))
    crease.line(to: NSPoint(x: 694, y: 330))
    crease.line(to: NSPoint(x: 694, y: 100))
    crease.close()
    NSColor(srgbRed: 232 / 255, green: 186 / 255, blue: 70 / 255, alpha: 1).setFill()
    crease.fill()

    // 줄.
    NSColor(srgbRed: 91 / 255, green: 78 / 255, blue: 34 / 255, alpha: 1).setFill()
    for (y, width) in [(690, 584), (540, 584), (390, 360)] as [(CGFloat, CGFloat)] {
        NSBezierPath(roundedRect: NSRect(x: 220, y: y, width: width, height: 56), xRadius: 28, yRadius: 28).fill()
    }
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let folder = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
for size in [16, 32, 128, 256, 512] {
    try render(pixels: size).write(to: folder.appendingPathComponent("icon_\(size)x\(size).png"))
    try render(pixels: size * 2).write(to: folder.appendingPathComponent("icon_\(size)x\(size)@2x.png"))
}
