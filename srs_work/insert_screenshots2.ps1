Add-Type -AssemblyName System.Drawing

$dash = [string][char]0x2014

$root = "C:\Users\AHMEDSAEED\OneDrive - AMEK\Desktop\shiftflow_asset\srs_work\unpacked"
$docPath = Join-Path $root "word\document.xml"
$mediaDir = Join-Path $root "word\media"

$content = Get-Content -Raw -Encoding UTF8 $docPath

function Get-EmuSize($pngPath, $targetWidthEmu) {
    $img = [System.Drawing.Image]::FromFile($pngPath)
    $w = $img.Width; $h = $img.Height
    $img.Dispose()
    $cy = [math]::Round($targetWidthEmu * $h / $w)
    return @{ cx = $targetWidthEmu; cy = $cy }
}

$targetWidthLandscape = 5762625
$targetWidthPortrait  = 3200400

# headingPrefix = ASCII-only prefix unique to that heading's <w:t> (avoids embedding non-ASCII in this script).
$items = @(
    @{ headingPrefix = "<w:t>5. Functional Requirements"; img = "image15.png"; rId = "rId25"; caption = "Screenshot 4.1 $dash Login screen"; width = $targetWidthPortrait }
    @{ headingPrefix = "<w:t>6. Functional Requirements"; img = "image16.png"; rId = "rId26"; caption = "Screenshot 5.1 $dash Shift schedule (Shift Maker) view"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>7. Functional Requirements"; img = "image17.png"; rId = "rId27"; caption = "Screenshot 6.1 $dash Live Shift Dashboard (Shift Operations)"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>8. Functional Requirements"; img = "image18.png"; rId = "rId28"; caption = "Screenshot 7.1 $dash Change Requests list"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>9. Functional Requirements"; img = "image19.png"; rId = "rId29"; caption = "Screenshot 8.1 $dash Shift Operations Dashboard (Executive Dashboard)"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>10. Functional Requirements"; img = "image20.png"; rId = "rId30"; caption = "Screenshot 9.1 $dash AI Assistant conversation interface"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>11. Functional Requirements"; img = "image21.png"; rId = "rId31"; caption = "Screenshot 10.1 $dash Asset registry (Assets list)"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>12. Functional Requirements"; img = "image22.png"; rId = "rId32"; caption = "Screenshot 11.1 $dash Work order detail with stage pipeline"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>13. Functional Requirements"; img = "image23.png"; rId = "rId33"; caption = "Screenshot 12.1 $dash Vendor Portal $dash My Work Orders"; width = $targetWidthLandscape }
    @{ headingPrefix = "<w:t>14. Localization"; img = "image24.png"; rId = "rId34"; caption = "Screenshot 13.1 $dash Preventive Maintenance contract with generated schedule"; width = $targetWidthLandscape }
)

$docPrIdBase = 900000001

for ($i = 0; $i -lt $items.Count; $i++) {
    $item = $items[$i]

    $firstIdx = $content.IndexOf($item.headingPrefix)
    if ($firstIdx -lt 0) { throw "Heading not found: $($item.headingPrefix)" }
    $secondIdx = $content.IndexOf($item.headingPrefix, $firstIdx + 1)
    if ($secondIdx -lt 0) { throw "Second occurrence not found: $($item.headingPrefix)" }
    $thirdIdx = $content.IndexOf($item.headingPrefix, $secondIdx + 1)
    if ($thirdIdx -ge 0) { throw "Ambiguous: 3+ occurrences for $($item.headingPrefix)" }

    $pStart = $content.LastIndexOf("<w:p ", $secondIdx)
    if ($pStart -lt 0) { throw "Paragraph start not found for: $($item.headingPrefix)" }

    # sanity: the found paragraph must actually be a Heading1 paragraph
    $checkSnippet = $content.Substring($pStart, 250)
    if ($checkSnippet -notmatch 'w:pStyle w:val="Heading1"') {
        throw "Paragraph at $pStart for '$($item.headingPrefix)' is not a Heading1 paragraph: $checkSnippet"
    }

    $imgPath = Join-Path $mediaDir $item.img
    $size = Get-EmuSize -pngPath $imgPath -targetWidthEmu $item.width
    $cx = $size.cx
    $cy = $size.cy
    $docPrId = $docPrIdBase + $i

    $captionEsc = $item.caption -replace '&', '&amp;'

    $xml = "<w:p><w:pPr><w:spacing w:before=`"120`" w:after=`"60`"/><w:jc w:val=`"center`"/></w:pPr><w:r><w:rPr><w:noProof/></w:rPr><w:drawing><wp:inline distT=`"0`" distB=`"0`" distL=`"0`" distR=`"0`"><wp:extent cx=`"$cx`" cy=`"$cy`"/><wp:effectExtent l=`"0`" t=`"0`" r=`"0`" b=`"0`"/><wp:docPr id=`"$docPrId`" name=`"Picture $docPrId`"/><wp:cNvGraphicFramePr><a:graphicFrameLocks xmlns:a=`"http://schemas.openxmlformats.org/drawingml/2006/main`" noChangeAspect=`"1`"/></wp:cNvGraphicFramePr><a:graphic xmlns:a=`"http://schemas.openxmlformats.org/drawingml/2006/main`"><a:graphicData uri=`"http://schemas.openxmlformats.org/drawingml/2006/picture`"><pic:pic xmlns:pic=`"http://schemas.openxmlformats.org/drawingml/2006/picture`"><pic:nvPicPr><pic:cNvPr id=`"0`" name=`"`"/><pic:cNvPicPr><a:picLocks noChangeAspect=`"1`" noChangeArrowheads=`"1`"/></pic:cNvPicPr></pic:nvPicPr><pic:blipFill><a:blip r:embed=`"$($item.rId)`"/><a:srcRect/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr bwMode=`"auto`"><a:xfrm><a:off x=`"0`" y=`"0`"/><a:ext cx=`"$cx`" cy=`"$cy`"/></a:xfrm><a:prstGeom prst=`"rect`"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p><w:p><w:pPr><w:spacing w:before=`"60`" w:after=`"220`"/><w:jc w:val=`"center`"/></w:pPr><w:r><w:rPr><w:i/><w:iCs/><w:color w:val=`"595959`"/><w:sz w:val=`"19`"/><w:szCs w:val=`"19`"/></w:rPr><w:t>$captionEsc</w:t></w:r></w:p>"

    $items[$i].insertAt = $pStart
    $items[$i].xml = $xml
}

[int[]]$insertAtValues = for ($i = 0; $i -lt $items.Count; $i++) { [int]$items[$i].insertAt }
[int[]]$order = [Array]::CreateInstance([int], $items.Count)
for ($i = 0; $i -lt $items.Count; $i++) { $order[$i] = $i }
[Array]::Sort($insertAtValues, $order)
[Array]::Reverse($order)
foreach ($idx in $order) {
    $it = $items[$idx]
    $content = $content.Substring(0, [int]$it.insertAt) + $it.xml + $content.Substring([int]$it.insertAt)
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($docPath, $content, $utf8NoBom)
Write-Host "Inserted $($items.Count) screenshots into document.xml"
