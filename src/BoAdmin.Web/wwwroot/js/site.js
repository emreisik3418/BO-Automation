// Uygulama kabuğu: mobil menü
(function () {
    'use strict';

    const body = document.body;

    document.querySelectorAll('[data-sidebar-open]').forEach(el =>
        el.addEventListener('click', () => body.classList.add('sidebar-open')));

    document.querySelectorAll('[data-sidebar-close]').forEach(el =>
        el.addEventListener('click', () => body.classList.remove('sidebar-open')));

    document.addEventListener('keydown', e => {
        if (e.key === 'Escape') body.classList.remove('sidebar-open');
    });
})();
