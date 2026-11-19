// Rapor Yönetişimi ekranları
(function () {
    'use strict';

    // ------------------------------------------------------------
    // Rapor listesi: toplu seçim
    // ------------------------------------------------------------
    const selectAll = document.getElementById('selectAll');
    const bulkBtn = document.getElementById('bulkDeleteBtn');
    const countEl = document.getElementById('selectedCount');
    const rowChecks = () => Array.from(document.querySelectorAll('.row-check'));

    function refreshSelection() {
        const all = rowChecks();
        const checked = all.filter(c => c.checked).length;

        if (countEl) countEl.textContent = checked;
        if (bulkBtn) bulkBtn.disabled = checked === 0;
        if (selectAll) {
            selectAll.checked = all.length > 0 && checked === all.length;
            selectAll.indeterminate = checked > 0 && checked < all.length;
        }
    }

    if (selectAll) {
        selectAll.addEventListener('change', () => {
            rowChecks().forEach(c => { c.checked = selectAll.checked; });
            refreshSelection();
        });
    }

    document.addEventListener('change', e => {
        if (e.target.classList && e.target.classList.contains('row-check')) refreshSelection();
    });

    refreshSelection();

    // ------------------------------------------------------------
    // Klasör otomatik tamamlama (klasör listesi sayfayla birlikte yüklenmez)
    // ------------------------------------------------------------
    document.querySelectorAll('[data-folder-autocomplete]').forEach(box => {
        const input = box.querySelector('input[type="text"]');
        const hidden = box.querySelector('[data-folder-id]');
        const list = box.querySelector('.autocomplete-list');
        const url = box.dataset.url;
        let timer = null;
        let items = [];
        let active = -1;
        let lastQuery = '';

        const escapeHtml = v => String(v).replace(/[&<>"']/g, ch =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));

        function close() { list.hidden = true; active = -1; }

        function render(message) {
            if (message) {
                list.innerHTML = '<li class="autocomplete-empty">' + escapeHtml(message) + '</li>';
            } else {
                list.innerHTML = items.map((f, i) =>
                    '<li><button type="button" class="autocomplete-item' + (i === active ? ' active' : '') + '" data-index="' + i + '">' +
                    '<span class="ac-name">' + escapeHtml(f.name) + '</span>' +
                    '<span class="ac-path">' + escapeHtml(f.path) + ' · ID ' + f.id + '</span></button></li>').join('');
            }
            list.hidden = false;
        }

        function choose(i) {
            const f = items[i];
            if (!f) return;
            input.value = f.path;
            hidden.value = f.id;
            close();
        }

        async function search(q) {
            lastQuery = q;
            render('Aranıyor...');
            try {
                const res = await fetch(url + '?q=' + encodeURIComponent(q), { headers: { 'Accept': 'application/json' } });
                if (q !== lastQuery) return; // daha yeni bir arama başladı
                if (!res.ok) { render('Klasörler alınamadı.'); return; }
                items = await res.json();
                active = -1;
                if (items.length === 0) render('Eşleşen klasör yok.');
                else render();
            } catch {
                render('Klasörler alınamadı.');
            }
        }

        input.addEventListener('input', () => {
            hidden.value = ''; // yazı değişince seçim temizlenir
            clearTimeout(timer);
            const q = input.value.trim();
            if (q.length < 2) { close(); return; }
            timer = setTimeout(() => search(q), 250);
        });

        input.addEventListener('keydown', e => {
            if (list.hidden || items.length === 0) return;
            if (e.key === 'ArrowDown') { active = Math.min(active + 1, items.length - 1); render(); e.preventDefault(); }
            else if (e.key === 'ArrowUp') { active = Math.max(active - 1, 0); render(); e.preventDefault(); }
            else if (e.key === 'Enter' && active >= 0) { choose(active); e.preventDefault(); }
            else if (e.key === 'Escape') { close(); }
        });

        list.addEventListener('mousedown', e => {
            const btn = e.target.closest('.autocomplete-item');
            if (btn) { choose(Number(btn.dataset.index)); e.preventDefault(); }
        });

        input.addEventListener('blur', () => setTimeout(close, 150));
    });

    // ------------------------------------------------------------
    // Silme / toplu silme önizleme: işlem türü + sayı ile onay
    // ------------------------------------------------------------
    const form = document.getElementById('deleteForm') || document.getElementById('bulkForm');
    if (!form) return;

    // Toplu silme formunda işlem türü seçimi yok; buton metni sabit kalır
    const hasOperationChoice = form.querySelector('input[name="MoveToArchive"]') !== null;

    const confirmInput = document.getElementById('confirmCount');
    const confirmBtn = document.getElementById('confirmBtn');
    const warning = document.getElementById('deleteWarning');

    function isArchive() {
        const selected = form.querySelector('input[name="MoveToArchive"]:checked');
        return selected !== null && selected.value === 'true';
    }

    function refreshConfirm() {
        const archive = isArchive();

        if (warning) {
            warning.className = archive ? 'alert alert-info small' : 'alert alert-warning small';
            warning.textContent = archive
                ? 'Raporlar arşiv klasörüne taşınacak; gerekirse geri alınabilir.'
                : 'Kalıcı silme geri alınamaz.';
        }

        if (confirmBtn) {
            if (hasOperationChoice) {
                confirmBtn.textContent = archive ? 'Onayla ve arşive taşı' : 'Onayla ve sil';
                confirmBtn.className = archive ? 'btn btn-warning' : 'btn btn-danger';
            }
            if (confirmInput) {
                confirmBtn.disabled = confirmInput.value.trim() !== confirmInput.dataset.expected;
            }
        }
    }

    form.querySelectorAll('input[name="MoveToArchive"]').forEach(r => r.addEventListener('change', refreshConfirm));
    if (confirmInput) confirmInput.addEventListener('input', refreshConfirm);
    refreshConfirm();

    // Çift tıklamayla iki kez gönderimi engelle
    form.addEventListener('submit', () => {
        if (confirmBtn) {
            setTimeout(() => {
                confirmBtn.disabled = true;
                confirmBtn.textContent = 'İşleniyor, lütfen bekleyin...';
            }, 0);
        }
    });
})();
