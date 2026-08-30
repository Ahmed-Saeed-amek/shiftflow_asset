// Leaflet helpers for the Asset Locations (Zone) map UI. Kept out of CSP-restricted
// inline scripts where practical; loaded only on Zones views, not globally.

// Kuwait-wide default center/zoom, used when no governorate/coordinates are selected yet.
const KUWAIT_CENTER = [29.3117, 47.4818];
const KUWAIT_ZOOM = 9;
const GOVERNORATE_ZOOM = 12;
const PIN_ZOOM = 15;

/** Editable single-marker map for Zone Create/Edit — two-way synced with the Lat/Lng inputs. */
function initZonePickerMap(mapElId, latInputId, lngInputId, initialLat, initialLng) {
    const latInput = document.getElementById(latInputId);
    const lngInput = document.getElementById(lngInputId);
    const startLat = initialLat ?? KUWAIT_CENTER[0];
    const startLng = initialLng ?? KUWAIT_CENTER[1];

    const map = L.map(mapElId).setView([startLat, startLng], initialLat ? PIN_ZOOM : KUWAIT_ZOOM);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors', maxZoom: 19,
    }).addTo(map);

    let marker = (initialLat && initialLng) ? L.marker([initialLat, initialLng]).addTo(map) : null;

    function setMarker(lat, lng) {
        if (marker) marker.setLatLng([lat, lng]);
        else marker = L.marker([lat, lng]).addTo(map);
        latInput.value = lat.toFixed(6);
        lngInput.value = lng.toFixed(6);
    }

    map.on('click', e => setMarker(e.latlng.lat, e.latlng.lng));

    function onCoordInputChange() {
        const lat = parseFloat(latInput.value);
        const lng = parseFloat(lngInput.value);
        if (!isNaN(lat) && !isNaN(lng)) {
            setMarker(lat, lng);
            map.setView([lat, lng], PIN_ZOOM);
        }
    }
    latInput.addEventListener('change', onCoordInputChange);
    lngInput.addEventListener('change', onCoordInputChange);

    return {
        recenter(lat, lng, zoom) { map.setView([lat, lng], zoom || GOVERNORATE_ZOOM); },
    };
}

/** Read-only single-pin map for the Zone Details page. */
function initZoneReadonlyMap(mapElId, lat, lng, popupText) {
    const map = L.map(mapElId, { zoomControl: true, dragging: true, scrollWheelZoom: false }).setView([lat, lng], PIN_ZOOM);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors', maxZoom: 19,
    }).addTo(map);
    L.marker([lat, lng]).addTo(map).bindPopup(popupText).openPopup();
}

/** Overview map for the Zones Index "Map View" — plots every zone that has coordinates. */
async function initZoneOverviewMap(mapElId, dataUrl, detailsUrlTemplate) {
    const map = L.map(mapElId).setView(KUWAIT_CENTER, KUWAIT_ZOOM);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors', maxZoom: 19,
    }).addTo(map);

    const zones = await (await fetch(dataUrl)).json();
    const markers = [];
    zones.forEach(z => {
        const marker = L.marker([z.latitude, z.longitude]).addTo(map);
        marker.bindPopup(
            `<strong>${z.name}</strong><br>${z.categoryName}<br>` +
            `${z.assetCount} asset(s)<br><a href="${detailsUrlTemplate.replace('__ID__', z.id)}">View →</a>`
        );
        markers.push(marker);
    });
    if (markers.length > 0) {
        const group = L.featureGroup(markers);
        map.fitBounds(group.getBounds().pad(0.2));
    }
}

// Marker color per work order stage — mirrors the app's _StatusBadge palette closely enough
// to be recognizable at a glance (green=done, blue=in progress, orange=blocked, gray=waiting).
const WORK_ORDER_STAGE_COLORS = {
    'Sent to Vendor': '#0d6efd',
    'Blocked': '#fd7e14',
    'Fixed - Pending Confirmation': '#ffc107',
    'Closed': '#198754',
};

function workOrderStageIcon(stage) {
    const color = WORK_ORDER_STAGE_COLORS[stage] || '#6c757d';
    return L.divIcon({
        className: '',
        html: `<div style="width:16px;height:16px;border-radius:50%;background:${color};border:2px solid #fff;box-shadow:0 0 2px rgba(0,0,0,.5)"></div>`,
        iconSize: [16, 16], iconAnchor: [8, 8], popupAnchor: [0, -8],
    });
}

// Asset status dot colors + stacking order for the Zone Overview split-view map. Assets share
// their Zone's coordinates (no per-asset lat/lng), so co-located assets stack at the exact same
// point — zIndexOffset controls which one ends up on top/clickable: Defective (red) beats
// Maintenance (yellow) beats Working (green) beats anything else.
const ASSET_STATUS_STYLE = {
    'Defective':   { color: '#dc3545', zIndexOffset: 1000 },
    'Maintenance': { color: '#ffc107', zIndexOffset: 500 },
    'Working':     { color: '#198754', zIndexOffset: 0 },
};

