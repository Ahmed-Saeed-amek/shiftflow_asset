// ---------------------------------------------------------------------------
// Localized strings for JS. window.i18n is emitted by _Layout.cshtml (serialized
// from the Razor Loc.T dictionary) so nothing in here has to carry hard-coded
// English that Arabic users would still see. t() falls back to the given English
// text when the layout isn't present (e.g. the standalone Login page).
// ---------------------------------------------------------------------------
window.i18n = window.i18n || {};
window.t = function (key, fallback) {
  var v = window.i18n[key];
  return (v === undefined || v === null || v === '') ? (fallback !== undefined ? fallback : key) : v;
};
function t(key, fallback) { return window.t(key, fallback); }

// Shared HTML-escaping helper for the typeahead pickers below — every field they render
// (employee FullName/Email/EmployeeNumber, asset Name, zone Name/NameAr) comes from other
// users' server data, not the current viewer, so every interpolated value must be escaped
// before it reaches innerHTML. Exposed on window so zone-map.js and the inline partial
// scripts share one implementation instead of each rolling their own (or none).
function esc(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');}
window.esc = esc;

document.addEventListener('DOMContentLoaded',function(){
  document.querySelectorAll('.alert-dismissible').forEach(a=>setTimeout(()=>{a.style.opacity='0';setTimeout(()=>a.remove(),300)},5000));
  document.querySelectorAll('[data-confirm]').forEach(btn=>btn.addEventListener('click',e=>{if(!confirm(btn.dataset.confirm||t('confirm','Are you sure?')))e.preventDefault();}));
  if(typeof bootstrap!=='undefined') document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el=>new bootstrap.Tooltip(el));
  initToasts();
  initEmployeePickers();
  initAssetPickers();
  initZonePickers();
  initSidebar();
  initSidebarActiveFallback();
  initSidebarScrollMemory();
  initTableScrollHints();
  initFloatingAvatar();
  initLocalDates();
  initRowLinks();
});

// ---------------------------------------------------------------------------
// Clickable table rows. Mark a row `<tr class="row-link" data-href="/Assets/Details/5">`
// and this handles the rest — one delegated listener for the whole document instead of an
// inline onclick="location.href=…" repeated on every row of every list view (which also
// meant no keyboard access and no way to click a button inside the row without navigating).
//
// The primary column of the row still carries a real <a> — that is the accessible path, and
// why no aria-label is added here. The row is a mouse/touch convenience on top of it.
// ---------------------------------------------------------------------------
var ROW_LINK_IGNORE = 'a, button, input, select, textarea, label, .dropdown, [data-no-row-link]';

function rowLinkTarget(e){
  var row = e.target.closest ? e.target.closest('tr[data-href]') : null;
  if (!row) return null;
  // Anything genuinely interactive inside the row wins — its own link, a kebab menu, a
  // checkbox, an inline form control.
  var interactive = e.target.closest(ROW_LINK_IGNORE);
  if (interactive && row.contains(interactive)) return null;
  return row.getAttribute('data-href') || null;
}

function initRowLinks(){
  document.addEventListener('click', function(e){
    // Let text selection inside a row stay a selection rather than a navigation.
    var sel = window.getSelection && window.getSelection();
    if (sel && String(sel).length > 0) return;
    var href = rowLinkTarget(e);
    if (href) window.location.href = href;
  });
  document.addEventListener('keydown', function(e){
    if (e.key !== 'Enter') return;
    var href = rowLinkTarget(e);
    if (href) { e.preventDefault(); window.location.href = href; }
  });
  // tabindex is applied here rather than in every view's markup.
  document.querySelectorAll('tr[data-href]').forEach(function(row){
    if (!row.hasAttribute('tabindex')) row.setAttribute('tabindex','0');
  });
}

// ---------------------------------------------------------------------------
// Fetch helpers — every call must check r.ok and land somewhere visible on
// failure, otherwise a 500/403 shows up as an empty dropdown that reads as
// "no results" instead of "the server didn't answer".
// ---------------------------------------------------------------------------
function fetchJson(url,options){
  var opts=options||{};
  opts.headers=Object.assign({'Accept':'application/json'},opts.headers||{});
  return fetch(url,opts).then(function(r){
    if(!r.ok) throw new Error('HTTP '+r.status+' for '+url);
    return r.json();
  });
}
window.fetchJson=fetchJson;

