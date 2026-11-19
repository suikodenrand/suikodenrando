using SuikodenRandomizer.S1BattlePreview;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}
void CheckBottomBand(CtbStripPlacement layout, CtbStripRect area, CtbStripRect[] occupied, float scale = 1)
{
    Check(area.Contains(layout.Bounds), "Portrait strip escaped the native safe area.");
    Check(Math.Abs(layout.Bounds.Bottom - (area.Bottom + 8 * scale)) < 0.01f,
        "Portrait strip must stay on the bottom edge of the safe area.");
    foreach (var panel in occupied)
        Check(!layout.Bounds.Overlaps(panel.Inflate(8 * scale)), "Portrait strip obscured a native HUD panel.");
}

var safe = new CtbStripRect(0, 0, 1920, 1080);
CtbStripRect[] native = [new(1510, 400, 410, 680), new(190, 650, 310, 360), new(48, 70, 410, 180)];
Check(NativeCtbStripLayout.TryPlace(safe, native, 8, 34, 1, out var normal), "Standard native HUD needs a strip.");
Check(normal.CardCount == 8 && normal.PortraitSize == 64 && normal.Bounds.Height == 156,
    "Standard strip must show eight 64-unit portraits in 156 units of height.");
Check(normal.Bounds.CenterX == safe.CenterX, "Standard HUD should center the turn order.");
CheckBottomBand(normal, safe, native);

// A controller stretching over the viewport must not masquerade as the actual
// command/party frame. Actual fullscreen frame graphics are still respected.
Check(!NativeCtbStripLayout.TryPanelFootprint(safe, safe, nativeFrame: false, out _), "Fullscreen container is not a HUD footprint.");
Check(NativeCtbStripLayout.TryPanelFootprint(safe, native[0], nativeFrame: true, out var party)
    && party.Equals(native[0]), "Actual native party frame must retain its precise bounds.");
Check(NativeCtbStripLayout.TryPanelFootprint(safe, safe, nativeFrame: true, out var fullFrame)
    && !NativeCtbStripLayout.TryPlace(safe, [fullFrame], 8, 34, 1, out _), "A real fullscreen window must hide the strip.");
Check(NativeCtbStripLayout.TryPanelFootprint(safe, new(1800, 700, 410, 680), nativeFrame: true, out var partial)
    && partial.Equals(new CtbStripRect(1800, 700, 120, 380)), "Sliding windows must clip to the canvas.");
Check(!NativeCtbStripLayout.TryPanelFootprint(safe, new(2000, 700, 410, 680), nativeFrame: true, out _), "Fully off-canvas windows must not block the strip.");

// The screenshot's wide party panel occupies the upper right, above the turn order.
CtbStripRect[] screenshot = [new(1150, 400, 770, 680), new(190, 650, 210, 360), native[2]];
Check(NativeCtbStripLayout.TryPlace(safe, screenshot, 8, 34, 1, out var screenshotLayout)
    && screenshotLayout.Bounds == normal.Bounds, "Upper party and command windows must leave the bottom strip centered.");
CheckBottomBand(screenshotLayout, safe, screenshot);

CtbStripRect[] submenu = [..native, new(0, 0, 735, 240), new(1150, 0, 770, 240)];
Check(NativeCtbStripLayout.TryPlace(safe, submenu, 8, 34, 1, out var reduced), "Native submenu leaves room for a reduced strip.");
Check(reduced.CardCount < 8 && reduced.CardCount >= 3 && reduced.PortraitSize >= 56,
    "Narrow gaps should reduce card count without unreadable portraits.");
CheckBottomBand(reduced, safe, submenu);
Check(!NativeCtbStripLayout.TryPlace(safe, [new(0, 0, 1920, 280)], 8, 34, 1, out _),
    "An occupied bottom band must hide the strip.");
Check(NativeCtbStripLayout.TryPlace(safe, [new(0, 800, 1920, 280)], 8, 34, 1, out var topBlocked)
    && topBlocked.Bounds == normal.Bounds, "A window confined to the top must not shrink or move the bottom strip.");
var centerPanel = new CtbStripRect(900, 0, 120, 220);
Check(NativeCtbStripLayout.TryPlace(safe, [centerPanel], 8, 34, 1, out var shifted) && shifted.CardCount == 8,
    "The strip should use a clear side of the bottom band when a native menu covers the center.");
CheckBottomBand(shifted, safe, [centerPanel]);

// Keep the current and last settled native positions separately during a slide;
// their giant bounding union would incorrectly close the clear centre gap.
CtbStripRect[] sliding = [new(0, 0, 410, 680), new(1510, 0, 410, 680)];
Check(NativeCtbStripLayout.TryPlace(safe, sliding, 8, 34, 1, out var slideLayout)
    && slideLayout.CardCount == 8, "Disjoint window positions must not block the entire canvas.");
CheckBottomBand(slideLayout, safe, sliding);

var narrowCanvas = new CtbStripRect(0, 0, 1280, 720);
CtbStripRect[] narrowHud = [new(870, 40, 410, 680), new(190, 20, 200, 300)];
Check(NativeCtbStripLayout.TryPlace(narrowCanvas, narrowHud, 8, 34, 1, out var narrowLayout)
    && narrowLayout.CardCount < 8, "Narrow native canvas must reduce the number of cards.");
CheckBottomBand(narrowLayout, narrowCanvas, narrowHud);
var inset = new CtbStripRect(-751, -357, 1297, 701);
Check(NativeCtbStripLayout.TryPlace(inset, [], 8, 28, 1.5f, out var insetLayout)
    && insetLayout.Bounds.CenterX == inset.CenterX, "Centering must use the inset safe area on a scaled canvas.");