function assetStatusIcon(status) {
    const style = ASSET_STATUS_STYLE[status] || { color: '#6c757d', zIndexOffset: -100 };
    return {
        icon: L.divIcon({
            className: '',
            html: `<div style="width:14px;height:14px;border-radius:50%;background:${style.color};border:2px solid #fff;box-shadow:0 0 2px rgba(0,0,0,.5)"></div>`,
            iconSize: [14, 14], iconAnchor: [7, 7], popupAnchor: [0, -7],
        }),
        zIndexOffset: style.zIndexOffset,
    };
}

/** Split-view map for Zone Overview — one dot per asset, colored by status, at its Zone's
 * coordinates. Multiple assets in the same Zone stack at the same point; the zIndexOffset above
 * makes the worst status (Defective > Maintenance > Working) render on top so it's what's seen
 * and clicked first at a glance.
 *
 * Returns a handle whose setFilter({category, status}) re-draws only matching markers, so the
 * Zone Overview quick filters can narrow the map without a full reload. Only fits the map's
 * bounds to the very first (unfiltered) render — refitting on every filter change would jump the
 * viewport around distractingly for what's meant to be a quick, lightweight toggle. */
async function initZoneOverviewAssetMap(mapElId, dataUrl, assetDetailsUrlTemplate) {
    const map = L.map(mapElId).setView(KUWAIT_CENTER, KUWAIT_ZOOM);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors', maxZoom: 19,
    }).addTo(map);

    const assets = await (await fetch(dataUrl)).json();
    const layer = L.layerGroup().addTo(map);
    let hasFitBounds = false;

    function render(filter) {
        layer.clearLayers();
        const matching = assets.filter(a =>
            (!filter || !filter.category || a.categoryName === filter.category) &&
            (!filter || !filter.status || a.status === filter.status)
        );
        const markers = matching.map(a => {
            const { icon, zIndexOffset } = assetStatusIcon(a.status);
            const marker = L.marker([a.lat, a.lng], { icon, zIndexOffset });
            marker.bindPopup(
                `<strong>${a.assetTag}</strong> — ${a.name}<br>${a.status}<br>${a.zoneName}, ${a.categoryName}<br>` +
                `<a href="${assetDetailsUrlTemplate.replace('__ID__', a.id)}">View →</a>`
            );
            marker.addTo(layer);
            return marker;
        });
        if (!hasFitBounds && markers.length > 0) {
            hasFitBounds = true;
            map.fitBounds(L.featureGroup(markers).getBounds().pad(0.2));
        }
    }

    render(null);
    return { setFilter: render };
}

/** Nudges markers that land on the exact same coordinates into a small ring around that point
 * so every one of them stays individually clickable — without this, a Leaflet marker placed
 * directly on top of another is entirely unreachable (no click-through, no popup cycling), which
 * silently hides every work order but the last one drawn at a shared Zone. ~15m radius at Kuwait's
 * latitude — small enough to still read as "the same location" at any zoom level that matters here. */
function spreadCoincidentPoints(items, getLat, getLng) {
    const seen = new Map();
    return items.map(item => {
        const key = `${getLat(item)},${getLng(item)}`;
        const count = seen.get(key) || 0;
        seen.set(key, count + 1);
        if (count === 0) return { item, lat: getLat(item), lng: getLng(item) };
        const angle = (count - 1) * (2 * Math.PI / 6);
        const r = 0.00015;
        return { item, lat: getLat(item) + r * Math.sin(angle), lng: getLng(item) + r * Math.cos(angle) };
    });
}

/** Overview map for the Vendor Portal "Map View" — plots every work order assigned to the
 * signed-in vendor, at its asset's Zone location, color-coded by stage. Work orders that share a
 * Zone are spread into a small ring (see spreadCoincidentPoints) so each stays clickable. */
async function initVendorWorkOrderMap(mapElId, dataUrl, detailsUrlTemplate) {
    const map = L.map(mapElId).setView(KUWAIT_CENTER, KUWAIT_ZOOM);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors', maxZoom: 19,
    }).addTo(map);

    const workOrders = await (await fetch(dataUrl)).json();
    const placed = spreadCoincidentPoints(workOrders, w => w.latitude, w => w.longitude);
    const markers = [];
    placed.forEach(({ item: w, lat, lng }) => {
        const marker = L.marker([lat, lng], { icon: workOrderStageIcon(w.stage) }).addTo(map);
        marker.bindPopup(
            `<strong>${w.workOrderNumber}</strong> — ${w.stage}<br>` +
            `${w.assetTag} — ${w.assetName}<br>${w.zoneName}, ${w.categoryName}<br>` +
            `<a href="${detailsUrlTemplate.replace('__ID__', w.id)}">View →</a>`
        );
        markers.push(marker);
    });
    if (markers.length > 0) {
        const group = L.featureGroup(markers);
        map.fitBounds(group.getBounds().pad(0.2));
    }
}
