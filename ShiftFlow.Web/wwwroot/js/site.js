document.addEventListener('DOMContentLoaded',function(){document.querySelectorAll('.alert-dismissible').forEach(a=>setTimeout(()=>{a.style.opacity='0';setTimeout(()=>a.remove(),300)},5000));document.querySelectorAll('[data-confirm]').forEach(btn=>btn.addEventListener('click',e=>{if(!confirm(btn.dataset.confirm||'Are you sure?'))e.preventDefault();}));document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(t=>new bootstrap.Tooltip(t));initEmployeePickers();initAssetPickers();initZonePickers();initSidebar();initSidebarScrollMemory();});

// Shared HTML-escaping helper for the typeahead pickers below — every field they render
// (employee FullName/Email/EmployeeNumber, asset Name, zone Name/NameAr) comes from other
// users' server data, not the current viewer, so every interpolated value must be escaped
// before it reaches innerHTML. A prior version escaped only the data-label attribute,
// leaving the visible text nodes open to stored DOM-XSS.
function esc(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');}

// Every sidebar link is a full-page navigation, which normally resets the
// scrollable nav list back to its top on the next page. Persist the scroll
// offset per-tab (sessionStorage, not localStorage — a scroll position is
// tab-local, unlike the collapsed preference above) and restore it as soon
// as the list exists, before first paint would otherwise show it at 0.
function initSidebarScrollMemory(){
  var list=document.querySelector('.sidebar-nav-scroll');
  if(!list) return;
  var KEY='sidebarScrollTop';
  var saved=sessionStorage.getItem(KEY);
  if(saved) list.scrollTop=parseInt(saved,10)||0;
  list.addEventListener('scroll',function(){sessionStorage.setItem(KEY,list.scrollTop);});
}

// Mobile/tablet sidebar: below 992px the sidebar is a closed-by-default
// overlay drawer (toggle button + backdrop). At 992px+ it's a static
// in-flow column that can be collapsed to free up content width; that
// choice is persisted in localStorage so it survives full-page navigation
// (this is a traditional MVC app, not an SPA — every link is a fresh load).
function initSidebar(){
  var sidebar=document.getElementById('sidebar');
  var backdrop=document.getElementById('sidebarBackdrop');
  var toggle=document.getElementById('sidebarToggle');
  if(!sidebar||!backdrop||!toggle) return;
  var mq=window.matchMedia('(max-width: 991.98px)');
  var COLLAPSED_CLASS='sidebar-collapsed-pref';
  var STORAGE_KEY='sidebarCollapsed';

  function open(){sidebar.classList.add('show');backdrop.classList.add('show');document.body.classList.add('sidebar-locked');}
  function close(){sidebar.classList.remove('show');backdrop.classList.remove('show');document.body.classList.remove('sidebar-locked');}

  function setDesktopCollapsed(collapsed){
    document.documentElement.classList.toggle(COLLAPSED_CLASS,collapsed);
    localStorage.setItem(STORAGE_KEY,collapsed?'1':'0');
  }

  toggle.addEventListener('click',function(){
    if(mq.matches){
      sidebar.classList.contains('show')?close():open();
    } else {
      setDesktopCollapsed(!document.documentElement.classList.contains(COLLAPSED_CLASS));
    }
  });
  backdrop.addEventListener('click',close);

  // Crossing the breakpoint (tablet rotation, desktop resize) should
  // never leave a stale open/backdrop/scroll-lock state behind — the
  // desktop-collapsed class is left alone since _Layout.cshtml's inline
  // head script already applies it independently of this drawer state.
  mq.addEventListener('change',close);
}

// Reusable employee picker: typeahead search by name/email against /api/users/search
function initEmployeePickers(){
  document.querySelectorAll('[data-employee-picker]').forEach(function(pk){
    if(pk.dataset.epInit) return; pk.dataset.epInit='1';
    var hidden=pk.querySelector('[data-ep-value]');
    var search=pk.querySelector('[data-ep-search]');
    var results=pk.querySelector('[data-ep-results]');
    var role=pk.dataset.role||'';
    var timer=null;
    var reqSeq=0;
    function hide(){results.classList.add('d-none');results.innerHTML='';}
    function render(list){
      if(!list.length){results.innerHTML='<div class="list-group-item small text-muted">'+esc(results.dataset.nomatch||'No matches')+'</div>';results.classList.remove('d-none');return;}
      results.innerHTML=list.map(function(u){
        var sub=[u.email,u.employeeNumber].filter(Boolean).map(esc).join(' · ');
        return '<button type="button" class="list-group-item list-group-item-action py-1" data-id="'+esc(u.id)+'" data-label="'+esc(u.fullName||'')+'">'+
               '<div class="fw-semibold small">'+esc(u.fullName||'')+'</div><div class="text-muted" style="font-size:.72rem">'+sub+'</div></button>';
      }).join('');
      results.classList.remove('d-none');
      results.querySelectorAll('[data-id]').forEach(function(b){
        b.addEventListener('click',function(){hidden.value=b.dataset.id;search.value=b.dataset.label;hide();});
      });
    }
    function query(){
      var q=search.value.trim();
      // typing invalidates a previous selection until a row is chosen
      hidden.value='';
      var url='/api/users/search?q='+encodeURIComponent(q)+(role?'&role='+encodeURIComponent(role):'');
      // A faster-typed later query can have its response race ahead of an earlier one still
      // in flight — track a sequence number so a stale response never overwrites a newer one.
      var myReq=++reqSeq;
      fetch(url,{headers:{'Accept':'application/json'}}).then(function(r){return r.ok?r.json():[];}).then(function(list){if(myReq===reqSeq)render(list);}).catch(function(){if(myReq===reqSeq)hide();});
    }
    search.addEventListener('input',function(){clearTimeout(timer);timer=setTimeout(query,220);});
    search.addEventListener('focus',function(){if(search.value.trim()!==''||true)query();});
    document.addEventListener('click',function(e){if(!pk.contains(e.target))hide();});
  });
}

// Reusable single-asset picker: typeahead search by tag/name against /Assets/Search
// (same endpoint the multi-asset picker uses) — mirrors initEmployeePickers().
function initAssetPickers(){
  document.querySelectorAll('[data-asset-picker]').forEach(function(pk){
    if(pk.dataset.apInit) return; pk.dataset.apInit='1';
    var hidden=pk.querySelector('[data-ap-value]');
    var search=pk.querySelector('[data-ap-search]');
    var results=pk.querySelector('[data-ap-results]');
    var timer=null;
    var reqSeq=0;
    function hide(){results.classList.add('d-none');results.innerHTML='';}
    function render(list){
      if(!list.length){results.innerHTML='<div class="list-group-item small text-muted">'+esc(results.dataset.nomatch||'No matches')+'</div>';results.classList.remove('d-none');return;}
      results.innerHTML=list.map(function(a){
        var label=a.assetTag+' — '+(a.name||'');
        return '<button type="button" class="list-group-item list-group-item-action py-1 text-truncate" data-id="'+esc(a.id)+'" data-label="'+esc(label)+'">'+
               '<div class="fw-semibold small text-truncate">'+esc(a.assetTag)+'</div><div class="text-muted text-truncate" style="font-size:.72rem">'+esc(a.name||'')+'</div></button>';
      }).join('');
      results.classList.remove('d-none');
      results.querySelectorAll('[data-id]').forEach(function(b){
        b.addEventListener('click',function(){hidden.value=b.dataset.id;search.value=b.dataset.label;hide();});
      });
    }
    function query(){
      var q=search.value.trim();
      hidden.value='';
      // A faster-typed later query can have its response race ahead of an earlier one still
      // in flight — track a sequence number so a stale response never overwrites a newer one.
      var myReq=++reqSeq;
      fetch('/Assets/Search?q='+encodeURIComponent(q),{headers:{'Accept':'application/json'}}).then(function(r){return r.ok?r.json():[];}).then(function(list){if(myReq===reqSeq)render(list);}).catch(function(){if(myReq===reqSeq)hide();});
    }
    search.addEventListener('input',function(){clearTimeout(timer);timer=setTimeout(query,220);});
    search.addEventListener('focus',function(){query();});
    document.addEventListener('click',function(e){if(!pk.contains(e.target))hide();});
    var scanBtn=pk.querySelector('[data-ap-scan]');
    if(scanBtn && window.scanAssetQr){
      scanBtn.addEventListener('click',function(){
        window.scanAssetQr(function(asset){
          if(!asset) return;
          search.value=asset.name;
          search.focus();
          query();
        });
      });
    }
  });
}

// Reusable Location Category -> searchable Zone picker: category select fetches that category's
// zones once (small fixed list), then typed input filters the already-fetched list client-side —
// mirrors initEmployeePickers()/initAssetPickers()'s shape but with a local filter instead of a
// server round-trip per keystroke, since /Zones/ByCategory returns the whole category upfront.
function initZonePickers(){
  document.querySelectorAll('[data-zone-combobox]').forEach(function(pk){
    if(pk.dataset.zcInit) return; pk.dataset.zcInit='1';
    var categorySel=pk.querySelector('[data-zc-category]');
    var hidden=pk.querySelector('[data-zc-value]');
    var search=pk.querySelector('[data-zc-search]');
    var results=pk.querySelector('[data-zc-results]');
    var zones=[];
    function hide(){results.classList.add('d-none');results.innerHTML='';}
    function render(list){
      if(!list.length){results.innerHTML='<div class="list-group-item small text-muted">'+esc(results.dataset.nomatch||'No matches')+'</div>';results.classList.remove('d-none');return;}
      results.innerHTML=list.map(function(z){
        var label=z.nameAr&&document.documentElement.dir==='rtl'?z.nameAr:z.name;
        return '<button type="button" class="list-group-item list-group-item-action py-1" data-id="'+esc(z.id)+'" data-label="'+esc(label||'')+'">'+esc(label||'')+'</button>';
      }).join('');
      results.classList.remove('d-none');
      results.querySelectorAll('[data-id]').forEach(function(b){
        b.addEventListener('click',function(){hidden.value=b.dataset.id;search.value=b.dataset.label;hide();});
      });
    }
    function filterAndRender(){
      var q=search.value.trim().toLowerCase();
      var matches=q?zones.filter(function(z){return (z.name||'').toLowerCase().includes(q)||(z.nameAr||'').toLowerCase().includes(q);}):zones;
      render(matches);
    }
    function loadZones(resetSelection){
      if(resetSelection){hidden.value='';search.value='';}
      zones=[];
      if(!categorySel.value) return;
      fetch('/Zones/ByCategory?locationCategoryId='+categorySel.value,{headers:{'Accept':'application/json'}})
        .then(function(r){return r.ok?r.json():[];}).then(function(list){zones=list;});
    }
    categorySel.addEventListener('change',function(){loadZones(true);});
    search.addEventListener('input',function(){hidden.value='';filterAndRender();});
    search.addEventListener('focus',function(){if(zones.length)filterAndRender();});
    document.addEventListener('click',function(e){if(!pk.contains(e.target))hide();});
    if(categorySel.value) loadZones(false);
  });
}

// Shared "Scan QR" helper for the asset pickers (_AssetSinglePicker/_AssetMultiPicker). An asset's
// printed QR code encodes a link straight to its Details page (see AssetsController.QrCode) — not
// the asset tag itself — so this extracts the numeric id out of that URL and resolves it server-side
// via /Assets/ById (respecting the caller's own asset scope) rather than trusting the raw QR text.
// window.scanAssetQr(onResolved) opens the shared modal, decodes one frame, resolves the asset, and
// calls onResolved({id, assetTag, name}) — or onResolved(null) if the modal was dismissed first.
window.scanAssetQr = function (onResolved) {
  var modalEl = document.getElementById('assetQrScanModal');
  var video = document.getElementById('assetQrScanVideo');
  var statusEl = document.getElementById('assetQrScanStatus');
  if (!modalEl || !video || !statusEl || typeof jsQR === 'undefined' || typeof bootstrap === 'undefined') {
    onResolved(null);
    return;
  }
  var messages = {
    pointCamera: statusEl.textContent,
    resolving: statusEl.dataset.resolving || 'Looking up asset…',
    notAnAsset: statusEl.dataset.notAnAsset || "That QR code isn't an asset label — keep scanning…",
    noSupport: statusEl.dataset.noSupport || "This browser can't access the camera.",
    noCamera: statusEl.dataset.noCamera || 'Camera access was denied or is unavailable — check your browser/device permissions.',
  };
  var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
  var canvas = document.createElement('canvas');
  var ctx = canvas.getContext('2d', { willReadFrequently: true });
  var stream = null, rafId = null, settled = false;

  function stop() {
    if (rafId) cancelAnimationFrame(rafId);
    rafId = null;
    if (stream) stream.getTracks().forEach(function (t) { t.stop(); });
    stream = null;
    video.srcObject = null;
  }
  function finish(result) {
    if (settled) return;
    settled = true;
    stop();
    modal.hide();
    onResolved(result);
  }
  function tick() {
    if (settled) return;
    if (video.readyState === video.HAVE_ENOUGH_DATA) {
      canvas.width = video.videoWidth; canvas.height = video.videoHeight;
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
      var code = jsQR(ctx.getImageData(0, 0, canvas.width, canvas.height).data, canvas.width, canvas.height);
      if (code && code.data) {
        var match = code.data.match(/\/Assets\/Details\/(\d+)/i);
        if (match) {
          statusEl.textContent = messages.resolving;
          fetch('/Assets/ById?id=' + match[1], { headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (asset) { finish(asset); })
            .catch(function () { finish(null); });
          return;
        }
        statusEl.textContent = messages.notAnAsset;
      }
    }
    rafId = requestAnimationFrame(tick);
  }

  modalEl.addEventListener('hidden.bs.modal', function onHidden() {
    modalEl.removeEventListener('hidden.bs.modal', onHidden);
    if (!settled) { settled = true; stop(); onResolved(null); }
  });

  // Open the modal before requesting the camera (not after) so a denial or "no camera" failure
  // shows an explanatory message in the modal itself instead of the button silently doing nothing —
  // the earlier version only opened the modal on success, so a denied permission looked like a
  // broken button with zero feedback.
  statusEl.textContent = messages.pointCamera;
  modal.show();
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    statusEl.textContent = messages.noSupport;
    return;
  }
  navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } })
    .then(function (s) {
      if (settled) { s.getTracks().forEach(function (t) { t.stop(); }); return; }
      stream = s;
      video.srcObject = s;
      video.play();
      rafId = requestAnimationFrame(tick);
    })
    .catch(function () {
      if (!settled) statusEl.textContent = messages.noCamera;
    });
};