/** Visible "couldn't load" state inside a .list-group dropdown. */
function showListError(listEl){
  if(!listEl) return;
  listEl.innerHTML='<div class="list-group-item small text-danger">'+esc(t('loadFailed',"Couldn't load — please try again."))+'</div>';
  listEl.classList.remove('d-none');
}
window.showListError=showListError;

/** Same, for a <select> whose options failed to load. */
function showSelectError(sel){
  if(!sel) return;
  sel.innerHTML='';
  var o=document.createElement('option');
  o.value='';
  o.textContent=t('loadFailed',"Couldn't load — please try again.");
  sel.appendChild(o);
  sel.disabled=true;
}
window.showSelectError=showSelectError;

// ---------------------------------------------------------------------------
// Shared keyboard behaviour for every typeahead dropdown (employee / asset /
// zone pickers and the multi-asset picker's inline script). Without this, Enter
// inside the search box submitted the surrounding form — on an order or work
// order form that means an accidental save mid-search.
//   Enter      picks the highlighted row (and never submits the form)
//   Up/Down    move the highlight
//   Escape     closes the list
// Options rendered by the caller are plain <button> elements inside listEl.
// ---------------------------------------------------------------------------
var __taSeq=0;
function typeaheadKeys(searchEl,listEl,opts){
  if(!searchEl||!listEl) return null;
  opts=opts||{};
  if(!listEl.id) listEl.id='ta-results-'+(++__taSeq);
  searchEl.setAttribute('role','combobox');
  searchEl.setAttribute('aria-autocomplete','list');
  searchEl.setAttribute('aria-controls',listEl.id);
  searchEl.setAttribute('aria-expanded','false');
  listEl.setAttribute('role','listbox');

  function isOpen(){return !listEl.classList.contains('d-none');}
  function options(){return Array.prototype.slice.call(listEl.querySelectorAll('button'));}
  function current(){return listEl.querySelector('.ta-active');}
  function highlight(el){
    options().forEach(function(o){o.classList.remove('ta-active');o.setAttribute('aria-selected','false');});
    if(!el){searchEl.removeAttribute('aria-activedescendant');return;}
    el.classList.add('ta-active');
    el.setAttribute('aria-selected','true');
    if(!el.id) el.id=listEl.id+'-opt-'+options().indexOf(el);
    searchEl.setAttribute('aria-activedescendant',el.id);
    if(el.scrollIntoView) el.scrollIntoView({block:'nearest'});
  }
  function move(delta){
    var list=options();
    if(!list.length) return;
    var idx=list.indexOf(current());
    idx=idx<0?(delta>0?0:list.length-1):(idx+delta+list.length)%list.length;
    highlight(list[idx]);
  }
  function close(){
    if(opts.onClose) opts.onClose();
    else { listEl.classList.add('d-none'); listEl.innerHTML=''; }
  }

  searchEl.addEventListener('keydown',function(e){
    if(e.key==='ArrowDown'||e.key==='Down'||e.key==='ArrowUp'||e.key==='Up'){
      e.preventDefault();
      if(!isOpen()&&opts.onOpen) opts.onOpen();
      move((e.key==='ArrowDown'||e.key==='Down')?1:-1);
    } else if(e.key==='Enter'){
      // Always swallowed: a typeahead search box is not a submit field.
      e.preventDefault();
      var el=current();
      if(el&&isOpen()) el.click();
    } else if(e.key==='Escape'||e.key==='Esc'){
      if(isOpen()){e.preventDefault();e.stopPropagation();close();}
    }
  });

  // The pickers re-render their list from scratch on every keystroke, so roles,
  // aria-expanded and any stale highlight are re-synced from the DOM itself.
  function sync(){
    searchEl.setAttribute('aria-expanded',isOpen()?'true':'false');
    options().forEach(function(o){
      o.setAttribute('role','option');
      if(!o.hasAttribute('aria-selected')) o.setAttribute('aria-selected','false');
    });
    if(!current()) searchEl.removeAttribute('aria-activedescendant');
  }
  if(window.MutationObserver) new MutationObserver(sync).observe(listEl,{attributes:true,attributeFilter:['class'],childList:true});
  sync();

  listEl.addEventListener('mousemove',function(e){
    var b=(e.target&&e.target.closest)?e.target.closest('button'):null;
    if(b&&listEl.contains(b)) highlight(b);
  });
  return {highlight:highlight,clear:function(){highlight(null);}};
}
window.typeaheadKeys=typeaheadKeys;