CheckBottomBand(insetLayout, inset, [], 1.5f);
Check(!NativeCtbStripLayout.TryPlace(new(0, 0, 180, 140), [], 8, 34, 1, out _), "Tiny viewport must hide instead of overflow.");
Check(!NativeCtbStripLayout.TryPlace(safe, [], 0, 34, 1, out _), "No scheduled actors must hide the strip.");
Check(NativeCtbStripLayout.TryPlace(safe, native, 1, 34, 1, out var single) && single.CardCount == 1, "Single scheduled actor remains supported.");
Check(NativeCtbStripLayout.CanPlaceNotice(safe, native, normal.Bounds, 1, out var notice)
    && safe.Contains(notice) && notice.Bottom > normal.Bounds.Top && notice.CenterX == normal.Bounds.CenterX,
    "The action preview must be centered above the portrait strip.");
Check(!NativeCtbStripLayout.CanPlaceNotice(safe, [notice], normal.Bounds, 1, out _), "Action preview cannot obscure a native submenu.");
Check(NativeCtbStripLayout.CanPlaceNotice(safe, native, notice, 1, out var rallyAbovePreview)
    && rallyAbovePreview.Bottom > notice.Top && rallyAbovePreview.CenterX == notice.CenterX,
    "The Rally row must sit above the action preview.");
Check(!NativeCtbStripLayout.CanPlaceNotice(safe, [rallyAbovePreview], notice, 1, out _),
    "An occupied second row must hide Rally without displacing the preview.");
Check(NativeCtbStripLayout.CanPlaceNotice(safe, [rallyAbovePreview], normal.Bounds, 1, out var previewBelowBlockedRally)
    && previewBelowBlockedRally.Equals(notice), "Blocked Rally space must preserve the first action-preview row.");
var shallow = new CtbStripRect(0, 0, 1920, 175);
Check(NativeCtbStripLayout.TryPlace(shallow, [], 8, 34, 1, out var shallowLayout)
    && !NativeCtbStripLayout.CanPlaceNotice(shallow, [], shallowLayout.Bounds, 1, out _),
    "Keep the turn strip visible when only its caption would exceed the safe area.");
Check(!NativeCtbStripLayout.TryPlace(safe, native, 8, 34, float.NaN, out _), "Invalid native scale must fail closed.");

var battleView = new CtbStripRect(-640, -360, 1280, 720);
const float badgeWidth = 44, badgeHeight = 30, gap = 6;
CtbStripRect BadgeAbove(CtbStripRect sprite)
{
    Check(NativeEnemyBadgeLayout.TryPlace(battleView, sprite, badgeWidth, badgeHeight, gap, out var badge),
        "Expected an emblem over the visible enemy.");
    Check(battleView.Contains(badge) && badge.Width == badgeWidth && badge.Height == badgeHeight,
        "The complete emblem must fit on the battle canvas.");
    Check(badge.Bottom >= sprite.Top + gap, "The emblem, including its selected outline, must clear the sprite.");
    return badge;
}

// Zombie Dragon's first extracted frame combines a 96x128 piece with a 64x64
// piece starting at (64,64). Test the full 128x128 union at several camera scales.
foreach (float zoom in new[] { 0.5f, 1f, 3f })
{
    var dragon = new CtbStripRect(-100 * zoom, -200, 128 * zoom, 128 * zoom);
    Check(BadgeAbove(dragon).CenterX == dragon.CenterX, "Center the emblem above the whole Zombie Dragon.");
}
var movingEnemy = new CtbStripRect(110, 25, 36, 48);
Check(BadgeAbove(movingEnemy).CenterX == movingEnemy.CenterX, "The emblem should follow a small enemy's position.");
BadgeAbove(new CtbStripRect(battleView.Left - 90, 10, 100, 64));
BadgeAbove(new CtbStripRect(battleView.Right - 5, 10, 100, 64));
BadgeAbove(new CtbStripRect(0, battleView.Bottom - 40, 32, 100));
Check(!NativeEnemyBadgeLayout.TryPlace(battleView, new(0, battleView.Top - 45, 128, 40),
    badgeWidth, badgeHeight, gap, out _), "When a boss reaches the top edge, never clamp the emblem onto its face.");
Check(!NativeEnemyBadgeLayout.TryPlace(battleView, new(battleView.Right + 5, 0, 100, 100),
    badgeWidth, badgeHeight, gap, out _), "An off-screen enemy must not leave an emblem at the edge.");

var random = new Random(913);
for (int test = 0; test < 5000; test++)
{
    float width = random.Next(640, 3841), height = random.Next(360, 2161);
    var area = new CtbStripRect(-width / 2, -height / 2, width, height);
    var occupied = new CtbStripRect[random.Next(0, 15)];
    for (int index = 0; index < occupied.Length; index++)
        occupied[index] = new CtbStripRect(area.Left + random.NextSingle() * width,
            area.Bottom + random.NextSingle() * height, random.Next(30, 601), random.Next(30, 701));
    if (!NativeCtbStripLayout.TryPlace(area, occupied, 8, 34, 1, out var layout)) continue;
    Check(layout.CardCount is >= 3 and <= 8 && layout.PortraitSize is >= 56 and <= 64,
        "Generated layout violated card readability limits.");
    CheckBottomBand(layout, area, occupied);
}
Console.WriteLine($"PASS {checks} compact native layout checks, including 5000 seeded viewport/HUD scenarios.");
