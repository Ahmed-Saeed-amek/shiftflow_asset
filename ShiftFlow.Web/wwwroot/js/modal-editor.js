// One generic "edit this row in a Bootstrap modal" wiring, shared by the small reference-data
// lists (Order Types, Work Order Block Reasons, Maintenance Action Types, Asset Categories, Asset
// Action Types). Each row's Edit button carries the record in data-* attributes instead of a long
// positional openEdit(...) argument list, so adding or reordering a field can't silently shift
// every value one slot over.
//
// initModalEditor({
//   modalId, formId, titleId, errorId,
//   titles: { create, edit },
//   urls:   { create, edit },
//   fieldMap: { datasetKey: 'elementId' },   // data-name -> dataset.name -> element
//   defaults: { datasetKey: value },         // what Create starts from
//   onApply: function (mode) { ... },        // optional, after fields are filled
//   reopen:  { mode, values, error } | null  // server-side validation redisplay
// })
(function () {
    function setField(el, value) {
        if (!el) return;
        if (el.type === 'checkbox') {
            el.checked = value === true || value === 'true' || value === 'True';
        } else {
            el.value = value === null || value === undefined ? '' : String(value);
        }
    }

    function initModalEditor(config) {
        var modalEl = document.getElementById(config.modalId);
        var form = document.getElementById(config.formId);
        if (!modalEl || !form) return;

        var titleEl = config.titleId ? document.getElementById(config.titleId) : null;
        var errorEl = config.errorId ? document.getElementById(config.errorId) : null;
        var fieldMap = config.fieldMap || {};
        var titles = config.titles || {};
        var urls = config.urls || {};

        function showError(message) {
            if (!errorEl) return;
            errorEl.textContent = message || '';
            errorEl.hidden = !message;
        }

        function apply(mode, values) {
            if (titleEl) titleEl.textContent = mode === 'edit' ? (titles.edit || '') : (titles.create || '');
            form.setAttribute('action', mode === 'edit' ? urls.edit : urls.create);
            Object.keys(fieldMap).forEach(function (key) {
                setField(document.getElementById(fieldMap[key]), values[key]);
            });
            if (typeof config.onApply === 'function') config.onApply(mode);
        }

        document.querySelectorAll('[data-modal-create="' + config.modalId + '"]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                showError('');
                apply('create', config.defaults || {});
            });
        });

        document.querySelectorAll('[data-modal-edit="' + config.modalId + '"]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                showError('');
                apply('edit', btn.dataset);
            });
        });

        // The POST redirects back to this list on a validation failure, so reopen the modal with
        // what was submitted and say what was wrong, instead of dropping the user on an unchanged
        // list with the entered values gone.
        if (config.reopen) {
            apply(config.reopen.mode || 'create', config.reopen.values || {});
            showError(config.reopen.error);
            if (window.bootstrap && window.bootstrap.Modal) {
                window.bootstrap.Modal.getOrCreateInstance(modalEl).show();
            }
        }
    }

    window.initModalEditor = initModalEditor;
})();