// ---------------------------------------------------------------------------
// Toasts — the single place TempData Success/Error feedback is rendered
// (see Views/Shared/_Toast.cshtml, included once by _Layout).
// ---------------------------------------------------------------------------
function initToasts(){
  if(typeof bootstrap==='undefined') return;
  document.querySelectorAll('#toastStack .toast').forEach(function(el){
    bootstrap.Toast.getOrCreateInstance(el,{
      autohide:el.dataset.autohide!=='false',
      delay:parseInt(el.dataset.delay||'5000',10)
    }).show();
  });
}

/** Programmatic toast, same look as the server-rendered ones. variant: 'success' | 'error'. */
window.showToast=function(message,variant){
  var stack=document.getElementById('toastStack');
  if(!stack||typeof bootstrap==='undefined') return;
  var isError=(variant==='error'||variant==='danger');
  var el=document.createElement('div');
  el.className='toast app-toast border-0 shadow '+(isError?'app-toast-error':'app-toast-success');
  el.setAttribute('role',isError?'alert':'status');
  el.setAttribute('aria-live',isError?'assertive':'polite');
  el.setAttribute('aria-atomic','true');
  el.innerHTML='<div class="d-flex"><div class="toast-body d-flex align-items-center gap-2">'+
    '<i class="bi '+(isError?'bi-exclamation-octagon-fill':'bi-check-circle-fill')+'" aria-hidden="true"></i>'+
    '<span>'+esc(message)+'</span></div>'+
    '<button type="button" class="btn-close me-2 m-auto" data-bs-dismiss="toast" aria-label="'+esc(t('close','Close'))+'"></button></div>';
  stack.appendChild(el);
  bootstrap.Toast.getOrCreateInstance(el,{autohide:!isError,delay:5000}).show();
  el.addEventListener('hidden.bs.toast',function(){el.remove();});
};

// Toggles .has-scroll-hint on every .table-responsive that currently has unscrolled content to
// the right (see site.css) — a right-edge shadow that appears only while swiping would reveal
// more and disappears once you've scrolled to the end, instead of a hint that's either always
// on (looks like a bug on tables that already fit) or never on (no clue there's more to see).
function initTableScrollHints(){
  document.querySelectorAll('.table-responsive').forEach(function(el){
    function update(){
      var hasOverflow=el.scrollWidth>el.clientWidth+1;
      var atEnd=Math.abs(el.scrollLeft)+el.clientWidth>=el.scrollWidth-2;
      el.classList.toggle('has-scroll-hint',hasOverflow&&!atEnd);
    }
    el.addEventListener('scroll',update,{passive:true});
    window.addEventListener('resize',update,{passive:true});
    update();
  });
}

// Every sidebar link is a full-page navigation, which normally resets the
// scrollable nav list back to its top on the next page. Persist the scroll
// offset per-tab (sessionStorage, not localStorage — a scroll position is
// tab-local, unlike the collapsed preference) and restore it as soon as the
// list exists, before first paint would otherwise show it at 0.
function initSidebarScrollMemory(){
  var list=document.querySelector('.sidebar-nav-scroll');
  if(!list) return;
  var KEY='sidebarScrollTop';
  var saved=sessionStorage.getItem(KEY);
  if(saved) list.scrollTop=parseInt(saved,10)||0;
  list.addEventListener('scroll',function(){sessionStorage.setItem(KEY,list.scrollTop);});
}

