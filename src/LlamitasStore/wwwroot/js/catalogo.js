// Catálogo y ficha de producto. Sin manejadores inline (la CSP los bloquea).
(function () {
    'use strict';

    // Enviar el formulario de orden al cambiar el <select>.
    document.querySelectorAll('form[data-autoenvio] select').forEach(function (select) {
        select.addEventListener('change', function () { select.form.submit(); });
    });

    // Galería: cambiar la imagen principal desde las miniaturas.
    var principal = document.getElementById('galeria-principal');
    if (principal) {
        document.querySelectorAll('[data-galeria-src]').forEach(function (boton) {
            boton.addEventListener('click', function () {
                principal.src = boton.getAttribute('data-galeria-src');
                principal.alt = boton.getAttribute('data-galeria-alt') || '';
                document.querySelectorAll('.ls-miniatura.activa').forEach(function (b) { b.classList.remove('activa'); });
                boton.classList.add('activa');
            });
        });
    }

    // Ficha inmersiva: al elegir una sección en el escenario, llevar la vista al detalle de esa sección.
    var detalle = document.getElementById('detalle');
    var reducirMovimiento = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    document.querySelectorAll('.ls-escenario-nav [data-bs-toggle="tab"]').forEach(function (pestana) {
        pestana.addEventListener('click', function () {
            if (!detalle) return;
            var arriba = detalle.getBoundingClientRect().top;
            // Solo desplaza si el detalle no está ya a la vista.
            if (arriba > window.innerHeight * 0.75 || arriba < 0) {
                detalle.scrollIntoView({ behavior: reducirMovimiento ? 'auto' : 'smooth', block: 'start' });
            }
        });
    });

    // Botones que abren una sección concreta (p. ej. "Ver todas las especificaciones").
    document.querySelectorAll('[data-ls-abrir-tab]').forEach(function (boton) {
        boton.addEventListener('click', function () {
            var pestana = document.querySelector(boton.getAttribute('data-ls-abrir-tab'));
            if (!pestana || !window.bootstrap) return;
            window.bootstrap.Tab.getOrCreateInstance(pestana).show();
            if (detalle) detalle.scrollIntoView({ behavior: reducirMovimiento ? 'auto' : 'smooth', block: 'start' });
            pestana.focus({ preventScroll: true });
        });
    });
})();
