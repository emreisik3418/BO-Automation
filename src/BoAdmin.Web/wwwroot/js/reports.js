// Rapor İşlemleri ekranları
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