// The active sidebar item is set server-side from the route's controller name (see
// _Layout). This only fills in when the server marked nothing — e.g. a page under a
// controller that has no sidebar entry of its own — by longest matching path prefix.
// The previous version compared the full href (query string included) against
// window.location.href, so anything but the bare index URL highlighted nothing.
function initSidebarActiveFallback(){
  var sidebar=document.getElementById('sidebar');
  if(!sidebar||sidebar.querySelector('.nav-link.active')) return;
  var path=window.location.pathname.replace(/\/+$/,'').toLowerCase();
  var best=null,bestLen=0;
  sidebar.querySelectorAll('.nav-link[href]').forEach(function(l){
    var href=(l.getAttribute('href')||'').split('?')[0].replace(/\/+$/,'').toLowerCase();
    if(!href||href.charAt(0)!=='/') return;
    if((path===href||path.indexOf(href+'/')===0)&&href.length>bestLen){best=l;bestLen=href.length;}
  });
  if(best) best.classList.add('active');
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

// Floating AI avatar. Opening is Bootstrap's own offcanvas toggle (data-bs-toggle on the
// button in _FloatingAvatar.cshtml, target #aiDrawer) — all that's left here is keeping the
// fixed bubble out of the way of the content underneath it.
function initFloatingAvatar(){
  var el=document.getElementById('floatingAvatar');
  if(!el) return;
  // Applies at any viewport/zoom — originally mobile-only on the assumption that desktop's
  // wider layout always leaves the bubble in empty margin, but at 200% browser zoom it was
  // confirmed overlapping real content (a Dashboard stat value, an Assets row's Edit button).
  var hideTimer;
  function hideThenSettle(delayMs){
    el.classList.add('scroll-hide');
    clearTimeout(hideTimer);
    hideTimer=setTimeout(function(){el.classList.remove('scroll-hide');},delayMs);
  }
  // Capture phase, on document rather than window — a plain window-scroll listener never
  // fires for scrolling *inside* a nested container (e.g. a .table-responsive scrolled
  // horizontally on a narrow screen), so the bubble stayed fully visible and kept covering
  // whatever row action had scrolled out from under it.
  document.addEventListener('scroll',function(){hideThenSettle(500);},{passive:true,capture:true});
  // Whatever row/button happens to land under the bubble's fixed corner on a freshly loaded
  // page is just as covered as it would be mid-scroll — give the page a beat to be seen.
  hideThenSettle(1200);
}

// Dates rendered server-side are UTC and formatted dd/MM/yyyy regardless of who is looking.
// Re-render any [data-local-date] element from its ISO value in the viewer's own locale and
// timezone, leaving the server-rendered text in place as the no-JS fallback.
function initLocalDates(){
  document.querySelectorAll('[data-local-date]').forEach(function(el){
    var iso=el.getAttribute('data-local-date');
    if(!iso) return;
    var d=new Date(iso);
    if(isNaN(d.getTime())) return;
    try{
      el.textContent=d.toLocaleDateString(document.documentElement.lang||undefined,
        {year:'numeric',month:'short',day:'numeric'});
    }catch(e){/* keep the server-rendered fallback */}
  });
}

// ---------------------------------------------------------------------------
// Shared form helpers for feature views (see NOTES-frontend.md).
// ---------------------------------------------------------------------------

/**
 * Parent <select> -> child <select> cascade, one fetch per parent change.
 *   initCascadingSelect('#categoryParentSelect', '#categorySubSelect',
 *                       id => `/AssetCategories/ByParent?parentId=${id}`,
 *                       { placeholder: noneLabel, selectedId: preselectedId, onChange: updateHidden });
 * opts: placeholder (string, or null to omit the blank row; default '—'), selectedId,
 *       label(item) (default: nameAr in RTL else name), onChange(items), onFilled(items),
 *       loadInitial (default true).
 * Returns { reload(selectedId), fill(items, selectedId) }.
 */
window.initCascadingSelect=function(parentSel,childSel,urlFn,opts){
  opts=opts||{};
  var parent=(typeof parentSel==='string')?document.querySelector(parentSel):parentSel;
  var child=(typeof childSel==='string')?document.querySelector(childSel):childSel;
  if(!parent||!child) return null;
  var isRTL=document.documentElement.dir==='rtl';
  var label=opts.label||function(i){return (isRTL&&i.nameAr)?i.nameAr:i.name;};
  var placeholder=(opts.placeholder===undefined)?'—':opts.placeholder;

  function fill(items,selectedId){
    child.innerHTML='';
    child.disabled=false;
    if(placeholder!==null){
      var blank=document.createElement('option');
      blank.value='';
      blank.textContent=placeholder;
      child.appendChild(blank);
    }
    items.forEach(function(i){
      var o=document.createElement('option');
      o.value=i.id;
      o.textContent=label(i);
      if(selectedId!=null&&String(i.id)===String(selectedId)) o.selected=true;
      child.appendChild(o);
    });
    child.disabled=items.length===0;
    if(opts.onFilled) opts.onFilled(items);
  }

  function reload(selectedId){
    if(!parent.value){fill([],null);if(opts.onChange)opts.onChange([]);return Promise.resolve([]);}
    return fetchJson(urlFn(parent.value)).then(function(items){
      fill(items,selectedId);
      if(opts.onChange)opts.onChange(items);
      return items;
    }).catch(function(){
      showSelectError(child);
      if(opts.onChange)opts.onChange([]);
      return [];
    });
  }

  parent.addEventListener('change',function(){reload(null);});
  if(opts.loadInitial!==false&&parent.value) reload(opts.selectedId!=null?opts.selectedId:null);
  return {reload:reload,fill:fill};
};

/**
 * Re-fills a fix-report form (CompletionDate + spare-part rows) after a rejected POST
 * bounced the user back to a fresh GET.
 *   window.__fixFormRestore = { completionDate: '…', sparePartIds: [...], partQuantities: [...] };
 *   restoreFixForm('employeeFixPartsList');
 * The same object can be passed as the second argument instead of the global.
 * The parts <select> options arrive asynchronously, so this waits on
 * window.__sparePartsReady[containerId] (set by _SparePartPicker) before adding rows.
 */
window.restoreFixForm=function(containerId,data){
  var d=data||window.__fixFormRestore||{};
  var dateField=document.querySelector('input[name=CompletionDate]');
  if(!dateField) return;
  if(d.completionDate) dateField.value=d.completionDate;

  var ids=d.sparePartIds||[];
  var qtys=d.partQuantities||[];
  if(!ids.length) return;
  if(!containerId||!window.__sparePartsReady||!window.__sparePartsReady[containerId]) return;
  window.__sparePartsReady[containerId].then(function(){
    for(var i=0;i<ids.length;i++) window.addSparePartRow(containerId);
    var container=document.getElementById(containerId);
    if(!container) return;
    var selects=container.querySelectorAll('select[name=SparePartIds]');
    var qtyInputs=container.querySelectorAll('input[name=PartQuantities]');
    ids.forEach(function(id,i){if(selects[i])selects[i].value=id;});
    qtys.forEach(function(q,i){if(qtyInputs[i])qtyInputs[i].value=q;});
  });
};

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
      if(!list.length){results.innerHTML='<div class="list-group-item small text-muted">'+esc(results.dataset.nomatch||t('noMatches','No matches'))+'</div>';results.classList.remove('d-none');return;}
      results.innerHTML=list.map(function(u){
        var sub=[u.email,u.employeeNumber].filter(Boolean).map(esc).join(' · ');
        return '<button type="button" class="list-group-item list-group-item-action py-1" data-id="'+esc(u.id)+'" data-label="'+esc(u.fullName||'')+'">'+
               '<div class="fw-semibold small">'+esc(u.fullName||'')+'</div><div class="text-muted" style="font-size:.72rem">'+sub+'</div></button>';
      }).join('');
      results.classList.remove('d-none');
      results.querySelectorAll('[data-id]').forEach(function(b){
        b.addEventListener('click',function(){clearTimeout(timer);reqSeq++;hidden.value=b.dataset.id;search.value=b.dataset.label;hide();hidden.dispatchEvent(new Event('change',{bubbles:true}));});
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
      fetchJson(url)
        .then(function(list){if(myReq===reqSeq)render(list);})
        .catch(function(){if(myReq===reqSeq)showListError(results);});
    }
    search.addEventListener('input',function(){clearTimeout(timer);timer=setTimeout(query,220);});
    search.addEventListener('focus',function(){query();});
    typeaheadKeys(search,results,{onOpen:query,onClose:hide});
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
      if(!list.length){results.innerHTML='<div class="list-group-item small text-muted">'+esc(results.dataset.nomatch||t('noMatches','No matches'))+'</div>';results.classList.remove('d-none');return;}
      results.innerHTML=list.map(function(a){
        var label=a.assetTag+' — '+(a.name||'');
        return '<button type="button" class="list-group-item list-group-item-action py-1 text-truncate" data-id="'+esc(a.id)+'" data-label="'+esc(label)+'">'+
               '<div class="fw-semibold small text-truncate">'+esc(a.assetTag)+'</div><div class="text-muted text-truncate" style="font-size:.72rem">'+esc(a.name||'')+'</div></button>';
      }).join('');
      results.classList.remove('d-none');
      results.querySelectorAll('[data-id]').forEach(function(b){
        b.addEventListener('click',function(){clearTimeout(timer);reqSeq++;hidden.value=b.dataset.id;search.value=b.dataset.label;hide();hidden.dispatchEvent(new Event('change',{bubbles:true}));});
      });
    }
    function query(){
      var q=search.value.trim();
      hidden.value='';
      // A faster-typed later query can have its response race ahead of an earlier one still
      // in flight — track a sequence number so a stale response never overwrites a newer one.
      var myReq=++reqSeq;
      fetchJson('/Assets/Search?q='+encodeURIComponent(q))
        .then(function(list){if(myReq===reqSeq)render(list);})
        .catch(function(){if(myReq===reqSeq)showListError(results);});
    }
    search.addEventListener('input',function(){clearTimeout(timer);timer=setTimeout(query,220);});
    search.addEventListener('focus',function(){query();});
    typeaheadKeys(search,results,{onOpen:query,onClose:hide});
    document.addEventListener('click',function(e){if(!pk.contains(e.target))hide();});
    var scanBtn=pk.querySelector('[data-ap-scan]');
    if(scanBtn){
      scanBtn.addEventListener('click',function(){
        window.scanAssetQr(function(asset){
          if(!asset) return;
          // Mirrors clicking a normal search result — scanning previously only filled the
          // visible search text and re-ran the query, leaving the actual hidden AssetId empty.
          clearTimeout(timer);reqSeq++;hidden.value=asset.id;
          search.value=asset.assetTag+' — '+(asset.name||'');
          hide();
          hidden.dispatchEvent(new Event('change',{bubbles:true}));
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
    var loadFailed=false;
    function hide(){results.classList.add('d-none');results.innerHTML='';}
    function render(list){
      if(loadFailed){showListError(results);return;}
      if(!list.length){results.innerHTML='<div class="list-group-item small text-muted">'+esc(results.dataset.nomatch||t('noMatches','No matches'))+'</div>';results.classList.remove('d-none');return;}
      results.innerHTML=list.map(function(z){
        var label=(z.nameAr&&document.documentElement.dir==='rtl')?z.nameAr:z.name;
        return '<button type="button" class="list-group-item list-group-item-action py-1" data-id="'+esc(z.id)+'" data-label="'+esc(label||'')+'">'+esc(label||'')+'</button>';
      }).join('');
      results.classList.remove('d-none');
      results.querySelectorAll('[data-id]').forEach(function(b){
        b.addEventListener('click',function(){hidden.value=b.dataset.id;search.value=b.dataset.label;hide();hidden.dispatchEvent(new Event('change',{bubbles:true}));});
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
      loadFailed=false;
      if(!categorySel.value) return;
      fetchJson('/Zones/ByCategory?locationCategoryId='+encodeURIComponent(categorySel.value))
        .then(function(list){zones=list;})
        .catch(function(){loadFailed=true;showListError(results);});
    }
    categorySel.addEventListener('change',function(){loadZones(true);});
    search.addEventListener('input',function(){hidden.value='';filterAndRender();});
    search.addEventListener('focus',function(){if(zones.length||loadFailed)filterAndRender();});
    typeaheadKeys(search,results,{onOpen:filterAndRender,onClose:hide});
    document.addEventListener('click',function(e){if(!pk.contains(e.target))hide();});
    if(categorySel.value) loadZones(false);
  });
}

// ---------------------------------------------------------------------------
// jsQR is a parser only the asset pickers' "Scan QR" button ever needs, so it is
// no longer loaded by the layout on every page — it is injected (pinned version
// + SRI) the first time a scan is actually requested.
// ---------------------------------------------------------------------------
var JSQR_SRC='https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js';
var JSQR_SRI='sha384-b5Ya4Bq3qCyz39m2ISh+4DxjAIljdeFwK/BsXLuj9gugaNwAcj/ia15fxNZL9Nlx';
var jsQrLoader=null;
function loadJsQr(){
  if(typeof jsQR!=='undefined') return Promise.resolve(true);
  if(jsQrLoader) return jsQrLoader;
  jsQrLoader=new Promise(function(resolve){
    var s=document.createElement('script');
    s.src=JSQR_SRC;
    s.integrity=JSQR_SRI;
    s.crossOrigin='anonymous';
    s.onload=function(){resolve(typeof jsQR!=='undefined');};
    s.onerror=function(){jsQrLoader=null;resolve(false);};
    document.head.appendChild(s);
  });
  return jsQrLoader;
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
  if (!modalEl || !video || !statusEl || typeof bootstrap === 'undefined') {
    onResolved(null);
    return;
  }
  var messages = {
    pointCamera: t('qrPoint', statusEl.textContent),
    resolving: t('qrResolving', 'Looking up asset…'),
    notAnAsset: t('qrNotAnAsset', "That QR code isn't an asset label — keep scanning…"),
    // getUserMedia is only exposed in a "secure context" — https, or http on localhost/127.0.0.1
    // specifically. Opening the app over plain http via its LAN IP (exactly how a phone would
    // reach it to scan a physical label) is NOT secure, so navigator.mediaDevices is simply
    // undefined there and the button would otherwise look broken with zero explanation.
    insecureContext: t('qrInsecureContext', 'Camera access needs a secure connection (HTTPS), or http://localhost on this same computer — scanning over a plain http:// LAN address like this one is blocked by the browser.'),
    noSupport: t('qrNoSupport', "This browser can't access the camera."),
    denied: t('qrDenied', 'Camera access was denied — allow camera access for this site in your browser settings and try again.'),
    noCamera: t('qrNoCamera', 'No camera was found on this device.'),
    inUse: t('qrInUse', 'The camera is already in use by another app or browser tab.'),
    otherError: t('qrOtherError', 'Camera access is unavailable — check your browser/device permissions.'),
    scannerUnavailable: t('qrScannerUnavailable', "The QR scanner couldn't be loaded — check your connection and try again."),
    lookupFailed: t('qrLookupFailed', "Couldn't look that asset up — please try again.")
  };
  var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
  var canvas = document.createElement('canvas');
  var ctx = canvas.getContext('2d', { willReadFrequently: true });
  var stream = null, rafId = null, settled = false;

  function stop() {
    if (rafId) cancelAnimationFrame(rafId);
    rafId = null;
    if (stream) stream.getTracks().forEach(function (track) { track.stop(); });
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
          fetchJson('/Assets/ById?id=' + encodeURIComponent(match[1]))
            .then(function (asset) { finish(asset); })
            .catch(function () {
              // Keep the modal open so the user can retry, instead of it closing with no explanation.
              statusEl.textContent = messages.lookupFailed;
              rafId = requestAnimationFrame(tick);
            });
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

  // Manual fallback: independent of camera state, so a denied/missing/broken camera (or simply
  // not having the physical asset in front of you) still lets the task finish. The modal element
  // is reused across calls, so the search input/results wiring is attached ONCE (dataset guard,
  // same idiom as initAssetPickers/initEmployeePickers) and rebinds only which `finish` callback
  // it calls into on each new scanAssetQr invocation.
  var manualSearch = document.getElementById('assetQrManualSearch');
  var manualResults = document.getElementById('assetQrManualResults');
  if (manualSearch && manualResults) {
    manualSearch.value = '';
    manualResults.classList.add('d-none');
    manualResults.innerHTML = '';
    window.__qrManualFinish = finish;
    if (!manualSearch.dataset.qrInit) {
      manualSearch.dataset.qrInit = '1';
      var qrDebounce = null;
      function runManualSearch() {
        var q = manualSearch.value.trim();
        if (!q) { manualResults.classList.add('d-none'); manualResults.innerHTML = ''; return; }
        fetch('/Assets/Search?q=' + encodeURIComponent(q), { headers: { 'Accept': 'application/json' } })
          .then(function (r) { return r.ok ? r.json() : []; })
          .then(function (list) {
            if (!list.length) { manualResults.innerHTML = '<div class="list-group-item small text-muted">' + t('noMatches', 'No matches') + '</div>'; manualResults.classList.remove('d-none'); return; }
            manualResults.innerHTML = list.map(function (a) {
              return '<button type="button" class="list-group-item list-group-item-action py-1 text-truncate" data-id="' + esc(a.id) + '" data-tag="' + esc(a.assetTag) + '" data-name="' + esc(a.name || '') + '">' +
                     '<div class="fw-semibold small text-truncate">' + esc(a.assetTag) + '</div><div class="text-muted text-truncate" style="font-size:.72rem">' + esc(a.name || '') + '</div></button>';
            }).join('');
            manualResults.classList.remove('d-none');
            manualResults.querySelectorAll('[data-id]').forEach(function (b) {
              b.addEventListener('click', function () {
                manualResults.classList.add('d-none');
                if (window.__qrManualFinish) window.__qrManualFinish({ id: Number(b.dataset.id), assetTag: b.dataset.tag, name: b.dataset.name });
              });
            });
          })
          .catch(function () { manualResults.classList.add('d-none'); });
      }
      manualSearch.addEventListener('input', function () { clearTimeout(qrDebounce); qrDebounce = setTimeout(runManualSearch, 220); });
      document.addEventListener('click', function (e) { if (e.target !== manualSearch && !manualResults.contains(e.target)) manualResults.classList.add('d-none'); });
    }
  }

  // Open the modal before requesting the camera (not after) so a denial or "no camera" failure
  // shows an explanatory message in the modal itself instead of the button silently doing nothing.
  statusEl.textContent = messages.pointCamera;
  modal.show();
  if (window.isSecureContext === false) {
    statusEl.textContent = messages.insecureContext;
    return;
  }
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    statusEl.textContent = messages.noSupport;
    return;
  }
  loadJsQr().then(function (ready) {
    if (settled) return;
    if (!ready) { statusEl.textContent = messages.scannerUnavailable; return; }
    navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } })
      .then(function (s) {
        if (settled) { s.getTracks().forEach(function (track) { track.stop(); }); return; }
        stream = s;
        video.srcObject = s;
        video.play();
        rafId = requestAnimationFrame(tick);
      })
      .catch(function (err) {
        if (settled) return;
        var name = err && err.name;
        statusEl.textContent = name === 'NotAllowedError' || name === 'SecurityError' ? messages.denied
          : name === 'NotFoundError' || name === 'DevicesNotFoundError' ? messages.noCamera
          : name === 'NotReadableError' || name === 'TrackStartError' ? messages.inUse
          : messages.otherError;
      });
  });
};

// Filter bars: changing a dropdown applies immediately (one less click); the Filter button
// remains for the free-text search and for keyboard users.
document.addEventListener('change', function (e) {
  var sel = e.target;
  if (!(sel instanceof HTMLSelectElement)) return;
  var form = sel.closest('form.filter-bar');
  if (!form || sel.hasAttribute('data-no-autosubmit')) return;
  if (typeof form.requestSubmit === 'function') form.requestSubmit(); else form.submit();
});
