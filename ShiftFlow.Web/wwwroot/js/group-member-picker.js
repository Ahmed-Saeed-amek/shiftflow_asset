// Member picker shared by Groups/Create and Groups/Edit. Every node is built with textContent, so
// a user's name or email can never be interpreted as markup (the two inline copies this replaces
// interpolated both straight into innerHTML). Also adds a request-sequence guard, keyboard
// support, and a visible error state when the search request fails.
(function () {
    function initGroupMemberPicker(options) {
        var root = document.getElementById(options.rootId);
        if (!root) return;

        var search = root.querySelector('[data-member-search]');
        var results = root.querySelector('[data-member-results]');
        var chipsEl = root.querySelector('[data-member-chips]');
        var status = root.querySelector('[data-member-status]');
        var fieldName = options.fieldName || 'MemberUserIds';
        var labels = options.labels || {};
        var selected = new Map((options.initial || []).map(function (m) { return [m.id, m.label]; }));

        var timer = null;
        // Monotonic request id: a slow earlier response must never overwrite a newer one's results.
        var latestRequest = 0;

        function setStatus(text) {
            if (!status) return;
            status.textContent = text || '';
            status.hidden = !text;
        }

        function renderChips() {
            chipsEl.textContent = '';
            selected.forEach(function (label, id) {
                var chip = document.createElement('span');
                chip.className = 'badge bg-primary-subtle text-primary-emphasis border border-primary-subtle d-inline-flex align-items-center gap-1 py-2';
                // data-chip is what site.css's [data-chip] .btn-close-sm rule targets to pad this
                // glyph-sized remove button out to a ~44px touch target.
                chip.setAttribute('data-chip', '');

                var text = document.createElement('span');
                text.textContent = label;
                chip.appendChild(text);

                var hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = fieldName;
                hidden.value = id;
                chip.appendChild(hidden);

                var remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'btn-close btn-close-sm member-chip-close';
                remove.setAttribute('aria-label', (labels.remove || 'Remove') + ' ' + label);
                remove.addEventListener('click', function () { selected.delete(id); renderChips(); });
                chip.appendChild(remove);

                chipsEl.appendChild(chip);
            });
        }

        function closeResults() {
            results.classList.add('d-none');
            results.textContent = '';
            search.setAttribute('aria-expanded', 'false');
        }

        function pick(id, label) {
            selected.set(id, label);
            renderChips();
            search.value = '';
            closeResults();
            setStatus('');
            search.focus();
        }

        function renderResults(list) {
            results.textContent = '';
            list.forEach(function (u) {
                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'list-group-item list-group-item-action py-1';

                var name = document.createElement('div');
                name.className = 'fw-semibold small';
                name.textContent = u.fullName || '';
                btn.appendChild(name);

                var email = document.createElement('div');
                email.className = 'text-muted member-result-email';
                email.textContent = u.email || '';
                btn.appendChild(email);

                btn.addEventListener('click', function () { pick(u.id, u.fullName || ''); });
                results.appendChild(btn);
            });
            results.classList.remove('d-none');
            search.setAttribute('aria-expanded', 'true');
        }

        function query() {
            var q = search.value.trim();
            var requestId = ++latestRequest;
            fetch(options.searchUrl + '?q=' + encodeURIComponent(q), { headers: { Accept: 'application/json' } })
                .then(function (r) {
                    if (!r.ok) throw new Error(r.status);
                    return r.json();
                })
                .then(function (list) {
                    if (requestId !== latestRequest) return; // a newer request already answered
                    setStatus('');
                    var available = list.filter(function (u) { return !selected.has(u.id); });
                    if (available.length === 0) { closeResults(); return; }
                    renderResults(available);
                })
                .catch(function () {
                    if (requestId !== latestRequest) return;
                    closeResults();
                    setStatus(labels.searchFailed || "Couldn't load employees. Check your connection and try again.");
                });
        }

        search.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(query, 220); });
        search.addEventListener('focus', query);
        search.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                // Enter picks the first suggestion; it must never submit the surrounding form.
                e.preventDefault();
                var first = results.querySelector('button');
                if (first) first.click();
                return;
            }
            if (e.key === 'Escape') { closeResults(); return; }
            if (e.key === 'ArrowDown') {
                var firstResult = results.querySelector('button');
                if (firstResult) { e.preventDefault(); firstResult.focus(); }
            }
        });
        results.addEventListener('keydown', function (e) {
            var items = Array.prototype.slice.call(results.querySelectorAll('button'));
            var i = items.indexOf(document.activeElement);
            if (e.key === 'ArrowDown' && i > -1 && i < items.length - 1) { e.preventDefault(); items[i + 1].focus(); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); (i > 0 ? items[i - 1] : search).focus(); }
            else if (e.key === 'Escape') { closeResults(); search.focus(); }
        });
        document.addEventListener('click', function (e) {
            if (!results.contains(e.target) && e.target !== search) closeResults();
        });

        renderChips();
    }

    window.initGroupMemberPicker = initGroupMemberPicker;
})();
