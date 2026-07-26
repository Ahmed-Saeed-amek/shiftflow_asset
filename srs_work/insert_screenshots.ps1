Add-Type -AssemblyName System.Drawing

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

# Each entry: heading text of the NEXT section (insertion point = right before that heading's paragraph),
# the image file, rId, and caption.
$targetWidthLandscape = 5762625   # matches existing figures (~6.3in)
$targetWidthPortrait  = 3200400   # narrower for the portrait login screenshot (~3.5in)

$items = @(
    @{ nextHeading = "5. Functional Requirements — Shift Scheduling"; img = "image15.png"; rId = "rId25"; caption = "Screenshot 4.1 — Login screen"; width = $targetWidthPortrait }
    @{ nextHeading = "6. Functional Requirements — Shift Operations"; img = "image16.png"; rId = "rId26"; caption = "Screenshot 5.1 — Shift schedule (Shift Maker) view"; width = $targetWidthLandscape }
    @{ nextHeading = "7. Functional Requirements — Change Requests"; img = "image17.png"; rId = "rId27"; caption = "Screenshot 6.1 — Live Shift Dashboard (Shift Operations)"; width = $targetWidthLandscape }
    @{ nextHeading = "8. Functional Requirements — Reports, Dashboard &amp; Analytics"; img = "image18.png"; rId = "rId28"; caption = "Screenshot 7.1 — Change Requests list"; width = $targetWidthLandscape }
    @{ nextHeading = "9. Functional Requirements — AI Assistant"; img = "image19.png"; rId = "rId29"; caption = "Screenshot 8.1 — Shift Operations Dashboard (Executive Dashboard)"; width = $targetWidthLandscape }
    @{ nextHeading = "10. Functional Requirements — Asset Management"; img = "image20.png"; rId = "rId30"; caption = "Screenshot 9.1 — AI Assistant conversation interface"; width = $targetWidthLandscape }
    @{ nextHeading = "11. Functional Requirements — Work Order &amp; Vendor-Driven Repair Workflow"; img = "image21.png"; rId = "rId31"; caption = "Screenshot 10.1 — Asset registry (Assets list)"; width = $targetWidthLandscape }
    @{ nextHeading = "12. Functional Requirements — Vendor Portal"; img = "image22.png"; rId = "rId32"; caption = "Screenshot 11.1 — Work order detail with stage pipeline"; width = $targetWidthLandscape }
    @{ nextHeading = "13. Functional Requirements — Contracts &amp; Vendor Management"; img = "image23.png"; rId = "rId33"; caption = "Screenshot 12.1 — Vendor Portal — My Work Orders"; width = $targetWidthLandscape }
    @{ nextHeading = "14. Localization"; img = "image24.png"; rId = "rId34"; caption = "Screenshot 13.1 — Preventive Maintenance contract with generated schedule"; width = $targetWidthLandscape }
)

$docPrIdBase = 900000001

for ($i = 0; $i -lt $items.Count; $i++) {
    $item = $items[$i]
    $headingSearch = "<w:t>$($item.nextHeading)</w:t>"

    # Find the SECOND occurrence (the real body heading, not the TOC one)
    $firstIdx = $content.IndexOf($headingSearch)
    if ($firstIdx -lt 0) { throw "Heading not found: $($item.nextHeading)" }
    $secondIdx = $content.IndexOf($headingSearch, $firstIdx + 1)
    if ($secondIdx -lt 0) { throw "Second occurrence not found: $($item.nextHeading)" }

    # Walk back to the start of the enclosing paragraph <w:p ...>
    $pStart = $content.LastIndexOf("<w:p ", $secondIdx)
    if ($pStart -lt 0) { throw "Paragraph start not found for: $($item.nextHeading)" }

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

# Insert from the LAST position to the FIRST so earlier offsets remain valid.
$sorted = $items | Sort-Object -Property insertAt -Descending
foreach ($it in $sorted) {
    $content = $content.Substring(0, $it.insertAt) + $it.xml + $content.Substring($it.insertAt)
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($docPath, $content, $utf8NoBom)
Write-Host "Inserted $($items.Count) screenshots into document.xml"
